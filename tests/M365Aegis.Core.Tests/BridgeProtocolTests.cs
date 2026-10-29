using System.Text.Json.Nodes;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Bridge;
using M365Aegis.Core.Config;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Tests.Auth;
using M365Aegis.Core.Tests.Config;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests;

/// <summary>
/// The bridge between the window's front end and the core (tasks 017 and 030): the protocol, and the dispatcher's
/// answers held against the samples under tests/contracts/bridge, which the front end's Vitest tests read too. A
/// request sample dispatched over a scripted sign-in and the recorded Graph answers gives exactly its reply sample.
/// </summary>
public sealed class BridgeProtocolTests
{
    private static readonly string Contracts = Path.Combine(AppContext.BaseDirectory, "contracts", "bridge");
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task PingIsAnsweredWithPongAndTheVersion()
    {
        BridgeMessage request = Sample("ping.json");

        BridgeMessage? reply = await Dispatcher().DispatchAsync(request, None);

        Assert.NotNull(reply);
        Assert.Equal("pong", reply.Type);
        Assert.Equal(request.Id, reply.Id);
        Assert.Equal(ProductInfo.Version, reply.Data?["version"]?.GetValue<string>());
        Assert.Equal(ProductInfo.Name, reply.Data?["product"]?.GetValue<string>());
        // The pong sample has the same shape; its version is the sample's own.
        Assert.Equal(["product", "version"], Sample("pong.json").Data!.AsObject().Select(property => property.Key));
    }

    [Fact]
    public async Task ReadyIsANotificationWithNoAnswer()
    {
        BridgeMessage ready = Sample("ready.json");
        Assert.Null(ready.Id);

        Assert.Null(await Dispatcher().DispatchAsync(ready, None));
    }

    [Fact]
    public async Task WhoAmIOfASignedInAccountIsTheSignedInSample()
    {
        var broker = new FakeTokenBroker
        {
            OperatingSystemAccount = scope => FakeTokenBroker.Token(scope, FakeTokenBroker.Alice, TokenOrigin.Broker),
            Silent = (scope, account) => FakeTokenBroker.Token(scope, account, TokenOrigin.Broker),
        };

        BridgeMessage? reply = await Dispatcher(broker).DispatchAsync(Sample("whoami.json"), None);

        AssertIsSample("signed-in.json", reply);
    }

