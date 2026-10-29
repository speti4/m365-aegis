using M365Aegis.Core.Auth;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Exchange.AdminApi;

/// <summary>
/// Makes the Admin API client a command uses (task 042): over the network with the profile's token for
/// <c>https://outlook.office365.com/.default</c> from <see cref="AuthService.GetTokenAsync"/> (silent only, the same
/// token the sidecar connects with; the tenant's id and the account come with it), or over the recorded fixtures when
/// <see cref="GraphConnection.FixturesEnvironmentVariable"/> names a folder (the answers are under its
/// <c>outlook.office365.com/</c>, for the tenant <see cref="TestTenantId"/> and the account <c>alice@contoso.com</c>).
/// With only <see cref="ExchangeConnection.TestOrganizationVariable"/> set, the sidecar-only offline tests, there is no
/// Admin API, so a lookup takes the sidecar's way.
/// </summary>
public static class AdminApiConnection
{
    /// <summary>The tenant id of the recorded Admin API answers.</summary>
    public const string TestTenantId = "00000000-0000-0000-0000-0000000000a0";

    /// <summary>The account of the recorded Admin API answers.</summary>
    public const string TestAccount = "alice@contoso.com";

    /// <summary>The client of a command, or null when the run has no Admin API (the sidecar-only offline tests).</summary>
    /// <param name="profileName">The tenant profile (<c>--profile</c>), or null for the default one.</param>
    /// <param name="requestLog">Where the request ids go (the command runner's log).</param>
    /// <param name="auth">The sign-in; not called when the fixtures answer.</param>
    public static AdminApiClient? Create(string? profileName, RequestLog requestLog, Func<AuthService> auth)
    {
        ArgumentNullException.ThrowIfNull(auth);
        string? fixtures = Environment.GetEnvironmentVariable(GraphConnection.FixturesEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fixtures))
        {
            return Fixtures(Path.GetFullPath(fixtures), requestLog);
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ExchangeConnection.TestOrganizationVariable)))
        {
            return null;
        }

        return Create(auth(), profileName, requestLog);
    }

    /// <summary>The client over the recorded answers under <paramref name="root"/> (the tests' <c>tests/fixtures/graph</c>).</summary>
    public static AdminApiClient Fixtures(string root, RequestLog? requestLog = null, GraphClientOptions? options = null) =>
        new(new FixtureGraphTransport(root), _ => Task.FromResult(new AdminApiTarget(TestTenantId, TestAccount)), requestLog, options);

    /// <summary>The client over the network with the given sign-in; the token is asked for on the first call.</summary>
    public static AdminApiClient Create(AuthService auth, string? profileName, RequestLog requestLog, GraphClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(auth);
        var tokens = new TokenSource(cancellationToken => auth.GetTokenAsync(Resource.Exchange, profileName, cancellationToken));
        return new AdminApiClient(
            new HttpAdminApiTransport(async cancellationToken => (await tokens.GetAsync(cancellationToken).ConfigureAwait(false)).AccessToken),
            async cancellationToken =>
            {
                TokenResult token = await tokens.GetAsync(cancellationToken).ConfigureAwait(false);
                string tenant = token.TenantId ?? token.Account.TenantId ?? throw new AegisException(
                    FailureKind.Auth,
                    "the Exchange Online token names no tenant, which the Admin API's URL needs",
                    "run `m365-aegis login` again");
                return new AdminApiTarget(tenant, token.Account.Username);
            },
            requestLog,
            options);
    }

    /// <summary>One token for the calls of a client, asked for again five minutes before it expires (a long-lived window).</summary>
    private sealed class TokenSource(Func<CancellationToken, Task<TokenResult>> fetch)
    {
        private readonly Lock _lock = new();
        private Task<TokenResult>? _token;

        internal async Task<TokenResult> GetAsync(CancellationToken cancellationToken)
        {
            Task<TokenResult> pending;
            lock (_lock)
            {
                if (_token is null || _token.IsFaulted || _token.IsCanceled
                    || (_token.IsCompletedSuccessfully && _token.Result.ExpiresOn - DateTimeOffset.UtcNow < TimeSpan.FromMinutes(5)))
                {
                    _token = fetch(cancellationToken);
                }

                pending = _token;
            }

            return await pending.ConfigureAwait(false);
        }
    }
}
