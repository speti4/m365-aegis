using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Exchange.Sidecar;

namespace M365Aegis.Core.Exchange;

/// <summary>What the connect probe of task 009 measured: no token, no tenant identifier, timings and facts only.</summary>
/// <param name="ConnectWith">The parameters the connect passed besides the token.</param>
/// <param name="Pwsh">The pwsh version.</param>
/// <param name="Module">The ExchangeOnlineManagement version.</param>
/// <param name="StartMs">From starting pwsh to the sidecar's <c>hello</c>.</param>
/// <param name="ImportMs">The module's import inside the sidecar.</param>
/// <param name="ConnectMs">The <c>Connect-ExchangeOnline -AccessToken</c> call.</param>
/// <param name="FirstCmdletMs">A first <c>Get-EXOMailbox -ResultSize 1</c> after the connect, end to end.</param>
/// <param name="ReconnectMs">A <c>reconnect</c> with a freshly asked token, end to end (the renewal path).</param>
/// <param name="AfterReconnectMs">The same cmdlet after the reconnect.</param>
/// <param name="TokenMinutesLeft">How long the first token had left when the connect began.</param>
/// <param name="TokenPermissions">The delegated permissions the Exchange Online token carries.</param>
public sealed record ConnectProbeReport(
    string ConnectWith,
    string Pwsh,
    string? Module,
    long StartMs,
    long ImportMs,
    long ConnectMs,
    long FirstCmdletMs,
    long ReconnectMs,
    long AfterReconnectMs,
    double TokenMinutesLeft,
    IReadOnlyList<string> TokenPermissions)
{
    /// <summary>Whether one token carries both permissions the Exchange backends need.</summary>
    public bool CarriesManageAndManageV2 =>
        TokenPermissions.Contains("Exchange.Manage", StringComparer.OrdinalIgnoreCase)
        && TokenPermissions.Contains("Exchange.ManageV2", StringComparer.OrdinalIgnoreCase);
}

/// <summary>One call of the Admin API probe: the answer's status and shape, never its values.</summary>
/// <param name="Endpoint">The endpoint (<c>OrganizationConfig</c>, <c>Mailbox</c>).</param>
/// <param name="AnchorForm">The form of the <c>X-AnchorMailbox</c> header (<c>UPN:</c> or <c>AAD-UPN:</c>), without the account.</param>
/// <param name="Status">The HTTP status, or null when nothing answered.</param>
/// <param name="ElapsedMs">How long the call took.</param>
/// <param name="Items">The items of a <c>value</c> list, or null when the answer is one object.</param>
/// <param name="Shape">The property names and JSON types of the answer (of its first item for a list).</param>
/// <param name="NextLinkParameters">The query parameter names of <c>@odata.nextLink</c>, or null when there is none.</param>
/// <param name="ErrorCode">The service's error code, or null.</param>
/// <param name="ErrorMessage">The service's error message, or the network error, or null.</param>
public sealed record AdminApiCall(
    string Endpoint,
    string AnchorForm,
    int? Status,
    long ElapsedMs,
    int? Items,
    IReadOnlyList<string> Shape,
    IReadOnlyList<string>? NextLinkParameters,
    string? ErrorCode,
    string? ErrorMessage);

/// <summary>What the Admin API probe of task 010 found.</summary>
/// <param name="Answered">Whether <c>OrganizationConfig</c> answered 200 with the broker's token.</param>
/// <param name="TokenPermissions">The delegated permissions the Exchange Online token carries.</param>
/// <param name="Calls">The calls, in the order they were made.</param>
public sealed record AdminApiProbeReport(bool Answered, IReadOnlyList<string> TokenPermissions, IReadOnlyList<AdminApiCall> Calls);