    [Fact]
    public async Task WhoAmIWithoutASilentSignInIsTheSignedOutSampleWithTheProfile()
    {
        var broker = new FakeTokenBroker();
        broker.Accounts.Add(FakeTokenBroker.Bob);

        BridgeMessage? reply = await Dispatcher(broker).DispatchAsync(Sample("whoami.json"), None);

        AssertIsSample("signed-out.json", reply);
        Assert.DoesNotContain(broker.Calls, call => call.StartsWith("interactive", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhoAmIWithoutAProfileIsSignedOutWithTheLoginHint()
    {
        string home = ProfileStoreTests.TempHome();
        var broker = new FakeTokenBroker();

        BridgeMessage? reply = await Dispatcher(broker, home).DispatchAsync(Sample("whoami.json"), None);

        Assert.NotNull(reply);
        Assert.Equal("signed-out", reply.Type);
        Assert.Null(reply.Data?["profile"]);
        Assert.Equal(ProfileStore.LoginHint, reply.Data?["hint"]?.GetValue<string>());
        Assert.Empty(broker.Calls);
    }

    [Fact]
    public async Task GroupMembersIsTheMembersSample()
    {
        BridgeMessage? reply = await Dispatcher().DispatchAsync(Sample("group-members.json"), None);

        AssertIsSample("members.json", reply);
    }

    [Fact]
    public async Task AGroupThatIsNotThereIsTheErrorSample()
    {
        BridgeMessage request = Sample("group-members.json") with { Id = "4", Data = new JsonObject { ["group"] = "Nothing" } };

        BridgeMessage? reply = await Dispatcher().DispatchAsync(request, None);

        AssertIsSample("error.json", reply);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{}""")]
    [InlineData("""{"group":"  "}""")]
    [InlineData("""{"group":42}""")]
    [InlineData("""["Finance"]""")]
    public async Task AGroupMembersRequestWithoutAGroupIsAUsageError(string? data)
    {
        var request = new BridgeMessage(BridgeProtocol.Version, "5", "group-members", data is null ? null : JsonNode.Parse(data));

        BridgeMessage? reply = await Dispatcher().DispatchAsync(request, None);

        Assert.NotNull(reply);
        Assert.Equal("error", reply.Type);
        Assert.Equal("5", reply.Id);
        Assert.Equal("usage", reply.Data?["code"]?.GetValue<string>());
    }

    [Fact]
    public async Task AnUnknownTypeIsAnError()
    {
        var request = new BridgeMessage(BridgeProtocol.Version, "7", "frobnicate", null);

        BridgeMessage? reply = await Dispatcher().DispatchAsync(request, None);

        Assert.NotNull(reply);
        Assert.Equal("error", reply.Type);
        Assert.Equal("unknown-type", reply.Data?["code"]?.GetValue<string>());
    }

    [Fact]
    public async Task AnUnexpectedExceptionIsTheInternalErrorWithTheStackTraceInTheLog()
    {
        string home = WithProfile();
        var services = new BridgeServices(
            new AuthService(home, _ => new FakeTokenBroker()),
            null,
            _ => throw new InvalidOperationException("the client broke for alice@contoso.com"),
            exception => InternalError.WriteToLog(exception, home));

        BridgeMessage? reply = await new BridgeDispatcher(services).DispatchAsync(Sample("group-members.json"), None);

        Assert.NotNull(reply);
        Assert.Equal("error", reply.Type);
        Assert.Equal("internal", reply.Data?["code"]?.GetValue<string>());
        string message = reply.Data!["message"]!.GetValue<string>();
        Assert.Contains("InvalidOperationException", message, StringComparison.Ordinal);
        Assert.DoesNotContain("alice@contoso.com", message, StringComparison.Ordinal);
        string log = Assert.Single(Directory.GetFiles(Path.Combine(home, LogSink.FolderName)));
        Assert.Contains(log, reply.Data["hint"]!.GetValue<string>(), StringComparison.Ordinal);
        string text = File.ReadAllText(log);
        Assert.Contains("internal-error: System.InvalidOperationException", text, StringComparison.Ordinal);
        Assert.DoesNotContain("alice@contoso.com", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EverySampleIsAMessageOfTheProtocol()
    {
        string[] samples = Directory.GetFiles(Contracts, "*.json");

        Assert.Equal(
            ["error.json", "group-members.json", "members.json", "ping.json", "pong.json", "ready.json", "signed-in.json", "signed-out.json", "whoami.json"],
            samples.Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.All(samples, path => Assert.NotNull(BridgeProtocol.TryParse(File.ReadAllText(path))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"aegis":2,"type":"ping"}""")]
    [InlineData("""{"type":"ping"}""")]
    [InlineData("""{"aegis":1}""")]
    public void ForeignTextIsRefused(string text)
    {
        Assert.Null(BridgeProtocol.TryParse(text));
    }

    [Fact]
    public void AMessageRoundTrips()
    {
        var message = new BridgeMessage(BridgeProtocol.Version, "42", "pong", new JsonObject { ["version"] = "1.2.3" });

        string json = BridgeProtocol.Write(message);
        BridgeMessage? parsed = BridgeProtocol.TryParse(json);

        Assert.Equal("""{"aegis":1,"id":"42","type":"pong","data":{"version":"1.2.3"}}""", json);
        Assert.NotNull(parsed);
        Assert.Equal(message.Type, parsed.Type);
        Assert.Equal(message.Id, parsed.Id);
    }

    private static BridgeMessage Sample(string fileName) =>
        BridgeProtocol.TryParse(File.ReadAllText(Path.Combine(Contracts, fileName)))
        ?? throw new InvalidOperationException($"{fileName} is not a message of the protocol");

    /// <summary>The reply, written as the host sends it, is the sample message, field by field.</summary>
    private static void AssertIsSample(string fileName, BridgeMessage? reply)
    {
        Assert.NotNull(reply);
        JsonNode expected = JsonNode.Parse(File.ReadAllText(Path.Combine(Contracts, fileName)))!;
        JsonNode actual = JsonNode.Parse(BridgeProtocol.Write(reply))!;
        Assert.True(JsonNode.DeepEquals(expected, actual), $"{fileName} differs from the reply:\n{actual.ToJsonString()}");
    }

    /// <summary>A home folder with one profile, the default: alice in contoso.</summary>
    private static string WithProfile()
    {
        string home = ProfileStoreTests.TempHome();
        new ProfileStore(home).Save(new TenantProfile("work", "contoso.onmicrosoft.com", "00000000-0000-0000-0000-000000000010", "alice@contoso.com"));
        return home;
    }

    /// <summary>The dispatcher over a scripted sign-in and the recorded Graph answers.</summary>
    private static BridgeDispatcher Dispatcher(FakeTokenBroker? broker = null, string? home = null)
    {
        home ??= WithProfile();
        broker ??= new FakeTokenBroker();
        (GraphClient graph, _) = GraphFixtures.Client();
        return new BridgeDispatcher(new BridgeServices(
            new AuthService(home, _ => broker),
            null,
            _ => graph,
            exception => InternalError.WriteToLog(exception, home)));
    }
}
