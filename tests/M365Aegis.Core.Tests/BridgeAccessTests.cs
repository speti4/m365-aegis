using System.Text.Json.Nodes;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Bridge;
using M365Aegis.Core.Config;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Core.Tests.Auth;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests;

/// <summary>
/// The window's access card over the bridge (task 102): <c>access</c> answers from the newest snapshot of the window's
/// profile as <c>access &lt;user&gt;</c> does (task 084), with no sign-in and no request to any service, and held against
/// the samples under tests/contracts/bridge, which the front end's Vitest tests read too. And the group owner write's
/// plan as a later card will receive it (task 104): its last-owner warning, made by the core over the recorded answers.
/// </summary>
public sealed class BridgeAccessTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>Two hours after the fixture snapshot was taken, as the sample's age says.</summary>
    private static readonly DateTimeOffset Soon = SnapshotSamples.Taken.AddHours(2);

    [Fact]
    public async Task DavesAccessIsTheAccessReportSampleWithoutASignIn()
    {
        var broker = new FakeTokenBroker();
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();
        string home = await HomeWithTheFixtureSnapshotAsync();

        BridgeMessage? reply = await Dispatcher(home, broker, graph).DispatchAsync(BridgeWindow.Sample(BridgeMessageType.Access), None);

        AssertIs(BridgeWindow.SampleNode(BridgeMessageType.AccessReport), reply);
        Assert.Empty(broker.Calls);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task AProfileWithoutASnapshotIsTheNoSnapshotSample()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());

        BridgeMessage? reply = await Dispatcher(home).DispatchAsync(BridgeWindow.Sample(BridgeMessageType.Access), None);

        AssertIs(BridgeWindow.SampleNode(BridgeMessageType.NoSnapshot), reply);
        Assert.False(SignInHints.NamesACommand(reply!.Data!["hint"]!.GetValue<string>()));
    }

    [Fact]
    public async Task AUserTheSnapshotDoesNotKnowIsNotFoundWithTheWindowsHint()
    {
        string home = await HomeWithTheFixtureSnapshotAsync();

        BridgeMessage? reply = await Dispatcher(home).DispatchAsync(Request("""{"user":"nobody@contoso.com"}"""), None);

        AssertError(reply, "not-found", SignInHints.Window.Failures[FailureKind.NotFound]);
        Assert.Equal("the snapshot 2026-10-31T060000Z.sqlite knows no user 'nobody@contoso.com'", reply!.Data!["message"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{}""")]
    [InlineData("""{"user":"  "}""")]
    [InlineData("""{"user":42}""")]
    public async Task AnAccessRequestWithoutAUserIsAUsageError(string? data)
    {
        string home = await HomeWithTheFixtureSnapshotAsync();

        BridgeMessage? reply = await Dispatcher(home).DispatchAsync(Request(data), None);

        AssertError(reply, "usage", "give the user's user principal name, address or object id");
    }

    [Fact]
    public async Task AWindowWithoutAProfileSendsToTheAdministrator()
    {
        BridgeMessage? reply = await Dispatcher(PlanSamples.TempHome()).DispatchAsync(BridgeWindow.Sample(BridgeMessageType.Access), None);

        AssertError(reply, "auth", SignInHints.Window.NoProfile);
    }

    [Fact]
    public async Task AProfileWhoseNameCannotBeAFolderIsAUsageErrorInTheWindowsWords()
    {
        string home = PlanSamples.TempHome();
        new ProfileStore(home).Save(SnapshotSamples.Profile with { Name = "contoso|eu" }, makeDefault: true);

        BridgeMessage? reply = await Dispatcher(home).DispatchAsync(BridgeWindow.Sample(BridgeMessageType.Access), None);

        AssertError(reply, "usage", SignInHints.Window.Failures[FailureKind.Usage]);
    }

    [Fact]
    public async Task AUserSearchIsTheUserMatchesSampleWithoutASignIn()
    {
        var broker = new FakeTokenBroker();
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();
        string home = await HomeWithTheFixtureSnapshotAsync();

        BridgeMessage? reply = await Dispatcher(home, broker, graph).DispatchAsync(BridgeWindow.Sample(BridgeMessageType.UserSearch), None);

        AssertIs(BridgeWindow.SampleNode(BridgeMessageType.UserMatches), reply);
        Assert.Empty(broker.Calls);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task AUserSearchWithoutASnapshotIsTheNoSnapshotState()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());

        BridgeMessage? reply = await Dispatcher(home).DispatchAsync(BridgeWindow.Sample(BridgeMessageType.UserSearch), None);

        // The access card's state, with the user search's own id.
        Assert.Equal((BridgeMessageType.NoSnapshot, "17"), (reply?.Type, reply?.Id));
        Assert.True(JsonNode.DeepEquals(BridgeWindow.SampleNode(BridgeMessageType.NoSnapshot)["data"], reply!.Data));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{}""")]
    [InlineData("""{"text":"  "}""")]
    [InlineData("""{"text":42}""")]
    public async Task AUserSearchWithoutATextIsAUsageError(string? data)
    {
        string home = await HomeWithTheFixtureSnapshotAsync();
        BridgeMessage request = new(BridgeProtocol.Version, "17", BridgeMessageType.UserSearch, data is null ? null : JsonNode.Parse(data));

        BridgeMessage? reply = await Dispatcher(home).DispatchAsync(request, None);

        AssertError(reply, "usage", "type the start of a name or an address");
    }

    [Fact]
    public async Task TheGroupOwnerPlanSampleIsTheCoresPlanOfALastOwnersRemoval()
    {
        (GraphClient graph, _) = GraphFixtures.Client();
        WritePlan write = await GroupWrites.PlanAsync(graph, GroupRelation.Owners, add: false, GroupWrites.FromArguments("Finance", ["alice@contoso.com"]), None);
        Plan shown = (await PlanGuard.RunAsync(write, WriteMode.DryRun, PlanSamples.Store(PlanSamples.TempHome()), None)).Data!;
        BridgeMessage request = new(BridgeProtocol.Version, "16", "group-owners-remove", null);

        BridgeMessage reply = BridgeProtocol.Reply(request, BridgeMessageType.Plan, new PlanReady("0123456789abcdef0123456789abcdef", shown));

        JsonNode sample = JsonNode.Parse(File.ReadAllText(Path.Combine(BridgeWindow.Contracts, "plan.group-owners-remove.json")))!;
        AssertIs(sample, reply);
        // The warning names a command of the command line: the window's wording map (plan-wording.ts) rewords it.
        Assert.Contains("`group owners-missing`", sample["data"]!["plan"]!["warnings"]![0]!.GetValue<string>(), StringComparison.Ordinal);
    }

    private static async Task<string> HomeWithTheFixtureSnapshotAsync()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());
        await SnapshotSamples.SeedAsync(home);
        return home;
    }

    private static BridgeMessage Request(string? data) =>
        new(BridgeProtocol.Version, "15", BridgeMessageType.Access, data is null ? null : JsonNode.Parse(data));

    /// <summary>The window's dispatcher over the home, with the window's hints, a broker that signs nobody in, and a clock two hours after the take.</summary>
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