/// <summary>What the Admin API probe reads beyond the organization (task 042); nothing more when both are null.</summary>
/// <param name="Mailbox">A mailbox whose delegates by name and whose folder permissions are read.</param>
/// <param name="Folder">The folder of that mailbox (<c>Calendar</c> when null), as the mailbox's language names it.</param>
/// <param name="Group">A distribution group whose first members are read.</param>
public sealed record AdminApiProbeTargets(string? Mailbox = null, string? Folder = null, string? Group = null);

/// <summary>
/// The two live probes of the Exchange bundle, behind the hidden <c>probe</c> command: the sidecar's connect with
/// the broker's token and its renewal (task 009), and one read of the Exchange Online Admin API v2.0 (task 010).
/// Both print timings, permissions, statuses and shapes; no token, no tenant identifier, no value of an answer.
/// </summary>
public static class ExchangeProbes
{
    /// <summary>The Admin API's base URL in the commercial cloud.</summary>
    public const string AdminApiBase = "https://outlook.office365.com/adminapi/v2.0/";

    /// <summary>Connects, runs a first cmdlet, renews the token with a <c>reconnect</c>, runs the cmdlet again.</summary>
    public static async Task<ConnectProbeReport> ConnectAsync(ExchangeSession session, ExchangeConnectWith connectWith, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clock);
        SidecarConnected connected = await session.ConnectAsync(cancellationToken).ConfigureAwait(false);
        ExchangeToken token = session.Token!;
        double minutesLeft = Math.Round((token.ExpiresOn - clock.GetUtcNow()).TotalMinutes, 1);
        var sample = new ExoCommand(ExoCmdlets.GetMailbox)
        {
            Parameters = new Dictionary<string, object?> { ["ResultSize"] = 1 },
            Properties = ["RecipientTypeDetails"],
        };

        var stopwatch = Stopwatch.StartNew();
        await session.InvokeAsync(sample, cancellationToken).ConfigureAwait(false);
        long first = stopwatch.ElapsedMilliseconds;
        stopwatch.Restart();
        await session.RenewAsync(cancellationToken).ConfigureAwait(false);
        long reconnect = stopwatch.ElapsedMilliseconds;
        stopwatch.Restart();
        await session.InvokeAsync(sample, cancellationToken).ConfigureAwait(false);
        long after = stopwatch.ElapsedMilliseconds;

