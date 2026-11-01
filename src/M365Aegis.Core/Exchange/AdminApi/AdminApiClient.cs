using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Exchange.AdminApi;

/// <summary>Where the Admin API calls go: the tenant's id in the URL and the routing hint of every call.</summary>
/// <param name="TenantId">The tenant's id (a GUID), the URL's segment after <c>v2.0</c>.</param>
/// <param name="Account">The signed-in administrator's user principal name, sent as <c>X-AnchorMailbox: UPN:&lt;account&gt;</c>.</param>
public sealed record AdminApiTarget(string TenantId, string Account);

/// <summary>
/// The Exchange Online Admin API v2.0 backend (D-004's second layer, task 042) behind the interface the sidecar
/// implements (<see cref="IExoCmdletRunner"/>), so a lookup asks either one for a cmdlet's objects in the same shape.
/// A cmdlet of <see cref="AdminApiCmdlets"/> becomes <c>POST &lt;base&gt;/adminapi/v2.0/&lt;tenant id&gt;/&lt;endpoint&gt;</c>
/// with a <c>{"CmdletInput":{"CmdletName","Parameters"}}</c> body, <c>X-AnchorMailbox: UPN:&lt;the administrator&gt;</c>
/// (the delegated flow's key for a call; task 010 proved it in the tenant) and the properties to keep as
/// <c>$select</c>; every page of an answer is read by posting the same body to its <c>@odata.nextLink</c>, as the
/// Graph client follows a collection (https://learn.microsoft.com/exchange/reference/admin-api-get-started). A 429 or
/// 503 is retried after its <c>Retry-After</c> within the same budget as the Graph client's
/// (<see cref="GraphClientOptions"/>; the snapshot's take sets its own with <see cref="WithBudget"/>). A failed answer becomes the contract's failure: 401 auth (3), 403 forbidden (8),
/// a missing object not found (4, a 404 or a 400 that says so), throttled (5), any other 400 a cmdlet failure (13),
/// a server error or no network a service error (9). The token goes to the Admin API's host only; piped input, which
/// the REST endpoints do not take, is refused as a bug.
/// </summary>
public sealed class AdminApiClient : IExoCmdletRunner
{
    /// <summary>The Admin API's base URL in the commercial cloud.</summary>
    public const string BaseUrl = "https://" + HttpAdminApiTransport.Host + "/adminapi/v2.0/";

    /// <summary>The hint of a refused permission or role.</summary>
    internal const string RoleHint = "the app registration needs the delegated permission Exchange.ManageV2 (Office 365 Exchange Online) with admin consent, and the account an Exchange Online role that reads the object (View-Only Organization Management or Recipient Management); after a new consent, `m365-aegis login` fetches a token that carries it";

    private readonly IGraphTransport _transport;
    private readonly Func<CancellationToken, Task<AdminApiTarget>> _target;
    private readonly GraphClientOptions _options;
    private readonly Shared _shared;

    /// <summary>A client over a transport (<see cref="AdminApiConnection"/> makes the product's).</summary>
    /// <param name="transport">The network (<see cref="HttpAdminApiTransport"/>), or the fixtures.</param>
    /// <param name="target">The tenant and the account; asked for once, before the first call.</param>
    /// <param name="requestLog">Where the request ids go; a new log when null.</param>
    /// <param name="options">The throttling budget; the defaults when null.</param>
    public AdminApiClient(IGraphTransport transport, Func<CancellationToken, Task<AdminApiTarget>> target, RequestLog? requestLog = null, GraphClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(target);
        _transport = transport;
        _target = target;
        RequestLog = requestLog ?? new RequestLog();
        _options = options ?? new GraphClientOptions();
        _shared = new Shared();
    }

    private AdminApiClient(AdminApiClient from, GraphClientOptions options)
    {
        _transport = from._transport;
        _target = from._target;
        RequestLog = from.RequestLog;
        _options = options;
        _shared = from._shared;
    }

    /// <summary>The request ids of every answer so far.</summary>
    public RequestLog RequestLog { get; }

    /// <summary>
    /// A client over the same transport, target and request log with another throttling budget, a fresh one, and the
    /// same clock and jitter, as <see cref="GraphClient.WithBudget"/>: the snapshot's take (task 117) is a long run of
    /// hundreds of calendar reads that may wait where a command would fail. The calls, the waits and the throttled
    /// answers are counted across both clients.
    /// </summary>
    internal AdminApiClient WithBudget(int maxRetries, TimeSpan maxTotalDelay) =>
        new(this, _options with { MaxRetries = maxRetries, MaxTotalDelay = maxTotalDelay });

