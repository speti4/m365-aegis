using System.Text.Json;
using System.Text.RegularExpressions;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Exchange.Sidecar;

/// <summary>
/// The sidecar's error replies as the failures of the contract (D-006, task 015). A code the protocol names maps
/// one to one; a <c>connect-failed</c> or a <c>cmdlet-failed</c> is read for what the service said, because the
/// module reports a sign-in, a permission, a missing object and throttling all as a PowerShell error:
/// <list type="bullet">
/// <item><c>module-missing</c>, and a <c>connect-failed</c> the service did not name: exit code 11, sidecar unavailable;</item>
/// <item>a <c>connect-failed</c> or <c>cmdlet-failed</c> that names the token or a 401: 3, auth;</item>
/// <item>one that names a 403, an access denial or a missing role: 8, forbidden; a <c>cmdlet-failed</c> whose cmdlet the
/// session does not know ("is not recognized", a <c>CommandNotFoundException</c>) too, since Exchange Online imports only
/// the cmdlets the account's roles allow (task 108);</item>
/// <item>a <c>cmdlet-failed</c> whose object was not found: 4, not found;</item>
/// <item>one that names throttling (a 429, Server Busy, a micro delay, a budget): 5, throttled;</item>
/// <item><c>cmdlet-not-allowed</c>: 12; any other <c>cmdlet-failed</c>: 13;</item>
/// <item><c>bad-request</c>, <c>unknown-type</c>, <c>not-connected</c>: the core sent what it should not have, a bug (70).</item>
/// </list>
/// </summary>
public static partial class SidecarFailures
{
    private const string DoctorHint = "run `m365-aegis doctor`: it checks pwsh, the module and the sidecar script";
    private const string LoginHint = "run `m365-aegis whoami`: the Exchange Online token must be there; `m365-aegis login` renews the sign-in";
    private const string RoleHint = "the account needs an Exchange Online role that runs the cmdlet (View-Only Organization Management or Recipient Management for the mailbox lookups), and the app registration Exchange.Manage with admin consent";
    private const string HiddenHint = "Exchange Online imports only the cmdlets the account's roles allow, so the session has no such cmdlet; " + RoleHint;

    private static readonly string[] AuthWords = ["401", "unauthorized", "token has expired", "token is expired", "expired token", "invalid token", "aadsts", "access token"];
    private static readonly string[] ForbiddenWords = ["403", "forbidden", "access denied", "accessdenied", "is not allowed to", "isn't allowed to", "doesn't have permission", "does not have permission", "insufficient", "write scope"];
    private static readonly string[] HiddenWords = ["is not recognized", "commandnotfoundexception"];
    private static readonly string[] NotFoundWords = ["couldn't be found", "could not be found", "managementobjectnotfound", "objectnotfound", "httpstatuscode=404", "not found"];
    private static readonly string[] ThrottledWords = ["throttl", "429", "server busy", "serverbusy", "microdelay", "budget", "too many requests", "toomanyrequests"];

