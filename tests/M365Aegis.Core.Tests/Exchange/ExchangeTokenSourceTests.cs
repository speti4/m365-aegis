using M365Aegis.Core.Auth;
using M365Aegis.Core.Config;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;
using M365Aegis.Core.Tests.Auth;
using M365Aegis.Core.Tests.Config;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Exchange;

/// <summary>
/// One Exchange Online token for every layer of a command (task 049): the source asks the sign-in once however many
/// layers ask at once, renews a token that is due once, and the sidecar's reconnect and the Admin API read the same
/// expiry. The layered read runs the sidecar over the fake module and the Admin API over the recorded answers.
/// </summary>
public sealed class ExchangeTokenSourceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LayersAskingAtOnceShareOneAcquisition()
    {
        var gate = new TaskCompletionSource();
        int fetched = 0;
        var tokens = new ExchangeTokenSource(async _ =>
        {
            Interlocked.Increment(ref fetched);
            await gate.Task;
            return Token(Noon.AddHours(1));
        });

        Task<ExchangeToken>[] asks = [tokens.GetAsync(Ct), tokens.GetAsync(Ct), tokens.RenewAsync(Ct)];
        gate.SetResult();
        ExchangeToken[] answers = await Task.WhenAll(asks);

        Assert.Equal(1, fetched);
        Assert.Equal(1, tokens.Acquisitions);
        Assert.All(answers, answer => Assert.Same(answers[0], answer));
    }

    [Fact]
    public async Task ATokenIsKeptUntilItIsDueThenRenewedOnce()
    {
        var clock = new ManualClock(Noon);
        int fetched = 0;
        var tokens = new ExchangeTokenSource(_ => Task.FromResult(Token(clock.GetUtcNow().AddHours(1), $"token-{Interlocked.Increment(ref fetched)}")), clock);

        ExchangeToken first = await tokens.GetAsync(Ct);
        clock.Advance(TimeSpan.FromMinutes(54));
        Assert.Same(first, await tokens.GetAsync(Ct));
        Assert.False(tokens.IsDue(first));

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(tokens.IsDue(first));
        ExchangeToken[] renewed = await Task.WhenAll(tokens.GetAsync(Ct), tokens.GetAsync(Ct));

        Assert.Equal("token-2", renewed[0].AccessToken);
        Assert.Same(renewed[0], renewed[1]);
        Assert.Equal(2, tokens.Acquisitions);
    }

    [Fact]
    public async Task AnExplicitRenewalFetchesAgainAndAFailureIsNotKept()
    {
        int fetched = 0;
        var tokens = new ExchangeTokenSource(_ => Interlocked.Increment(ref fetched) == 2
            ? throw new AegisException(FailureKind.Auth, "no silent sign-in", "sign in")
            : Task.FromResult(Token(DateTimeOffset.UtcNow.AddHours(1), $"token-{fetched}")));

        await tokens.GetAsync(Ct);
        await Assert.ThrowsAsync<AegisException>(() => tokens.RenewAsync(Ct));
        ExchangeToken after = await tokens.GetAsync(Ct);

        Assert.Equal("token-3", after.AccessToken);
        Assert.Equal(3, tokens.Acquisitions);
    }

    [Fact]
    public async Task TheProfilesSourceAsksTheBrokerOnceForSeveralLayers()
    {
        var broker = new FakeTokenBroker { Silent = (scope, account) => FakeTokenBroker.Token(scope, account, TokenOrigin.Broker) };
        broker.Accounts.Add(FakeTokenBroker.Alice);
        string home = ProfileStoreTests.TempHome();
        var auth = new AuthService(home, _ => broker);
        auth.Profiles.Save(new TenantProfile("work", "contoso.onmicrosoft.com", "00000000-0000-0000-0000-000000000010", "alice@contoso.com"));
        // Before the fake token's expiry (2026-10-29 15:00 +01:00), so it is not due.
        var tokens = ExchangeTokenSource.ForProfile(auth, null, new ManualClock(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero)));

        ExchangeToken[] answers = await Task.WhenAll(tokens.GetAsync(Ct), tokens.GetAsync(Ct), tokens.GetAsync(Ct));

        Assert.Single(broker.Calls, call => call == "silent:alice@contoso.com");
        Assert.Equal("alice@contoso.com", answers[0].Account);
        Assert.Equal(FakeTokenBroker.Alice.TenantId, answers[0].TenantId);
        Assert.Equal(1, tokens.Acquisitions);
    }

    [Fact]
    public async Task MailboxAccessOverBothLayersAcquiresOneToken()
    {
        var tokens = new ExchangeTokenSource(_ => Task.FromResult(Token(DateTimeOffset.UtcNow.AddHours(1))));
        await using ExchangeSession session = SidecarFixtures.Session(tokens.GetAsync);
        await using var backends = new ExchangeBackends(session, AdminApiConnection.Create(tokens, new FixtureGraphTransport(GraphFixtures.Root), null));

        CommandResult<MailboxAccess> result = await MailboxLookups.AccessAsync(backends, "finance", new MailboxAccessOptions(), Ct);

        Assert.True(result.Ok);
        Assert.Equal("admin-api+pwsh", backends.Backend);
        Assert.Equal(1, tokens.Acquisitions);
        Assert.Same(await tokens.GetAsync(Ct), session.Token);
    }

    [Fact]
    public async Task TheSidecarsReconnectAndTheAdminApiReadTheSameRenewal()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        int fetched = 0;
        var tokens = new ExchangeTokenSource(_ => Task.FromResult(Token(clock.GetUtcNow().AddHours(1), $"token-{Interlocked.Increment(ref fetched)}")), clock);
        var transport = new FixtureGraphTransport(GraphFixtures.Root);
        await using var session = new ExchangeSession(new ExchangeSessionOptions
        {
            Start = cancellationToken => SidecarClient.StartAsync(SidecarFixtures.StartInfo(), cancellationToken),
            Token = tokens.GetAsync,
            RenewToken = tokens.RenewAsync,
            Target = _ => Task.FromResult(new ExchangeTarget(SidecarFixtures.Organization, null)),
            Clock = tokens.Clock,
            RenewBefore = tokens.RenewBefore,
        });
        AdminApiClient admin = AdminApiConnection.Create(tokens, transport, null);
        var domains = new ExoCommand(ExoCmdlets.GetAcceptedDomain) { Properties = ["DomainName"] };

        await session.InvokeAsync(domains, Ct);
        clock.Advance(TimeSpan.FromMinutes(56));
        await session.InvokeAsync(domains, Ct);
        await admin.InvokeAsync(new ExoCommand(AdminApiCmdlets.GetOrganizationConfig), Ct);

        Assert.Equal(1, session.Renewals);
        Assert.Equal("token-2", session.Token!.AccessToken);
        Assert.Same(session.Token, await tokens.GetAsync(Ct));
        Assert.Equal(2, tokens.Acquisitions);

        await session.RenewAsync(Ct);
        Assert.Equal("token-3", session.Token!.AccessToken);
        Assert.Equal(3, tokens.Acquisitions);
    }

    private static ExchangeToken Token(DateTimeOffset expiresOn, string accessToken = "fake-token") =>
        new(accessToken, expiresOn, ["Exchange.Manage", "Exchange.ManageV2"], AdminApiConnection.TestAccount, AdminApiConnection.TestTenantId);

    /// <summary>A clock the test moves by hand.</summary>
    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
