using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Audit;

/// <summary>What <c>m365-aegis signin list</c> was asked for.</summary>
/// <param name="User">The user principal name whose sign-ins to list, or null for every user.</param>
/// <param name="FailuresOnly">Only the failed sign-ins (an error code other than 0).</param>
/// <param name="Days">The last N days, or null (<see cref="AuditWindow"/>).</param>
/// <param name="Since">The explicit start of the window, or null.</param>
/// <param name="Limit">The most sign-ins to return; the newest come first.</param>
/// <param name="Until">The end of the window, or null for now (task 038).</param>
/// <param name="Kind">
/// The kind of sign-in to list from the beta endpoint (task 039), or null for what v1.0 lists: the interactive user
/// sign-ins.
/// </param>
public sealed record SignInQuery(
    string? User,
    bool FailuresOnly,
    int? Days,
    DateTimeOffset? Since,
    int Limit,
    DateTimeOffset? Until = null,
    SignInKind? Kind = null);

/// <summary>The kinds of sign-in the beta endpoint's <c>signInEventTypes</c> tells apart (task 039).</summary>
public enum SignInKind
{
    /// <summary>A user gave a factor (<c>interactiveUser</c>): what v1.0 lists.</summary>
    Interactive,

    /// <summary>A client signed in on a user's behalf with a token or a code (<c>nonInteractiveUser</c>).</summary>
    NonInteractive,

    /// <summary>An application signed in as itself, with a secret or a certificate (<c>servicePrincipal</c>).</summary>
    ServicePrincipal,

    /// <summary>An Azure resource signed in with its managed identity (<c>managedIdentity</c>).</summary>
    ManagedIdentity,
}

/// <summary>The names of the sign-in kinds: the command line's and the log's.</summary>
public static class SignInKinds
{
    /// <summary>The values <c>--kind</c> takes.</summary>
    public static IReadOnlyList<string> Names { get; } = ["interactive", "non-interactive", "service-principal", "managed-identity"];

    /// <summary>The kind a <c>--kind</c> value names; null for null.</summary>
    /// <exception cref="AegisException">A usage failure for a value that names no kind.</exception>
    public static SignInKind? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null => null,
        "interactive" => SignInKind.Interactive,
        "non-interactive" => SignInKind.NonInteractive,
        "service-principal" => SignInKind.ServicePrincipal,
        "managed-identity" => SignInKind.ManagedIdentity,
        _ => throw new AegisException(FailureKind.Usage, $"--kind '{value}' is not a kind of sign-in", $"give one of {string.Join(", ", Names)}"),
    };

    /// <summary>The kind as the log's <c>signInEventTypes</c> names it (<c>nonInteractiveUser</c>).</summary>
    public static string EventType(this SignInKind kind) => kind switch
    {
        SignInKind.Interactive => "interactiveUser",
        SignInKind.NonInteractive => "nonInteractiveUser",
        SignInKind.ServicePrincipal => "servicePrincipal",
        SignInKind.ManagedIdentity => "managedIdentity",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "a sign-in kind without an event type"),
    };

    /// <summary>True for the kinds an application signs in as itself, without a user.</summary>
    public static bool IsApplication(this SignInKind kind) => kind is SignInKind.ServicePrincipal or SignInKind.ManagedIdentity;
}

/// <summary>The answer of <c>signin list</c>: the window, the filters, and the sign-ins, newest first.</summary>
/// <param name="User">The user principal name the list is for (lower case, as the log keeps it), or null.</param>
/// <param name="FailuresOnly">Whether only the failed sign-ins are listed.</param>
/// <param name="Since">The start of the window, in UTC.</param>
/// <param name="SignIns">The sign-ins, newest first.</param>
/// <param name="Truncated">More sign-ins matched than the limit let through.</param>
/// <param name="Until">The end of the window, in UTC, or null for the moment of the request.</param>
/// <param name="Oldest">
/// When <paramref name="Truncated"/> is set, the time of the oldest sign-in listed: the answer covers the window from
/// there on, and <c>--until</c> with this time lists the sign-ins before it; null otherwise.
/// </param>
/// <param name="Kind">The kind asked for with <c>--kind</c>, or null for the v1.0 list.</param>
/// <param name="Endpoint">The Graph endpoint version that answered: <c>v1.0</c>, or <c>beta</c> for a <c>--kind</c>.</param>
public sealed record SignInReport(
    string? User,
    bool FailuresOnly,
    DateTimeOffset Since,
    IReadOnlyList<SignInEntry> SignIns,
    bool Truncated,
    DateTimeOffset? Until = null,
    DateTimeOffset? Oldest = null,
    SignInKind? Kind = null,
    string Endpoint = "v1.0");

