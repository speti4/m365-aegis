using System.Text.Json.Serialization;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Config;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Exchange.Sidecar;

/// <summary>
/// Makes the Exchange Online session a command uses (task 015): pwsh from PATH, the script next to the exe (checked
/// against the copy compiled into the core, as the doctor does), the profile's token for
/// <c>https://outlook.office365.com</c> from <see cref="AuthService.GetTokenAsync"/> (silent only: no sign-in is
/// exit code 3), and the organization's <c>.onmicrosoft.com</c> name: the profile's tenant when it is one, else the
/// initial domain Graph names (<c>GET /organization</c>). With <see cref="TestOrganizationVariable"/> set the
/// session asks for no token and connects to that organization with a placeholder, which only the fake module of the
/// tests accepts; like <c>M365AEGIS_GRAPH_FIXTURES</c> it is how the command line's own tests run offline.
/// </summary>
public static class ExchangeConnection
{
    /// <summary>The organization the offline tests connect to, without a sign-in (with the fake module only).</summary>
    public const string TestOrganizationVariable = "M365AEGIS_EXO_TEST_ORGANIZATION";

    /// <summary>
    /// Which parameters the connect passes besides the token: <c>organization</c> (the default), <c>upn</c> or
    /// <c>both</c> (task 009 measures which the service needs).
    /// </summary>
    public const string ConnectWithVariable = "M365AEGIS_EXO_CONNECT";

    /// <summary>The placeholder token of the offline tests; the fake module accepts it, the service never would.</summary>
    internal const string TestToken = "offline-test-token";

    /// <summary>The session of a command: the real sign-in, or the offline tests' organization when the variable is set.</summary>
    /// <param name="profileName">The tenant profile (<c>--profile</c>), or null for the default one.</param>
    /// <param name="requestLog">Where the request ids of the Graph lookup of the organization go.</param>
    /// <param name="connectWith">The connect's parameters; null reads <see cref="ConnectWithVariable"/>.</param>
    public static ExchangeSession Create(string? profileName, RequestLog requestLog, ExchangeConnectWith? connectWith = null) =>
        new(Options(profileName, requestLog, connectWith));

    /// <summary>
    /// What a session of a command is made of (the start, the token, the target), for a caller that drives the
    /// sidecar itself: the connect measurement of task 011 starts a fresh one per run with the same token.
    /// </summary>
    public static ExchangeSessionOptions Options(string? profileName, RequestLog requestLog, ExchangeConnectWith? connectWith = null) =>
        Options(profileName, requestLog, AuthService.Create, connectWith);

    /// <summary>
    /// The session of a command whose other layers share its sign-in (task 042: the Admin API and Graph beside the
    /// sidecar): the offline tests' organization when the variable is set, else the sign-in <paramref name="auth"/> makes.
    /// </summary>
    public static ExchangeSession Create(string? profileName, RequestLog requestLog, Func<AuthService> auth) =>
        new(Options(profileName, requestLog, auth));

    /// <summary>What a session is made of, over the sign-in <paramref name="auth"/> makes (not called in the offline tests).</summary>
    public static ExchangeSessionOptions Options(string? profileName, RequestLog requestLog, Func<AuthService> auth, ExchangeConnectWith? connectWith = null)
    {
        ArgumentNullException.ThrowIfNull(auth);
        ExchangeConnectWith with = connectWith ?? ParseConnectWith(Environment.GetEnvironmentVariable(ConnectWithVariable));
        string? testOrganization = Environment.GetEnvironmentVariable(TestOrganizationVariable);
        if (!string.IsNullOrWhiteSpace(testOrganization))
        {
            return new ExchangeSessionOptions
            {
                Start = StartAsync,
                Token = _ => Task.FromResult(new ExchangeToken(TestToken, DateTimeOffset.UtcNow.AddHours(1), ["Exchange.Manage"], "alice@contoso.com", null)),
                Target = _ => Task.FromResult(Target(with, testOrganization.Trim(), "alice@contoso.com")),
            };
        }

        return Options(auth(), profileName, requestLog, with, StartAsync);
    }