    /// <summary>The failure for an error reply to a message of the given type.</summary>
    /// <param name="type">The request's type: <c>connect</c>, <c>reconnect</c>, <c>invoke</c>, …</param>
    /// <param name="error">The reply.</param>
    /// <param name="stderr">The last lines of the child's stderr, for a bug report.</param>
    public static Exception For(string type, SidecarError error, string stderr)
    {
        ArgumentNullException.ThrowIfNull(error);
        string text = $"{error.Message} {error.ErrorId} {error.Category}";
        bool connecting = type is "connect" or "reconnect";
        switch (error.Code)
        {
            case "module-missing":
                return new AegisException(FailureKind.SidecarUnavailable, "the ExchangeOnlineManagement module is not installed", "Install-Module ExchangeOnlineManagement -Scope CurrentUser");
            case "cmdlet-not-allowed":
                return new AegisException(FailureKind.CmdletNotAllowed, error.Message, $"the sidecar runs {string.Join(", ", ExoCmdlets.Allowed)} only (D-004)");
            case "connect-failed" or "cmdlet-failed":
                break;
            default:
                return new InvalidOperationException(
                    $"the sidecar refused '{type}' ({error.Code}: {error.Message}){(stderr.Length == 0 ? "" : $"; stderr: {stderr}")}");
        }

        // The service's text can be a long REST error with a JSON body and a server name: the answer gets its one
        // sentence, the whole text goes to the --verbose log (scrubbed there like every entry).
        LogSink.Verbose?.Write("exchange-online", $"{type} {error.Code}: {error.Message} (error id: {error.ErrorId}; category: {error.Category})");
        string said = Shorten(error.Message);
        string what = connecting ? "the Exchange Online connect failed" : "the Exchange Online cmdlet failed";
        if (Names(text, AuthWords))
        {
            return new AegisException(FailureKind.Auth, $"{what}: {said}", LoginHint);
        }

        if (Names(text, ForbiddenWords))
        {
            return new AegisException(FailureKind.Forbidden, $"{what}: {said}", RoleHint);
        }

        // A cmdlet the session does not know (task 108): the module imported the allow-list by name, and the service left
        // out what the account's roles do not run, so the shape is a missing role, not a broken cmdlet.
        if (!connecting && Names(text, HiddenWords))
        {
            return new AegisException(FailureKind.Forbidden, $"{what}: {said}", HiddenHint);
        }

        if (connecting)
        {
            return new AegisException(FailureKind.SidecarUnavailable, $"{what}: {said}", DoctorHint);
        }

        if (Names(text, ThrottledWords))
        {
            return new AegisException(FailureKind.Throttled, $"Exchange Online throttled the request: {said}", "wait a few minutes and run the command again");
        }

        if (Names(text, NotFoundWords))
        {
            return new AegisException(FailureKind.NotFound, said, NotFoundHint);
        }

        return new AegisException(FailureKind.CmdletFailed, $"{what}: {said}", "run the command again; `--verbose` writes the service's whole answer to the log");
    }

    /// <summary>The hint of a failure whose object was not found.</summary>
    public const string NotFoundHint = "check the name or the address; `m365-aegis mailbox list` lists the mailboxes";

    /// <summary>
    /// The service's message in one sentence: the <c>message</c> of a REST error's JSON body
    /// (<c>Error while querying REST service. HttpStatusCode=404 ErrorMessage={"error":{"message":…}}</c>) without the
    /// <c>Error executing request.</c> preamble and the server a missing object was looked for on, cut at 300
    /// characters.
    /// </summary>
    public static string Shorten(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        string text = message;
        int body = text.IndexOf("ErrorMessage=", StringComparison.OrdinalIgnoreCase);
        if (body >= 0 && InnerMessage(text[(body + "ErrorMessage=".Length)..]) is { } inner)
        {
            text = inner;
        }

        text = Preamble().Replace(text, "");
        text = ServerName().Replace(text, "$1");
        text = Whitespace().Replace(text, " ").Trim();
        return text.Length <= 300 ? text : text[..299] + "…";
    }

    /// <summary>The <c>error.message</c> of a JSON body, or the first <c>"message"</c> value when it does not parse.</summary>
    private static string? InnerMessage(string json)
    {
        int open = json.IndexOf('{', StringComparison.Ordinal);
        int close = json.LastIndexOf('}');
        if (open < 0 || close <= open)
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json[open..(close + 1)]);
            if (document.RootElement.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        catch (JsonException)
        {
            // Cut or invalid: the first message value below.
        }

        Match match = MessageValue().Match(json);
        return match.Success ? Regex.Unescape(match.Groups[1].Value) : null;
    }

    [GeneratedRegex(@"^\s*Error executing request\.\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Preamble();

    [GeneratedRegex(@"(couldn't be found|could not be found) on '[^']*'", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ServerName();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"""message""\s*:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.CultureInvariant)]
    private static partial Regex MessageValue();

    private static bool Names(string text, string[] words) =>
        words.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
}
