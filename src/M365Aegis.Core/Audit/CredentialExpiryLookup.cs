using System.Globalization;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Audit;

/// <summary>What <c>m365-aegis secrets expiring</c> was asked for.</summary>
/// <param name="Days">The credentials that expire within this many days are listed.</param>
/// <param name="IncludeExpired">Also list the credentials that have already expired and are still on the object.</param>
public sealed record CredentialExpiryQuery(int Days, bool IncludeExpired);

/// <summary>Which directory object a credential belongs to.</summary>
public enum CredentialOwner
{
    /// <summary>An application registration (<c>application</c>).</summary>
    Application,

    /// <summary>A service principal, an enterprise application (<c>servicePrincipal</c>), such as a SAML signing certificate.</summary>
    ServicePrincipal,
}

/// <summary>The kind of a credential.</summary>
public enum CredentialKind
{
    /// <summary>A client secret (<c>passwordCredentials</c>).</summary>
    Secret,

    /// <summary>A certificate (<c>keyCredentials</c>).</summary>
    Certificate,
}

/// <summary>The answer of <c>secrets expiring</c>: what was checked, and the credentials that expire soonest first.</summary>
/// <param name="Days">The window in days.</param>
/// <param name="AsOf">The moment the expiry was computed against, in UTC.</param>
/// <param name="IncludeExpired">Whether the expired credentials are listed too.</param>
/// <param name="Applications">The application registrations checked.</param>
/// <param name="ServicePrincipals">The service principals checked.</param>
/// <param name="Credentials">The credentials in the window, the soonest to expire first.</param>
public sealed record CredentialExpiryReport(
    int Days,
    DateTimeOffset AsOf,
    bool IncludeExpired,
    int Applications,
    int ServicePrincipals,
    IReadOnlyList<ExpiringCredential> Credentials);

/// <summary>One secret or certificate and the object that holds it.</summary>
/// <param name="Owner">An application registration or a service principal.</param>
/// <param name="DisplayName">The application's name.</param>
/// <param name="AppId">The application (client) id.</param>
/// <param name="ObjectId">The object id of the application or the service principal.</param>
/// <param name="Kind">A secret or a certificate.</param>
/// <param name="Name">The credential's own display name (its description in the portal), when it has one.</param>
/// <param name="KeyId">The credential's key id.</param>
/// <param name="EndDateTime">When the credential expires, in UTC.</param>
/// <param name="DaysLeft">Whole days until the expiry; negative once it has expired.</param>
/// <param name="Expired">Whether the credential has already expired.</param>
public sealed record ExpiringCredential(
    CredentialOwner Owner,
    string DisplayName,
    string? AppId,
    string ObjectId,
    CredentialKind Kind,
    string? Name,
    string? KeyId,
    DateTimeOffset EndDateTime,
    int DaysLeft,
    bool Expired);

/// <summary>
/// <c>secrets expiring &lt;days&gt; [--include-expired]</c> (task 006): every application registration and every
/// service principal with <c>$select=id,appId,displayName,passwordCredentials,keyCredentials</c>, 999 to a page
/// (the most the directory allows), and the expiry computed here, against one moment, because the directory cannot
/// filter on a credential's end date. Needs <c>Application.Read.All</c>.
/// </summary>
public static class CredentialExpiryLookup
{
    /// <summary>The delegated permission the lookup needs.</summary>
    public const string Permission = "Application.Read.All";

    /// <summary>The longest window, ten years: enough for every certificate a tenant holds.</summary>
    public const int MaxDays = 3650;

    private const string Select = "id,appId,displayName,passwordCredentials,keyCredentials";

    /// <summary>Runs the lookup.</summary>
    /// <exception cref="AegisException">A usage error, or the failure of a request.</exception>
    public static async Task<CommandResult<CredentialExpiryReport>> RunAsync(GraphClient graph, CredentialExpiryQuery query, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Days is < 0 or > MaxDays)
        {
            throw new AegisException(
                FailureKind.Usage,
                $"{query.Days.ToString(CultureInfo.InvariantCulture)} days is outside 0 to {MaxDays.ToString(CultureInfo.InvariantCulture)}",
                "give the window in days, such as `m365-aegis secrets expiring 30`");
        }

        DateTimeOffset asOf = now.ToUniversalTime();
        DateTimeOffset until = asOf.AddDays(query.Days);
        var credentials = new List<ExpiringCredential>();
        int applications = 0;
        await foreach (GraphCredentialHolder holder in graph.GetAllAsync<GraphCredentialHolder>(Request("/applications"), cancellationToken).ConfigureAwait(false))
        {
            applications++;
            credentials.AddRange(Expiring(holder, CredentialOwner.Application, asOf, until, query.IncludeExpired));
        }

        int servicePrincipals = 0;
        await foreach (GraphCredentialHolder holder in graph.GetAllAsync<GraphCredentialHolder>(Request("/servicePrincipals"), cancellationToken).ConfigureAwait(false))
        {
            servicePrincipals++;
            credentials.AddRange(Expiring(holder, CredentialOwner.ServicePrincipal, asOf, until, query.IncludeExpired));
        }

        List<ExpiringCredential> ordered = credentials
            .OrderBy(credential => credential.EndDateTime)
            .ThenBy(credential => credential.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return CommandResult.Success(new CredentialExpiryReport(query.Days, asOf, query.IncludeExpired, applications, servicePrincipals, ordered));
    }

    /// <summary>The request of a collection: <c>/applications?$select=…&amp;$top=999</c>.</summary>
    internal static GraphRequest Request(string collection) =>
        GraphRequest.Get(GraphQuery.Path(collection, ("$select", Select), ("$top", "999"))) with { Permission = Permission };

    private static IEnumerable<ExpiringCredential> Expiring(GraphCredentialHolder holder, CredentialOwner owner, DateTimeOffset asOf, DateTimeOffset until, bool includeExpired)
    {
        IEnumerable<(CredentialKind Kind, GraphCredential Credential)> all =
            (holder.PasswordCredentials ?? []).Select(credential => (CredentialKind.Secret, credential))
            .Concat((holder.KeyCredentials ?? []).Select(credential => (CredentialKind.Certificate, credential)));
        foreach ((CredentialKind kind, GraphCredential credential) in all)
        {
            if (credential.EndDateTime is not { } end || end > until)
            {
                continue;
            }

            bool expired = end <= asOf;
            if (expired && !includeExpired)
            {
                continue;
            }

            yield return new ExpiringCredential(
                owner,
                holder.DisplayName ?? holder.AppId ?? holder.Id ?? "",
                holder.AppId,
                holder.Id ?? "",
                kind,
                string.IsNullOrWhiteSpace(credential.DisplayName) ? null : credential.DisplayName,
                credential.KeyId,
                end.ToUniversalTime(),
                (int)Math.Floor((end - asOf).TotalDays),
                expired);
        }
    }
}

/// <summary>An application or a service principal as the lookup selects it.</summary>
internal sealed record GraphCredentialHolder : GraphObject
{
    public string? Id { get; init; }

    public string? AppId { get; init; }

    public string? DisplayName { get; init; }

    public IReadOnlyList<GraphCredential>? PasswordCredentials { get; init; }

    public IReadOnlyList<GraphCredential>? KeyCredentials { get; init; }
}

/// <summary>
/// The fields a <c>passwordCredential</c> and a <c>keyCredential</c> share and the lookup reads; the certificate's
/// <c>key</c> and a secret's <c>hint</c> are left unread.
/// </summary>
internal sealed record GraphCredential(string? KeyId, string? DisplayName, DateTimeOffset? EndDateTime);