    /// <summary>The session's parts over the given sign-in and sidecar start.</summary>
    public static ExchangeSessionOptions Options(
        AuthService auth,
        string? profileName,
        RequestLog requestLog,
        ExchangeConnectWith connectWith,
        Func<CancellationToken, Task<SidecarClient>> start)
    {
        ArgumentNullException.ThrowIfNull(auth);
        return new ExchangeSessionOptions
        {
            Start = start,
            Token = async cancellationToken => ToExchangeToken(await auth.GetTokenAsync(Resource.Exchange, profileName, cancellationToken).ConfigureAwait(false)),
            Target = async cancellationToken =>
            {
                TenantProfile profile = auth.Profiles.Resolve(profileName);
                string? organization = connectWith == ExchangeConnectWith.UserPrincipalName
                    ? null
                    : await OrganizationAsync(profile, () => GraphConnection.Create(auth, profileName, requestLog), cancellationToken).ConfigureAwait(false);
                return Target(connectWith, organization, profile.Account);
            },
        };
    }

    /// <summary>The broker's token as the session keeps it.</summary>
    public static ExchangeToken ToExchangeToken(TokenResult token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return new ExchangeToken(token.AccessToken, token.ExpiresOn, Scopes.Permissions(token.Scopes), token.Account.Username, token.TenantId ?? token.Account.TenantId);
    }

    /// <summary>The value of <see cref="ConnectWithVariable"/>; anything unknown is the default.</summary>
    public static ExchangeConnectWith ParseConnectWith(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "UPN" or "USERPRINCIPALNAME" => ExchangeConnectWith.UserPrincipalName,
        "BOTH" => ExchangeConnectWith.Both,
        _ => ExchangeConnectWith.Organization,
    };

    /// <summary>
    /// The organization's <c>.onmicrosoft.com</c> name for <c>-Organization</c>: the profile's tenant when it is one,
    /// else the initial domain of <c>GET /organization</c> (readable with <c>User.Read</c>).
    /// </summary>
    internal static async Task<string> OrganizationAsync(TenantProfile profile, Func<GraphClient> graph, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(graph);
        if (profile.Tenant.EndsWith(".onmicrosoft.com", StringComparison.OrdinalIgnoreCase))
        {
            return profile.Tenant;
        }

        string path = GraphQuery.Path("/organization", ("$select", "verifiedDomains"));
        IReadOnlyList<GraphOrganization> organizations = await graph().ListAsync<GraphOrganization>(GraphRequest.Get(path) with { Permission = "User.Read" }, cancellationToken).ConfigureAwait(false);
        string? initial = organizations
            .SelectMany(organization => organization.VerifiedDomains ?? [])
            .FirstOrDefault(domain => domain.IsInitial == true)?.Name;
        return initial ?? throw new AegisException(
            FailureKind.SidecarUnavailable,
            "Graph named no initial .onmicrosoft.com domain for the tenant, which the Exchange Online connect needs",
            "log in with the tenant's .onmicrosoft.com name as --tenant, or set M365AEGIS_EXO_CONNECT=upn");
    }

    /// <summary>Starts the sidecar of this install: pwsh from PATH, the script next to the exe once its hash matches the build's.</summary>
    internal static Task<SidecarClient> StartAsync(CancellationToken cancellationToken)
    {
        string pwsh = Doctor.FindPwsh() ?? throw new AegisException(
            FailureKind.SidecarUnavailable,
            "pwsh was not found on PATH; the Exchange Online commands need PowerShell 7.6 or later",
            "winget install Microsoft.PowerShell");
        string script = SidecarScript.InstalledPath;
        byte[]? installed = File.Exists(script) ? File.ReadAllBytes(script) : null;
        DoctorCheck check = Doctor.CheckSidecarScript(installed, SidecarScript.ReadBuildCopy());
        if (check.Status == CheckStatus.Fail)
        {
            // The script runs with the administrator's Exchange Online token: a copy that is not the build's never starts.
            throw new AegisException(FailureKind.SidecarUnavailable, $"the sidecar script cannot be used: {check.Detail}", check.Hint);
        }

        return SidecarClient.StartAsync(new SidecarStartInfo(pwsh, script), cancellationToken);
    }

    private static ExchangeTarget Target(ExchangeConnectWith with, string? organization, string account) => with switch
    {
        ExchangeConnectWith.UserPrincipalName => new ExchangeTarget(null, account),
        ExchangeConnectWith.Both => new ExchangeTarget(organization, account),
        _ => new ExchangeTarget(organization, null),
    };

    /// <summary>The part of an organization the connect reads.</summary>
    internal sealed record GraphOrganization : GraphObject
    {
        /// <summary>The tenant's verified domains.</summary>
        [JsonPropertyName("verifiedDomains")]
        public IReadOnlyList<GraphVerifiedDomain>? VerifiedDomains { get; init; }
    }

    /// <summary>A verified domain: its name, and whether it is the tenant's initial <c>.onmicrosoft.com</c> one.</summary>
    internal sealed record GraphVerifiedDomain(string? Name, bool? IsInitial);
}