        SidecarHello hello = session.Hello!;
        return new ConnectProbeReport(
            connectWith.ToString(),
            hello.Pwsh,
            hello.Module,
            hello.StartMs,
            connected.ImportMs,
            connected.ConnectMs,
            first,
            reconnect,
            after,
            minutesLeft,
            token.Permissions);
    }

    /// <summary>
    /// <c>POST adminapi/v2.0/{tenant}/OrganizationConfig</c> with <c>X-AnchorMailbox: UPN:&lt;account&gt;</c> (and
    /// <c>AAD-UPN:</c> when that is refused), then one page of <c>Mailbox</c> (<c>ResultSize</c> 1) for the shape of
    /// <c>@odata.nextLink</c>. Every call is a read; the undocumented <c>adminapi/beta/InvokeCommand</c> is never
    /// called (D-004).
    /// </summary>
    public static Task<AdminApiProbeReport> AdminApiAsync(HttpMessageInvoker http, ExchangeToken token, CancellationToken cancellationToken) =>
        AdminApiAsync(http, token, new AdminApiProbeTargets(), cancellationToken);

    /// <summary>
    /// The probe of task 010, and for task 042 the shapes of the reads the backend makes: one mailbox with its Send on
    /// Behalf delegates by display name, the permissions of one of its folders, and a distribution group's members.
    /// The shape of a list's first entry is printed as its pattern (every letter an <c>a</c>, every digit a <c>9</c>),
    /// so the form of an entry shows and its value does not.
    /// </summary>
    public static async Task<AdminApiProbeReport> AdminApiAsync(HttpMessageInvoker http, ExchangeToken token, AdminApiProbeTargets targets, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targets);
        AdminApiProbeReport report = await OrganizationProbeAsync(http, token, cancellationToken).ConfigureAwait(false);
        string tenant = token.TenantId!;
        string anchor = report.Calls.LastOrDefault(call => call.Endpoint == "OrganizationConfig" && call.Status is >= 200 and < 300)?.AnchorForm ?? "UPN:";
        var calls = report.Calls.ToList();
        if (targets.Mailbox is { } mailbox)
        {
            string delegates = AdminApiClient.Body(new ExoCommand(AdminApiCmdlets.GetMailbox)
            {
                Parameters = new Dictionary<string, object?> { ["Identity"] = mailbox, ["IncludeGrantSendOnBehalfToWithDisplayNames"] = true },
            });
            calls.Add(await CallAsync(http, token, tenant, "Mailbox", anchor, delegates, cancellationToken).ConfigureAwait(false));
            string folder = AdminApiClient.Body(new ExoCommand(AdminApiCmdlets.GetMailboxFolderPermission)
            {
                Parameters = new Dictionary<string, object?> { ["Identity"] = $"{mailbox}:\\{targets.Folder ?? "Calendar"}" },
            });
            calls.Add(await CallAsync(http, token, tenant, "MailboxFolderPermission", anchor, folder, cancellationToken).ConfigureAwait(false));
        }

        if (targets.Group is { } group)
        {
            string members = AdminApiClient.Body(new ExoCommand(AdminApiCmdlets.GetDistributionGroupMember)
            {
                Parameters = new Dictionary<string, object?> { ["Identity"] = group, ["ResultSize"] = 5 },
            });
            calls.Add(await CallAsync(http, token, tenant, "DistributionGroupMember", anchor, members, cancellationToken).ConfigureAwait(false));
        }

        return report with { Calls = calls };
    }

    private static async Task<AdminApiProbeReport> OrganizationProbeAsync(HttpMessageInvoker http, ExchangeToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(token);
        string tenant = token.TenantId ?? throw new InvalidOperationException("the Exchange Online token names no tenant");
        var calls = new List<AdminApiCall>();
        const string organizationConfig = """{"CmdletInput":{"CmdletName":"Get-OrganizationConfig"}}""";
        AdminApiCall first = await CallAsync(http, token, tenant, "OrganizationConfig", "UPN:", organizationConfig, cancellationToken).ConfigureAwait(false);
        calls.Add(first);
        string anchor = "UPN:";
        if (first.Status is not (>= 200 and < 300))
        {
            AdminApiCall second = await CallAsync(http, token, tenant, "OrganizationConfig", "AAD-UPN:", organizationConfig, cancellationToken).ConfigureAwait(false);
            calls.Add(second);
            anchor = second.Status is >= 200 and < 300 ? "AAD-UPN:" : anchor;
        }

        const string mailbox = """{"CmdletInput":{"CmdletName":"Get-Mailbox","Parameters":{"ResultSize":1}}}""";
        calls.Add(await CallAsync(http, token, tenant, "Mailbox", anchor, mailbox, cancellationToken).ConfigureAwait(false));
        return new AdminApiProbeReport(calls.Any(call => call.Endpoint == "OrganizationConfig" && call.Status is >= 200 and < 300), token.Permissions, calls);
    }

    private static async Task<AdminApiCall> CallAsync(
        HttpMessageInvoker http, ExchangeToken token, string tenant, string endpoint, string anchorForm, string body, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{AdminApiBase}{Uri.EscapeDataString(tenant)}/{endpoint}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Headers.TryAddWithoutValidation("X-AnchorMailbox", anchorForm + token.Account);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            long elapsed = stopwatch.ElapsedMilliseconds;
            return Describe(endpoint, anchorForm, (int)response.StatusCode, elapsed, text);
        }
        catch (HttpRequestException exception)
        {
            return new AdminApiCall(endpoint, anchorForm, null, stopwatch.ElapsedMilliseconds, null, [], null, null, exception.Message);
        }
    }

    /// <summary>An answer as its status and shape: the property names and their JSON types, never a value.</summary>
    internal static AdminApiCall Describe(string endpoint, string anchorForm, int status, long elapsedMs, string body)
    {
        JsonElement root;
        try
        {
            using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new AdminApiCall(endpoint, anchorForm, status, elapsedMs, null, [$"(not JSON, {body.Length} characters)"], null, null, null);
        }

        string? errorCode = null;
        string? errorMessage = null;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.Object)
        {
            errorCode = error.TryGetProperty("code", out JsonElement code) ? code.ToString() : null;
            errorMessage = error.TryGetProperty("message", out JsonElement message) ? message.ToString() : null;
        }

        int? items = null;
        var shape = new List<string>();
        if (root.ValueKind == JsonValueKind.Object)
        {
            shape.AddRange(Shape(root, ""));
            if (root.TryGetProperty("value", out JsonElement value) && value.ValueKind == JsonValueKind.Array)
            {
                items = value.GetArrayLength();
                if (items > 0 && value[0].ValueKind == JsonValueKind.Object)
                {
                    shape.AddRange(Shape(value[0], "value[0]."));
                    shape.AddRange(Inner(value[0], "value[0]."));
                }
            }
        }
        else
        {
            shape.Add($"(root: {Kind(root)})");
        }

        IReadOnlyList<string>? nextLink = null;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("@odata.nextLink", out JsonElement link) && link.ValueKind == JsonValueKind.String
            && Uri.TryCreate(link.GetString(), UriKind.Absolute, out Uri? uri))
        {
            nextLink = uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => Uri.UnescapeDataString(pair.Split('=')[0]))
                .ToList();
        }

        return new AdminApiCall(endpoint, anchorForm, status, elapsedMs, items, shape, nextLink, errorCode, errorMessage);
    }

    private static IEnumerable<string> Shape(JsonElement element, string prefix) =>
        element.EnumerateObject().Select(property => string.Create(CultureInfo.InvariantCulture, $"{prefix}{property.Name}: {Kind(property.Value)}"));

    /// <summary>
    /// What an item's nested values look like (task 042): an object's property names, and a list's first entry as
    /// its kind and, for a text, its pattern.
    /// </summary>
    private static IEnumerable<string> Inner(JsonElement element, string prefix)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            JsonElement value = property.Value;
            if (value.ValueKind == JsonValueKind.Object)
            {
                yield return $"{prefix}{property.Name}: object {{{string.Join(",", value.EnumerateObject().Select(inner => inner.Name))}}}";
            }
            else if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0)
            {
                JsonElement first = value[0];
                yield return first.ValueKind switch
                {
                    JsonValueKind.String => $"{prefix}{property.Name}[0]: string '{Pattern(first.GetString() ?? "")}'",
                    JsonValueKind.Object => $"{prefix}{property.Name}[0]: object {{{string.Join(",", first.EnumerateObject().Select(inner => inner.Name))}}}",
                    _ => $"{prefix}{property.Name}[0]: {Kind(first)}",
                };
            }
        }
    }

    /// <summary>A text as its pattern: a run of letters an <c>a</c>, a run of digits a <c>9</c>, the rest as it is.</summary>
    internal static string Pattern(string text)
    {
        var pattern = new System.Text.StringBuilder();
        foreach (char character in text)
        {
            char mark = char.IsLetter(character) ? 'a' : char.IsDigit(character) ? '9' : character;
            if ((mark is 'a' or '9') && pattern.Length > 0 && pattern[^1] == mark)
            {
                continue;
            }

            pattern.Append(mark);
        }

        return pattern.ToString();
    }

    private static string Kind(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "object",
        JsonValueKind.Array => $"array[{value.GetArrayLength()}]",
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        _ => "null",
    };
}
