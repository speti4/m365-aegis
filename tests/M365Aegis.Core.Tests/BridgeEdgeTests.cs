using System.Text.Json.Nodes;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Bridge;
using M365Aegis.Core.Config;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;
using M365Aegis.Core.Tests.Auth;
using M365Aegis.Core.Tests.Config;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests;

/// <summary>
/// The window's services and its Exchange holder at their edges: the services of a home folder, a lookup's failure
/// as a reply, the verbose log of a request and of a keep-warm tick, the tick that finds another one running.
/// </summary>
public sealed class BridgeEdgeTests
{
    private static readonly ExoCommand Domains = new(ExoCmdlets.GetAcceptedDomain);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheWindowsServicesOverAHomeFolderStartNothing()
    {
        string home = ProfileStoreTests.TempHome();

        BridgeServices services = BridgeServices.ForWindow(home, "work", () => IntPtr.Zero, run => run());
        await using WindowExchange exchange = services.Exchange!;

        Assert.Equal("work", services.Profile);
        Assert.Same(SignInHints.Window, services.Auth.Hints);
        Assert.Null(exchange.Session);
        Assert.Throws<ArgumentNullException>(() => BridgeServices.ForWindow(home, null, null!, run => run()));
    }

    [Theory]
    [InlineData(FailureKind.NotFound, true, "error")]
    [InlineData(FailureKind.Partial, false, "error")]
    [InlineData(FailureKind.Partial, true, "members")]
    public void ALookupsFailureIsAnErrorUnlessItIsAPartialAnswerWithItsData(FailureKind kind, bool withData, string type)
    {
        var request = new BridgeMessage(BridgeProtocol.Version, "1", "group-members", new JsonObject { ["group"] = "Finance" });
        CommandResult<string> result = CommandResult.Failure(new AegisFailure(kind, "some of it", null), withData ? "data" : null);

        BridgeMessage reply = Dispatcher().Answer(request, BridgeMessageType.Members, result);

        Assert.Equal(type, reply.Type);
    }

    [Fact]
    public async Task AWindowRequestUnderVerboseIsLoggedWithItsLayers()
    {
        string home = ProfileStoreTests.TempHome();
        await using var exchange = new WindowExchange(() => SidecarFixtures.Session());

        using (LogSink.UseVerbose(LogSink.ForHome(home)))
        {
            BridgeMessage? reply = await Dispatcher(exchange).DispatchAsync(new BridgeMessage(BridgeProtocol.Version, "2", "mailbox-list", null), Ct);
            Assert.Equal("mailboxes", reply?.Type);
        }

        string log = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(home, LogSink.FolderName))));
        Assert.Contains("mailbox-list answered in", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHolderOverATokenSourceNamesItsAcquisitions()
    {
        await using var exchange = new WindowExchange(
            () => new ExchangeTokenSource(_ => Task.FromResult(SidecarFixtures.Token(DateTimeOffset.UtcNow.AddHours(1)))),
            _ => SidecarFixtures.Session(),
            _ => null,
            TimeProvider.System);

        await using ExchangeBackends backends = exchange.Backends();

        Assert.False(backends.HasAdminApi);
        Assert.Equal("no sidecar, token acquisitions 0", exchange.Describe());
    }

    [Fact]
    public async Task AKeepWarmTickRenewsUnderVerboseAndASecondTickWaitsNot()
    {
        string home = ProfileStoreTests.TempHome();
        var clock = new TimerClock(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));
        var second = new TaskCompletionSource<ExchangeToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        int asked = 0;
        await using var exchange = new WindowExchange(
            () => SidecarFixtures.Session(
                token: _ => ++asked == 1 ? Task.FromResult(SidecarFixtures.Token(clock.GetUtcNow().AddMinutes(60))) : second.Task,
                clock: clock),
            clock);
        await exchange.InvokeAsync(Domains, Ct);
        clock.Advance(TimeSpan.FromMinutes(55));

        using (LogSink.UseVerbose(LogSink.ForHome(home)))
        {
            clock.Tick();
            // The first tick waits for its token; a second one finds it running and returns.
            clock.Tick();
            second.SetResult(SidecarFixtures.Token(clock.GetUtcNow().AddMinutes(60), "token-2"));
            await WaitUntil(() => exchange.KeptWarm == 1);
        }

        Assert.Equal(1, exchange.KeptWarm);
        string log = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(home, LogSink.FolderName))));
        Assert.Contains("exchange session kept warm", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AKeepWarmTickThatFailsIsLoggedUnderVerbose()
    {
        string home = ProfileStoreTests.TempHome();
        var clock = new TimerClock(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));
        int asked = 0;
        await using var exchange = new WindowExchange(
            () => SidecarFixtures.Session(
                token: _ => ++asked == 1
                    ? Task.FromResult(SidecarFixtures.Token(clock.GetUtcNow().AddMinutes(60)))
                    : throw new AegisException(FailureKind.Auth, "no silent sign-in", "sign in again"),
                clock: clock),
            clock);
        await exchange.InvokeAsync(Domains, Ct);
        clock.Advance(TimeSpan.FromMinutes(55));

        using (LogSink.UseVerbose(LogSink.ForHome(home)))
        {
            Assert.False(await exchange.RenewIfDueAsync(Ct));
        }

        string log = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(home, LogSink.FolderName))));
        Assert.Contains("exchange session not renewed", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASessionThatNeverConnectedSaysSo()
    {
        await using var exchange = new WindowExchange(() => SidecarFixtures.Session(
            token: _ => throw new AegisException(FailureKind.Auth, "no silent sign-in", "sign in again")));

        await Assert.ThrowsAsync<AegisException>(() => exchange.InvokeAsync(Domains, Ct));

        Assert.EndsWith("last connect none", exchange.Describe(), StringComparison.Ordinal);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(50, Ct);
        }
    }

    private static BridgeDispatcher Dispatcher(WindowExchange? exchange = null)
    {
        string home = ProfileStoreTests.TempHome();
        new ProfileStore(home).Save(new TenantProfile("work", "contoso.onmicrosoft.com", "00000000-0000-0000-0000-000000000010", "alice@contoso.com"));
        (GraphClient graph, _) = GraphFixtures.Client();
        return new BridgeDispatcher(new BridgeServices(
            new AuthService(home, _ => new FakeTokenBroker(), SignInHints.Window),
            null,
            _ => graph,
            exception => InternalError.WriteToLog(exception, home))
        {
            Exchange = exchange,
        });
    }

    /// <summary>A clock the test moves by hand, whose timer fires when the test says so.</summary>
    private sealed class TimerClock(DateTimeOffset now) : TimeProvider
    {
        private readonly List<TimerCallback> _callbacks = [];
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;

        public void Tick()
        {
            foreach (TimerCallback callback in _callbacks.ToList())
            {
                callback(null);
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callbacks.Add(callback);
            return new NoTimer();
        }

        private sealed class NoTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
