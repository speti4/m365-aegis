using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Audit;

/// <summary>What <c>m365-aegis signin list</c> was asked for.</summary>
/// <param name="User">The user principal name whose sign-ins to list, or null for every user.</param>
/// <param name="FailuresOnly">Only the failed sign-ins (an error code other than 0).</param>
/// <param name="Days">The last N days, or null (<see cref="AuditWindow"/>).</param>
/// <param name="Since">The explicit start of the window, or null.</param>
/// <param name="Limit">The most sign-ins to return; the newest come first.</param>
public sealed record SignInQuery(string? User, bool FailuresOnly, int? Days, DateTimeOffset? Since, int Limit);

/// <summary>The answer of <c>signin list</c>: the window, the filters, and the sign-ins, newest first.</summary>
/// <param name="User">The user principal name the list is for (lower case, as the log keeps it), or null.</param>
/// <param name="FailuresOnly">Whether only the failed sign-ins are listed.</param>
/// <param name="Since">The start of the window, in UTC.</param>
/// <param name="SignIns">The sign-ins, newest first.</param>
/// <param name="Truncated">More sign-ins matched than the limit let through.</param>
public sealed record SignInReport(string? User, bool FailuresOnly, DateTimeOffset Since, IReadOnlyList<SignInEntry> SignIns, bool Truncated);

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
    string? FailureReason)
{
    /// <summary>True when the sign-in succeeded (error code 0).</summary>
    public bool Succeeded => ErrorCode == 0;
}

/// <summary>
/// <c>signin list [--user &lt;upn&gt;] [--failures] [--days N | --since &lt;date&gt;] [--limit N]</c> over
/// <c>GET /auditLogs/signIns</c> (task 006): the window and the user go into the <c>$filter</c>
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
        DateTimeOffset since = AuditWindow.Start(query.Days, query.Since, now);
        string? user = query.User?.Trim().ToLowerInvariant();
        if (user is { Length: 0 })
        {
            throw new AegisException(FailureKind.Usage, "--user is empty", "give a user principal name, such as alice@contoso.com");
        }

        GraphRequest request = Request(user, since);
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

        return CommandResult.Success(new SignInReport(user, query.FailuresOnly, since, entries, truncated));
    }

    /// <summary>The request of a window and a user: <c>/auditLogs/signIns?$filter=…</c>.</summary>
    internal static GraphRequest Request(string? user, DateTimeOffset since)
    {
        string filter = $"createdDateTime ge {AuditWindow.Literal(since)}";
        if (user is not null)
        {
            filter += $" and userPrincipalName eq {GraphQuery.Literal(user)}";
        }

        return GraphRequest.Get(GraphQuery.Path("/auditLogs/signIns", ("$filter", filter))) with { Permission = Permission };
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
            string.IsNullOrWhiteSpace(item.Status?.FailureReason) ? null : item.Status.FailureReason);
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

    public GraphSignInLocation? Location { get; init; }
}

/// <summary>The <c>signInStatus</c> of a sign-in.</summary>
internal sealed record GraphSignInStatus(int ErrorCode, string? FailureReason);

/// <summary>The <c>signInLocation</c> of a sign-in, in the fields the lookup reads.</summary>
internal sealed record GraphSignInLocation(string? City, string? CountryOrRegion);
