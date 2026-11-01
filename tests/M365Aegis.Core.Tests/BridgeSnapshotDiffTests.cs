using System.Text.Json.Nodes;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Bridge;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;
using M365Aegis.Core.Tests.Auth;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests;

/// <summary>
/// The window's diff card over the bridge (task 115): <c>snapshot-diff</c> answers from the newest snapshot of the
/// window's profile and the one before it as <c>snapshot diff</c> does (task 098), for one user when asked (task 114),
/// with no sign-in and no request to any service, held against the samples under tests/contracts/bridge, which the
/// front end's Vitest tests read too; fewer than two snapshots is the plain <c>too-few-snapshots</c> state.
/// </summary>
public sealed class BridgeSnapshotDiffTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>Two hours after the changed snapshot was taken, as the sample's ages say.</summary>
    private static readonly DateTimeOffset Soon = SnapshotSamples.ChangedTaken.AddHours(2);

    [Fact]
    public async Task TheNewestAndTheOneBeforeItAreTheSnapshotDiffReportSampleWithoutASignIn()
    {
        var broker = new FakeTokenBroker();
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();
        string home = await HomeWithTwoSnapshotsAsync();

        BridgeMessage? reply = await Dispatcher(home, broker, graph).DispatchAsync(BridgeWindow.Sample(BridgeMessageType.SnapshotDiff), None);

        AssertIs(BridgeWindow.SampleNode(BridgeMessageType.SnapshotDiffReport), reply);
        Assert.Empty(broker.Calls);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task OneUsersChangesAreTheUserVariantOfTheSample()
    {
        string home = await HomeWithTwoSnapshotsAsync();

        BridgeMessage? reply = await Dispatcher(home).DispatchAsync(Request("""{"user":" bob@contoso.com ","from":" ","to":""}"""), None);

        AssertIs(JsonNode.Parse(File.ReadAllText(Path.Combine(BridgeWindow.Contracts, "snapshot-diff-report.user.json")))!, reply);
    }

    [Fact]
    public async Task TwoNamedSnapshotsAreComparedAsNamed()
    {
        string home = await HomeWithTwoSnapshotsAsync();

        BridgeMessage? reply = await Dispatcher(home).DispatchAsync(Request("""{"from":"latest","to":"2026-10-31"}"""), None);

        Assert.Equal(BridgeMessageType.SnapshotDiffReport, reply!.Type);
        Assert.Equal("2026-11-01T060000Z.sqlite", reply.Data!["from"]!["snapshot"]!.GetValue<string>());
        Assert.Equal("2026-10-31", reply.Data!["to"]!["pick"]!.GetValue<string>());
    }

    [Fact]
    public async Task AProfileWithoutASnapshotIsTheTooFewSnapshotsSample()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());

        BridgeMessage? reply = await Dispatcher(home).DispatchAsync(BridgeWindow.Sample(BridgeMessageType.SnapshotDiff), None);

        AssertIs(BridgeWindow.SampleNode(BridgeMessageType.TooFewSnapshots), reply);
        Assert.False(SignInHints.NamesACommand(reply!.Data!["hint"]!.GetValue<string>()));
    }

    [Fact]
    public async Task OneSnapshotOrNoneBeforeThePickedOneIsTooFewWithItsCount()
    {
        string one = SnapshotSamples.WriteProfile(PlanSamples.TempHome());
        await SnapshotSamples.SeedAsync(one);
        string two = await HomeWithTwoSnapshotsAsync();

        BridgeMessage? single = await Dispatcher(one).DispatchAsync(BridgeWindow.Sample(BridgeMessageType.SnapshotDiff), None);
        BridgeMessage? first = await Dispatcher(two).DispatchAsync(Request("""{"to":"2026-10-31T060000Z"}"""), None);

        Assert.Equal((BridgeMessageType.TooFewSnapshots, 1), (single!.Type, single.Data!["snapshots"]!.GetValue<int>()));
        Assert.Equal("only one snapshot of the profile 'contoso' so far", single.Data!["message"]!.GetValue<string>());
        Assert.Equal((BridgeMessageType.TooFewSnapshots, 2), (first!.Type, first.Data!["snapshots"]!.GetValue<int>()));
        Assert.Equal("no snapshot of the profile 'contoso' before the one picked", first.Data!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task APickThatIsNoneOrAUserNeitherKnowsIsAnErrorWithTheWindowsHint()
    {
        string home = await HomeWithTwoSnapshotsAsync();

        BridgeMessage? word = await Dispatcher(home).DispatchAsync(Request("""{"from":"last-week"}"""), None);
        BridgeMessage? user = await Dispatcher(home).DispatchAsync(Request("""{"user":"nobody@contoso.com"}"""), None);

        AssertError(word, "usage", SignInHints.Window.Failures[FailureKind.Usage]);
        AssertError(user, "not-found", SignInHints.Window.Failures[FailureKind.NotFound]);
        Assert.Equal(
            "neither 2026-10-31T060000Z.sqlite nor 2026-11-01T060000Z.sqlite knows a user 'nobody@contoso.com'",
            user!.Data!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task AWindowWithoutAProfileSendsToTheAdministrator()
    {
        BridgeMessage? reply = await Dispatcher(PlanSamples.TempHome()).DispatchAsync(BridgeWindow.Sample(BridgeMessageType.SnapshotDiff), None);

        AssertError(reply, "auth", SignInHints.Window.NoProfile);
    }

    private static async Task<string> HomeWithTwoSnapshotsAsync()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());
        await SnapshotSamples.SeedAsync(home);
        await SnapshotSamples.SeedChangedAsync(home);
        return home;
    }

    private static BridgeMessage Request(string data) =>
        new(BridgeProtocol.Version, "17", BridgeMessageType.SnapshotDiff, JsonNode.Parse(data));

    /// <summary>The window's dispatcher over the home, with the window's hints, a broker that signs nobody in, and a clock two hours after the second take.</summary>
    private static BridgeDispatcher Dispatcher(string home, FakeTokenBroker? broker = null, GraphClient? graph = null)
    {
        broker ??= new FakeTokenBroker();
        graph ??= GraphFixtures.Client().Client;
        return new BridgeDispatcher(new BridgeServices(
            new AuthService(home, _ => broker, SignInHints.Window),
            null,
            _ => graph,
            exception => InternalError.WriteToLog(exception, home))
        {
            Clock = new SnapshotSamples.FixedClock(Soon),
        });
    }

    private static void AssertError(BridgeMessage? reply, string code, string hint)
    {
        Assert.NotNull(reply);
        Assert.Equal(BridgeMessageType.Error, reply.Type);
        Assert.Equal(code, reply.Data?["code"]?.GetValue<string>());
        Assert.Equal(hint, reply.Data?["hint"]?.GetValue<string>());
    }

    private static void AssertIs(JsonNode expected, BridgeMessage? reply)
    {
        Assert.NotNull(reply);
        JsonNode actual = JsonNode.Parse(BridgeProtocol.Write(reply))!;
        Assert.True(JsonNode.DeepEquals(expected, actual), $"the sample differs from the reply:\n{actual.ToJsonString()}");
    }
}
