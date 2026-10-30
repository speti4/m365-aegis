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
    public static ExchangeSessionOptions Options(string? profileName, RequestLog requestLog, Func<AuthService> auth, ExchangeConnectWith? connectWith = null) =>
        Options(profileName, requestLog, auth, Tokens(profileName, auth), connectWith);

    /// <summary>
    /// The Exchange Online token source of a command (task 049), which its sidecars and its Admin API share: the
    /// offline tests' placeholder when <see cref="TestOrganizationVariable"/> is set, else the profile's sign-in
    /// <paramref name="auth"/> makes, asked for on the first token.
    /// </summary>
    public static ExchangeTokenSource Tokens(string? profileName, Func<AuthService> auth)
    {
        ArgumentNullException.ThrowIfNull(auth);
        return string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TestOrganizationVariable))
            ? new ExchangeTokenSource(async cancellationToken => ToExchangeToken(await auth().GetTokenAsync(Resource.Exchange, profileName, cancellationToken).ConfigureAwait(false)))
            : new ExchangeTokenSource(_ => Task.FromResult(new ExchangeToken(TestToken, DateTimeOffset.UtcNow.AddHours(1), ["Exchange.Manage"], "alice@contoso.com", null)));
    }

    /// <summary>
    /// What the sessions of a command are made of when its layers share one token (task 049): every session made
    /// from these options (the command's sidecar and the extra ones of a split read) connects with the token of
    /// <paramref name="tokens"/>, renews it on the same threshold and clock, and asks for the organization once.
    /// </summary>
    public static ExchangeSessionOptions Options(string? profileName, RequestLog requestLog, Func<AuthService> auth, ExchangeTokenSource tokens, ExchangeConnectWith? connectWith = null)
    {
        ArgumentNullException.ThrowIfNull(auth);
        ArgumentNullException.ThrowIfNull(tokens);
        ExchangeConnectWith with = connectWith ?? ParseConnectWith(Environment.GetEnvironmentVariable(ConnectWithVariable));
        string? testOrganization = Environment.GetEnvironmentVariable(TestOrganizationVariable);
        if (!string.IsNullOrWhiteSpace(testOrganization))
        {
            return Over(tokens, StartAsync, _ => Task.FromResult(Target(with, testOrganization.Trim(), "alice@contoso.com")));
        }

        return Over(tokens, StartAsync, cancellationToken => TargetAsync(auth(), profileName, requestLog, with, cancellationToken));
    }

    /// <summary>The session's parts over the given sign-in and sidecar start, with a token source of its own.</summary>
    public static ExchangeSessionOptions Options(
        AuthService auth,
        string? profileName,
        RequestLog requestLog,
        ExchangeConnectWith connectWith,
        Func<CancellationToken, Task<SidecarClient>> start)
    {
        ArgumentNullException.ThrowIfNull(auth);
        return Over(
            ExchangeTokenSource.ForProfile(auth, profileName),
            start,
            cancellationToken => TargetAsync(auth, profileName, requestLog, connectWith, cancellationToken));
    }

    /// <summary>Session options over a token source: its token, its renewal threshold and clock, and the target asked for once.</summary>
    private static ExchangeSessionOptions Over(ExchangeTokenSource tokens, Func<CancellationToken, Task<SidecarClient>> start, Func<CancellationToken, Task<ExchangeTarget>> target)
    {
        var gate = new Lock();
        Task<ExchangeTarget>? known = null;
        return new ExchangeSessionOptions
        {
            Start = start,
            Token = tokens.GetAsync,
            RenewToken = tokens.RenewAsync,
            Clock = tokens.Clock,
            RenewBefore = tokens.RenewBefore,
            Target = cancellationToken =>
            {
                lock (gate)
                {
                    if (known is null || known.IsFaulted || known.IsCanceled)
                    {
                        known = target(cancellationToken);
                    }

                    return known;
                }
            },
        };
    }

    private static async Task<ExchangeTarget> TargetAsync(AuthService auth, string? profileName, RequestLog requestLog, ExchangeConnectWith connectWith, CancellationToken cancellationToken)
    {
        TenantProfile profile = auth.Profiles.Resolve(profileName);
        string? organization = connectWith == ExchangeConnectWith.UserPrincipalName
            ? null
            : await OrganizationAsync(profile, () => GraphConnection.Create(auth, profileName, requestLog), cancellationToken).ConfigureAwait(false);
        return Target(connectWith, organization, profile.Account);
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
    internal static Task<SidecarClient> StartAsync(CancellationToken cancellationToken) =>
        StartAsync(SidecarScript.InstalledPath, cancellationToken);

    /// <summary>Starts the sidecar from the script at <paramref name="script"/> once its hash matches the build's (the tests name another copy).</summary>
    internal static Task<SidecarClient> StartAsync(string script, CancellationToken cancellationToken)
    {
        string pwsh = Doctor.FindPwsh() ?? throw new AegisException(
            FailureKind.SidecarUnavailable,
            "pwsh was not found on PATH; the Exchange Online commands need PowerShell 7.6 or later",
            "winget install Microsoft.PowerShell");
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