/// <summary>One sign-in of the Microsoft Entra sign-in log, in the fields an administrator reads first.</summary>
/// <param name="Id">The sign-in's id.</param>
/// <param name="CreatedDateTime">When the sign-in started, in UTC.</param>
/// <param name="UserPrincipalName">The user's principal name.</param>
/// <param name="UserDisplayName">The user's display name.</param>
/// <param name="AppDisplayName">The application signed in to.</param>
/// <param name="ClientAppUsed">The client (<c>Browser</c>, <c>Mobile Apps and Desktop clients</c>, <c>IMAP</c>…).</param>
/// <param name="IpAddress">The client's IP address.</param>
/// <param name="Location">The city and the country or region, as far as known.</param>
/// <param name="IsInteractive">Whether the sign-in was interactive.</param>
/// <param name="ConditionalAccessStatus"><c>success</c>, <c>failure</c> or <c>notApplied</c>.</param>
/// <param name="ErrorCode">0 for a successful sign-in, else the Microsoft Entra error code.</param>
/// <param name="FailureReason">The reason of a failure.</param>
/// <param name="ServicePrincipalName">The application that signed in as itself (beta only), or null.</param>
/// <param name="ResourceDisplayName">The resource signed in to, or null.</param>
public sealed record SignInEntry(
    string Id,
    DateTimeOffset CreatedDateTime,
    string? UserPrincipalName,
    string? UserDisplayName,
    string? AppDisplayName,
    string? ClientAppUsed,
    string? IpAddress,
    string? Location,
    bool? IsInteractive,
    string? ConditionalAccessStatus,
    int ErrorCode,
    string? FailureReason,
    string? ServicePrincipalName = null,
    string? ResourceDisplayName = null)
{
    /// <summary>True when the sign-in succeeded (error code 0).</summary>
    public bool Succeeded => ErrorCode == 0;
}

/// <summary>
/// <c>signin list [--user &lt;upn&gt;] [--failures] [--kind &lt;kind&gt;] [--days N | --since &lt;date&gt;] [--until &lt;date&gt;]
/// [--limit N]</c> over <c>GET /auditLogs/signIns</c> (task 006), which lists the interactive user sign-ins in v1.0;
/// a <c>--kind</c> goes to the beta endpoint with <c>signInEventTypes/any(t: t eq '…')</c> in the <c>$filter</c>, the
/// only place the non-interactive, service principal and managed identity sign-ins are (task 039), and the report
/// names the endpoint that answered. The window and the user go into the <c>$filter</c>
/// (<c>createdDateTime ge …</c>, <c>userPrincipalName eq …</c>, both documented filters), the pages are the
/// client's, and the failures are picked here, because the log documents only <c>eq</c> on
/// <c>status/errorCode</c>. Reading the log needs <c>AuditLog.Read.All</c>, a role that reads it (Reports Reader,
/// Security Reader, Global Reader…), and a Microsoft Entra ID P1 or P2 license in the tenant: a 403 for the missing
/// license becomes its own sentence instead of the generic one.
/// </summary>
public static class SignInLookup
{
    /// <summary>The delegated permission the sign-in log needs.</summary>
    public const string Permission = "AuditLog.Read.All";

    /// <summary>Runs the lookup.</summary>
    /// <exception cref="AegisException">A usage error, or the failure of a request.</exception>
    public static async Task<CommandResult<SignInReport>> RunAsync(GraphClient graph, SignInQuery query, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(query);
        AuditWindow.CheckLimit(query.Limit);
        (DateTimeOffset since, DateTimeOffset? until) = AuditWindow.Window(query.Days, query.Since, query.Until, now);
        string? user = query.User?.Trim().ToLowerInvariant();
        if (user is { Length: 0 })
        {
            throw new AegisException(FailureKind.Usage, "--user is empty", "give a user principal name, such as alice@contoso.com");
        }

        if (user is not null && query.Kind is { } application && application.IsApplication())
        {
            throw new AegisException(
                FailureKind.Usage,
                $"--user names a user, and a {SignInKinds.Names[(int)application]} sign-in has none",
                "leave --user out, or ask for the interactive or non-interactive sign-ins");
        }

        GraphRequest request = Request(user, since, until, query.Kind);
        var entries = new List<SignInEntry>();
        bool truncated = false;
        try
        {
            await foreach (GraphSignIn item in graph.GetAllAsync<GraphSignIn>(request, cancellationToken).ConfigureAwait(false))
            {
                SignInEntry entry = Map(item);
                if (query.FailuresOnly && entry.Succeeded)
                {
                    continue;
                }

                if (entries.Count == query.Limit)
                {
                    truncated = true;
                    break;
                }

                entries.Add(entry);
            }
        }
        catch (AegisException exception) when (exception.Failure.Kind == FailureKind.Forbidden)
        {
            throw Forbidden(exception);
        }

        DateTimeOffset? oldest = truncated && entries.Count > 0 ? entries.Min(entry => entry.CreatedDateTime) : null;
        return CommandResult.Success(new SignInReport(
            user, query.FailuresOnly, since, entries, truncated, until, oldest, query.Kind, request.Version.Segment()));
    }