    /// <summary>How many HTTP calls the client made (pages and retries included), this client's and its budgeted copies'.</summary>
    public int Calls
    {
        get
        {
            lock (_shared.Lock)
            {
                return _shared.Calls;
            }
        }
    }

    /// <summary>How long the client has waited on throttling so far, this client's and its budgeted copies' (the take's per-domain counts, task 117).</summary>
    public TimeSpan Waited
    {
        get
        {
            lock (_shared.Lock)
            {
                return _shared.Waited;
            }
        }
    }

    /// <summary>How many answers were a 429 or a 503 so far, retried or not, this client's and its budgeted copies' (task 117).</summary>
    public long ThrottledAnswers
    {
        get
        {
            lock (_shared.Lock)
            {
                return _shared.ThrottledAnswers;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<ExoResult> InvokeAsync(ExoCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        string endpoint = AdminApiCmdlets.Endpoint(command.Cmdlet);
        if (command.Input is not null)
        {
            throw new ArgumentException("the Admin API's endpoints take no piped input; name the object with -Identity", nameof(command));
        }

        AdminApiTarget target = await TargetAsync(cancellationToken).ConfigureAwait(false);
        string url = $"{BaseUrl}{Uri.EscapeDataString(target.TenantId)}/{endpoint}";
        if (command.Properties is { Count: > 0 } properties)
        {
            url = GraphQuery.Path(url, ("$select", string.Join(",", properties)));
        }

        string body = Body(command);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-AnchorMailbox"] = $"UPN:{target.Account}" };
        var items = new List<ExoObject>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var next = new Uri(url);
        while (true)
        {
            var request = new GraphHttpRequest(HttpMethod.Post, next, headers, body);
            GraphResponse response = await SendWithRetriesAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                throw Failure(command, response);
            }

            string? nextLink = Read(command, response.Body, items);
            if (nextLink is null || !seen.Add(nextLink))
            {
                break;
            }

            next = new Uri(nextLink);
            if (!string.Equals(next.Host, HttpAdminApiTransport.Host, StringComparison.OrdinalIgnoreCase) || next.Scheme != Uri.UriSchemeHttps)
            {
                throw new AegisException(FailureKind.ServiceError, $"the Admin API's next link for {command.Cmdlet} leaves https://{HttpAdminApiTransport.Host}", "report it with the request id");
            }
        }

        return new ExoResult(items, []);
    }

    /// <summary>The <c>CmdletInput</c> body of a cmdlet run: its name and its parameters (strings, numbers, booleans, lists).</summary>
    internal static string Body(ExoCommand command)
    {
        var input = new JsonObject { ["CmdletName"] = command.Cmdlet };
        if (command.Parameters.Count > 0)
        {
            var parameters = new JsonObject();
            foreach ((string name, object? value) in command.Parameters)
            {
                parameters[name] = value switch
                {
                    null => null,
                    string text => JsonValue.Create(text),
                    bool flag => JsonValue.Create(flag),
                    int number => JsonValue.Create(number),
                    long number => JsonValue.Create(number),
                    IEnumerable<string> list => new JsonArray(list.Select(item => (JsonNode?)JsonValue.Create(item)).ToArray()),
                    _ => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture)),
                };
            }

            input["Parameters"] = parameters;
        }

        return new JsonObject { ["CmdletInput"] = input }.ToJsonString();
    }

