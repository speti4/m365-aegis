using System.Text.Json.Nodes;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Bridge;
using M365Aegis.Core.Config;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Tests.Auth;
using M365Aegis.Core.Tests.Config;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests;

/// <summary>
/// A window over every layer at once, for the bridge's write tests (tasks 082 and 079): a temporary home with the
/// default profile (the plan records' folder), a signed-in account, the recorded Graph and Admin API answers through
/// fixture transports that write each request to <see cref="Requests"/>, and the window's layers as
/// <see cref="BridgeServices.ForWindow"/> makes them over one made-up token source, the session on the fake module,
/// which writes each write cmdlet it runs to <see cref="ExchangeWrites"/>. The clock stands still until a test moves it.
/// Each window has a sidecar of its own: an apply changes the fake module's data for the life of its process.
/// </summary>
internal sealed class BridgeWindow : IAsyncDisposable
{
    /// <summary>The bridge contract samples, next to the test assembly.</summary>
    internal static readonly string Contracts = Path.Combine(AppContext.BaseDirectory, "contracts", "bridge");

    private readonly string _sinks = Path.Combine(Path.GetTempPath(), "m365-aegis-tests", $"bridge-sinks-{Guid.NewGuid():N}");

    /// <summary>A window over the layers; <paramref name="adminApi"/> false leaves the Admin API out, as the sidecar-only offline tests have it.</summary>
    internal BridgeWindow(bool adminApi = true)
    {
        Directory.CreateDirectory(_sinks);
        Requests = Path.Combine(_sinks, "requests.jsonl");
        ExchangeWrites = Path.Combine(_sinks, "exchange-writes.jsonl");
        Home = ProfileStoreTests.TempHome();
        new ProfileStore(Home).Save(new TenantProfile("work", "contoso.onmicrosoft.com", "00000000-0000-0000-0000-000000000010", "alice@contoso.com"));
        Broker = new FakeTokenBroker();
        Broker.Accounts.Add(FakeTokenBroker.Alice);
        Exchange = new WindowExchange(
            () => new ExchangeTokenSource(_ => Task.FromResult(new ExchangeToken(
                "fake-token", DateTimeOffset.UtcNow.AddHours(1), ["Exchange.Manage", "Exchange.ManageV2"], AdminApiConnection.TestAccount, AdminApiConnection.TestTenantId))),
            tokens => SidecarFixtures.Session(tokens.GetAsync, writes: ExchangeWrites),
            tokens => adminApi ? AdminApiConnection.Create(tokens, new FixtureGraphTransport(GraphFixtures.Root) { RequestLog = Requests }, null) : null);
        var graph = new GraphClient(
            new FixtureGraphTransport(GraphFixtures.Root) { RequestLog = Requests },
            options: new GraphClientOptions { Delay = (_, _) => Task.CompletedTask, Jitter = () => 0 });
        Services = new BridgeServices(
            new AuthService(Home, _ => Broker, SignInHints.Window),
            null,
            _ => graph,
            exception => InternalError.WriteToLog(exception, Home))
        {
            Exchange = Exchange,
            Clock = Clock,
        };
        Dispatcher = new BridgeDispatcher(Services);
    }

    /// <summary>The temporary home folder: the profile, the log, the plan records.</summary>
    internal string Home { get; }

    /// <summary>The file the fixture transports append every request to.</summary>
    internal string Requests { get; }

    /// <summary>The file the fake module appends every write cmdlet to.</summary>
    internal string ExchangeWrites { get; }

    /// <summary>The sign-in's broker: Alice is signed in.</summary>
    internal FakeTokenBroker Broker { get; }

    /// <summary>The window's Exchange Online layers.</summary>
    internal WindowExchange Exchange { get; }

    /// <summary>The clock of the window's writes, at <see cref="PlanSamples.Now"/> until a test moves it.</summary>
    internal StepClock Clock { get; } = new(PlanSamples.Now);

    /// <summary>The services the dispatcher runs on.</summary>
    internal BridgeServices Services { get; }

    /// <summary>The dispatcher.</summary>
    internal BridgeDispatcher Dispatcher { get; }

    /// <summary>The plan records under the home folder's <c>plans</c>.</summary>
    internal string[] Records => Directory.Exists(Path.Combine(Home, "plans")) ? Directory.GetFiles(Path.Combine(Home, "plans")) : [];

    /// <summary>A sample message of tests/contracts/bridge, by its type.</summary>
    internal static BridgeMessage Sample(string type) =>
        BridgeProtocol.TryParse(File.ReadAllText(Path.Combine(Contracts, type + ".json")))
        ?? throw new InvalidOperationException($"{type}.json is not a message of the protocol");

    /// <summary>A sample message of tests/contracts/bridge as its JSON, by its type.</summary>
    internal static JsonNode SampleNode(string type) => JsonNode.Parse(File.ReadAllText(Path.Combine(Contracts, type + ".json")))!;

    /// <summary>Sends a request and returns the reply.</summary>
    internal async Task<BridgeMessage> SendAsync(BridgeMessage request) =>
        await Dispatcher.DispatchAsync(request, TestContext.Current.CancellationToken) ?? throw new InvalidOperationException($"no reply to {request.Type}");

    /// <summary>Sends a request of the given type and payload and returns the reply.</summary>
    internal Task<BridgeMessage> SendAsync(string type, string? data, string id = "1") =>
        SendAsync(new BridgeMessage(BridgeProtocol.Version, id, type, data is null ? null : JsonNode.Parse(data)));

    /// <summary>The plan id of a <c>plan</c> reply.</summary>
    internal static string PlanId(BridgeMessage reply)
    {
        Assert.Equal(BridgeMessageType.Plan, reply.Type);
        return reply.Data?["planId"]?.GetValue<string>() ?? throw new InvalidOperationException("the plan has no id");
    }

    /// <summary>An <c>apply-plan</c> request: the plan's id and the confirmation as given (none when null).</summary>
    internal static BridgeMessage Apply(string? planId, JsonNode? confirm, string id = "2")
    {
        var data = new JsonObject();
        if (planId is not null)
        {
            data["planId"] = planId;
        }

        if (confirm is not null)
        {
            data["confirm"] = confirm;
        }

        return new BridgeMessage(BridgeProtocol.Version, id, BridgeMessageType.ApplyPlan, data);
    }

    /// <summary>What reached a layer as a write: the requests that are not reads, and every write cmdlet of the fake module.</summary>
    internal List<string> Sent()
    {
        List<string> sent = SentRequests.Writes(File.Exists(Requests) ? File.ReadAllLines(Requests) : []);
        if (File.Exists(ExchangeWrites))
        {
            sent.AddRange(File.ReadAllLines(ExchangeWrites).Where(line => line.Length > 0).Select(line => $"fake module {line}"));
        }

        return sent;
    }

    public async ValueTask DisposeAsync() => await Exchange.DisposeAsync();

    /// <summary>A clock that stands still until <see cref="Advance"/> moves it.</summary>
    internal sealed class StepClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan by) => _now += by;
    }
}
