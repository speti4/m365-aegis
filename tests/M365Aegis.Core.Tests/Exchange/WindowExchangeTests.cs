using M365Aegis.Core.Bridge;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Exchange;

/// <summary>
/// The window's one Exchange Online session (task 045): nothing until the first cmdlet, one sidecar for every later
/// one, the keep-warm renewal before the token's expiry, a sign-out that ends the sidecar, and the window's close.
/// A real pwsh child against the fake module, as <see cref="SidecarTests"/>.
/// </summary>
public sealed class WindowExchangeTests
{
    private static readonly ExoCommand Domains = new(ExoCmdlets.GetAcceptedDomain);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NothingStartsBeforeTheFirstCmdlet()
    {
        int made = 0;
        await using var exchange = new WindowExchange(() =>
        {
            made++;
            return SidecarFixtures.Session();
        });

        Assert.Null(exchange.Session);
        Assert.Equal(0, made);
        Assert.Equal("no sidecar", exchange.Describe());
        Assert.False(await exchange.RenewIfDueAsync(Ct));
    }

    [Fact]
    public async Task EveryCmdletRunsOnTheOneSidecarOfTheWindow()
    {
        await using var exchange = new WindowExchange(() => SidecarFixtures.Session());

        await exchange.InvokeAsync(Domains, Ct);
        SidecarClient first = exchange.Session!.Client!;
        ExoResult again = await exchange.InvokeAsync(Domains, Ct);

        Assert.Equal(2, again.Items.Count);
        Assert.Same(first, exchange.Session.Client);
        Assert.Equal(1, exchange.Sessions);
        Assert.Equal(1, exchange.Session.Starts);
        Assert.Equal(0, exchange.Session.Renewals);
        Assert.StartsWith("session 1, sidecar starts 1, renewals 0, last connect ", exchange.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheKeepWarmTickReconnectsAFreshTokenBeforeTheSessionWould()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));
        int asked = 0;
        await using var exchange = new WindowExchange(
            () => SidecarFixtures.Session(
                token: _ =>
                {
                    asked++;
                    return Task.FromResult(SidecarFixtures.Token(clock.GetUtcNow().AddMinutes(60), $"token-{asked}"));
                },
                clock: clock),
            clock);
        await exchange.InvokeAsync(Domains, Ct);

        clock.Advance(TimeSpan.FromMinutes(50));
        bool early = await exchange.RenewIfDueAsync(Ct);
        clock.Advance(TimeSpan.FromMinutes(4));
        bool due = await exchange.RenewIfDueAsync(Ct);
        await exchange.InvokeAsync(Domains, Ct);

        Assert.False(early);
        Assert.True(due);
        Assert.Equal(1, exchange.KeptWarm);
        Assert.Equal("token-2", exchange.Session!.Token!.AccessToken);
        // The request after the tick met a fresh token: no renewal of its own, the same sidecar.
        Assert.Equal(1, exchange.Session.Renewals);
        Assert.Equal(1, exchange.Session.Starts);
    }

    [Fact]
    public async Task AKeepWarmTickThatCannotRenewLeavesItToTheNextRequest()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));
        int asked = 0;
        await using var exchange = new WindowExchange(
            () => SidecarFixtures.Session(
                token: _ => ++asked == 1
                    ? Task.FromResult(SidecarFixtures.Token(clock.GetUtcNow().AddMinutes(60)))
                    : throw new AegisException(FailureKind.Auth, "no silent sign-in for alice@contoso.com", "run `m365-aegis login`"),
                clock: clock),
            clock);
        await exchange.InvokeAsync(Domains, Ct);
        clock.Advance(TimeSpan.FromMinutes(55));

        bool renewed = await exchange.RenewIfDueAsync(Ct);
        AegisException next = await Assert.ThrowsAsync<AegisException>(() => exchange.InvokeAsync(Domains, Ct));

        Assert.False(renewed);
        Assert.Equal(0, exchange.KeptWarm);
        Assert.Equal(FailureKind.Auth, next.Failure.Kind);
    }

    [Fact]
    public async Task ASignOutEndsTheSidecarAndTheNextRequestStartsANewSession()
    {
        await using var exchange = new WindowExchange(() => SidecarFixtures.Session());
        await exchange.InvokeAsync(Domains, Ct);
        SidecarClient first = exchange.Session!.Client!;

        await exchange.ResetAsync();
        Assert.Null(exchange.Session);
        Assert.True(first.HasExited);
        await exchange.InvokeAsync(Domains, Ct);

        Assert.Equal(2, exchange.Sessions);
        Assert.NotSame(first, exchange.Session!.Client);
    }

    [Fact]
    public async Task TheWindowsCloseEndsTheSidecarAndRefusesLaterCmdlets()
    {
        var exchange = new WindowExchange(() => SidecarFixtures.Session());
        await exchange.InvokeAsync(Domains, Ct);
        SidecarClient sidecar = exchange.Session!.Client!;

        await exchange.DisposeAsync();
        await exchange.DisposeAsync();

        Assert.True(sidecar.HasExited);
        Assert.Null(exchange.Session);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => exchange.InvokeAsync(Domains, Ct));
        Assert.False(await exchange.RenewIfDueAsync(Ct));
    }

    [Fact]
    public async Task ACmdletOffTheAllowListIsRefusedBeforeASidecarStarts()
    {
        await using var exchange = new WindowExchange(() => SidecarFixtures.Session());

        AegisException refused = await Assert.ThrowsAsync<AegisException>(() => exchange.InvokeAsync(new ExoCommand("Remove-Mailbox"), Ct));

        Assert.Equal(FailureKind.CmdletNotAllowed, refused.Failure.Kind);
        Assert.Null(exchange.Session);
    }

    /// <summary>A clock the test moves by hand.</summary>
    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