    /// <summary>Adds an answer's objects (a <c>value</c> list, or one object) to the list; its next link, or null.</summary>
    private static string? Read(ExoCommand command, string body, List<ExoObject> items)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                items.AddRange(root.EnumerateArray().Select(item => new ExoObject(item)));
                return null;
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty("value", out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.Array)
                {
                    items.AddRange(value.EnumerateArray().Select(item => new ExoObject(item)));
                }
                else if (value.ValueKind == JsonValueKind.Object)
                {
                    items.Add(new ExoObject(value));
                }
            }
            else if (root.EnumerateObject().Any(property => !property.Name.StartsWith('@')))
            {
                items.Add(new ExoObject(root));
            }

            return root.TryGetProperty("@odata.nextLink", out JsonElement link) && link.ValueKind == JsonValueKind.String ? link.GetString() : null;
        }
        catch (JsonException exception)
        {
            throw new AegisException(
                FailureKind.ServiceError,
                $"the Admin API's answer to {command.Cmdlet} is not the JSON the client expected: {exception.Message}",
                "run the command again; if it persists, report it with the request id",
                exception);
        }
    }

    private Task<AdminApiTarget> TargetAsync(CancellationToken cancellationToken)
    {
        lock (_shared.Lock)
        {
            if (_shared.PendingTarget is null || _shared.PendingTarget.IsFaulted || _shared.PendingTarget.IsCanceled)
            {
                _shared.PendingTarget = _target(cancellationToken);
            }

            return _shared.PendingTarget;
        }
    }

    private async Task<GraphResponse> SendWithRetriesAsync(GraphHttpRequest request, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            GraphResponse response = await _transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
            bool throttled = response.StatusCode is 429 or 503;
            lock (_shared.Lock)
            {
                _shared.Calls++;
                _shared.ThrottledAnswers += throttled ? 1 : 0;
            }

            if (throttled)
            {
                ThrottleWitness.Note();
            }

            RequestLog.Add(response.RequestId);
            if (!throttled || NextDelay(response.Headers, attempt) is not { } delay || !TryReserve(delay))
            {
                return response;
            }

            await _options.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private TimeSpan? NextDelay(IReadOnlyDictionary<string, string> headers, int attempt)
    {
        if (attempt >= _options.MaxRetries)
        {
            return null;
        }

        if (GraphClient.RetryAfter(headers, DateTimeOffset.UtcNow) is { } after)
        {
            return after + TimeSpan.FromMilliseconds(250 * _options.Jitter());
        }

        double seconds = Math.Min(30, Math.Pow(2, attempt));
        return TimeSpan.FromSeconds(seconds * (0.5 + (0.5 * _options.Jitter())));
    }

    private bool TryReserve(TimeSpan delay)
    {
        lock (_shared.Lock)
        {
            if (_shared.Waited + delay > _options.MaxTotalDelay)
            {
                return false;
            }

            _shared.Waited += delay;
            return true;
        }
    }

    /// <summary>The contract's failure for an answer that was not a 2xx.</summary>
    internal static AegisException Failure(ExoCommand command, GraphResponse response)
    {
        (string? code, string? message) = GraphClient.ReadError(response.Body);
        LogSink.Verbose?.Write("admin-api", $"{command.Cmdlet} answered {response.StatusCode.ToString(CultureInfo.InvariantCulture)}: {code}: {message}");
        string said = SidecarFailures.Shorten(message ?? code ?? $"HTTP {response.StatusCode.ToString(CultureInfo.InvariantCulture)}");
        string sentence = $"the Exchange Online Admin API answered {response.StatusCode.ToString(CultureInfo.InvariantCulture)} to {command.Cmdlet}: {said}";
        bool notFound = response.StatusCode == 404 || (response.StatusCode == 400 && SaysNotFound($"{code} {message}"));
        AegisException failure = response.StatusCode switch
        {
            401 => new AegisException(FailureKind.Auth, sentence, "the token was refused; run `m365-aegis whoami` (the Exchange Online token must carry Exchange.ManageV2), then `m365-aegis login`"),
            403 => new AegisException(FailureKind.Forbidden, sentence, RoleHint),
            429 or 503 => new AegisException(FailureKind.Throttled, sentence, "the retries ran out; run the command again in a minute"),
            _ when notFound => new AegisException(FailureKind.NotFound, said, SidecarFailures.NotFoundHint),
            400 => new AegisException(FailureKind.CmdletFailed, sentence, "run the command again; `--verbose` writes the service's answer to the log"),
            _ => new AegisException(FailureKind.ServiceError, sentence, "a service error on Microsoft's side; run the command again later"),
        };
        return new AegisException(failure.Failure) { HttpStatus = response.StatusCode };
    }

    private static bool SaysNotFound(string text) =>
        text.Contains("couldn't be found", StringComparison.OrdinalIgnoreCase)
        || text.Contains("could not be found", StringComparison.OrdinalIgnoreCase)
        || text.Contains("ObjectNotFound", StringComparison.OrdinalIgnoreCase)
        || text.Contains("NotFound", StringComparison.Ordinal);

    /// <summary>What a client and its budgeted copies share: the target once resolved, and the counts under one lock.</summary>
    private sealed class Shared
    {
        internal Lock Lock { get; } = new();

        internal Task<AdminApiTarget>? PendingTarget { get; set; }

        internal TimeSpan Waited { get; set; }

        internal int Calls { get; set; }

        internal long ThrottledAnswers { get; set; }
    }
}