    /// <summary>
    /// The request of a window, a user and a kind: <c>/auditLogs/signIns?$filter=…</c>, on the beta endpoint for a
    /// kind.
    /// </summary>
    internal static GraphRequest Request(string? user, DateTimeOffset since, DateTimeOffset? until = null, SignInKind? kind = null)
    {
        string filter = $"createdDateTime ge {AuditWindow.Literal(since)}";
        if (until is { } end)
        {
            filter += $" and createdDateTime le {AuditWindow.Literal(end)}";
        }

        if (user is not null)
        {
            filter += $" and userPrincipalName eq {GraphQuery.Literal(user)}";
        }

        if (kind is { } eventType)
        {
            filter += $" and signInEventTypes/any(t: t eq {GraphQuery.Literal(eventType.EventType())})";
        }

        return GraphRequest.Get(GraphQuery.Path("/auditLogs/signIns", ("$filter", filter))) with
        {
            Permission = Permission,
            Version = kind is null ? GraphVersion.V1 : GraphVersion.Beta,
        };
    }

    /// <summary>
    /// The failure of a 403: a tenant without Microsoft Entra ID P1 or P2 gets a sentence of its own (Graph answers
    /// <c>Authentication_RequestFromNonPremiumTenantOrB2CTenant</c>, "… tenant doesn't have premium license"), any
    /// other 403 the permission and the roles the sign-in log needs.
    /// </summary>
    internal static AegisException Forbidden(AegisException exception)
    {
        string message = exception.Failure.Message;
        if (message.Contains("NonPremiumTenant", StringComparison.OrdinalIgnoreCase)
            || message.Contains("premium license", StringComparison.OrdinalIgnoreCase))
        {
            return new AegisException(
                FailureKind.Forbidden,
                "the sign-in log through Microsoft Graph needs a Microsoft Entra ID P1 or P2 license, and the tenant has none",
                "the sign-ins are still in the Microsoft Entra admin center (Entra ID > Monitoring & health > Sign-in logs); `m365-aegis audit list` reads the directory audit, which needs no license",
                exception);
        }

        return new AegisException(
            FailureKind.Forbidden,
            message,
            $"the app registration needs the delegated permission {Permission} with admin consent, and the account a role that reads the sign-in log (Reports Reader, Security Reader, Global Reader); after the change, run `m365-aegis login` for a fresh token",
            exception);
    }

    private static SignInEntry Map(GraphSignIn item)
    {
        string location = item.Location is { } place
            ? string.Join(", ", new[] { place.City, place.CountryOrRegion }.Where(part => !string.IsNullOrWhiteSpace(part)))
            : "";
        return new SignInEntry(
            item.Id ?? "",
            item.CreatedDateTime,
            item.UserPrincipalName,
            item.UserDisplayName,
            item.AppDisplayName,
            item.ClientAppUsed,
            item.IpAddress,
            location.Length == 0 ? null : location,
            item.IsInteractive,
            item.ConditionalAccessStatus,
            item.Status?.ErrorCode ?? 0,
            string.IsNullOrWhiteSpace(item.Status?.FailureReason) ? null : item.Status.FailureReason,
            string.IsNullOrWhiteSpace(item.ServicePrincipalName) ? null : item.ServicePrincipalName,
            string.IsNullOrWhiteSpace(item.ResourceDisplayName) ? null : item.ResourceDisplayName);
    }
}

/// <summary>A sign-in as Graph sends it (the <c>signIn</c> resource), in the fields the lookup reads.</summary>
internal sealed record GraphSignIn : GraphObject
{
    public string? Id { get; init; }

    public DateTimeOffset CreatedDateTime { get; init; }

    public string? UserPrincipalName { get; init; }

    public string? UserDisplayName { get; init; }

    public string? AppDisplayName { get; init; }

    public string? ClientAppUsed { get; init; }

    public string? IpAddress { get; init; }

    public bool? IsInteractive { get; init; }

    public string? ConditionalAccessStatus { get; init; }

    public GraphSignInStatus? Status { get; init; }

    public string? ServicePrincipalName { get; init; }

    public string? ResourceDisplayName { get; init; }

    public GraphSignInLocation? Location { get; init; }
}

/// <summary>The <c>signInStatus</c> of a sign-in.</summary>
internal sealed record GraphSignInStatus(int ErrorCode, string? FailureReason);

/// <summary>The <c>signInLocation</c> of a sign-in, in the fields the lookup reads.</summary>
internal sealed record GraphSignInLocation(string? City, string? CountryOrRegion);
