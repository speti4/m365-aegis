using System.Text.Json;
using M365Aegis.Cli.Commands;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Cli.Tests;

/// <summary>
/// <c>call</c> as a child process over the recorded answers (<c>M365AEGIS_GRAPH_FIXTURES</c>), and its table in
/// process, byte for byte.
/// </summary>
public sealed class CallCommandTests
{
    /// <summary>The environment that points the exe's Graph client at the fixtures.</summary>
    internal static Dictionary<string, string> Fixtures { get; } = new() { [GraphConnection.FixturesEnvironmentVariable] = GraphFixtures.Root };

    [Fact]
    public void AGetIsTheAnswerUnchangedWithItsRequestId()
    {
        (int exitCode, string stdout, string stderr) = CliTests.RunWith(Fixtures, "--json", "call", "GET", "/me");

        Assert.Equal(0, exitCode);
        Assert.Equal("", stderr);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("Alice Example", root.GetProperty("data").GetProperty("displayName").GetString());
        Assert.Equal("https://graph.microsoft.com/v1.0/$metadata#users/$entity", root.GetProperty("data").GetProperty("@odata.context").GetString());
        Assert.Equal("graph", root.GetProperty("meta").GetProperty("backend").GetString());
        Assert.Equal("00000000-0000-0000-0000-0000000000f1", Assert.Single(root.GetProperty("meta").GetProperty("requestIds").EnumerateArray()).GetString());
    }

    [Fact]
    public void BetaCallsTheBetaEndpoint()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Fixtures, "--json", "call", "GET", "/me", "--beta");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("Member", document.RootElement.GetProperty("data").GetProperty("userType").GetString());
    }

    [Fact]
    public void AWriteWithoutYesExitsSevenWithThePlan()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(
            Fixtures, "--json", "call", "POST", "/groups", "--body", "{\"displayName\":\"Finance\",\"mailEnabled\":false}");

        Assert.Equal(7, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement root = document.RootElement;
        Assert.Equal("refused-without-yes", root.GetProperty("error").GetProperty("code").GetString());
        JsonElement change = Assert.Single(root.GetProperty("data").GetProperty("changes").EnumerateArray());
        Assert.Equal("send", change.GetProperty("action").GetString());
        Assert.Equal("https://graph.microsoft.com/v1.0/groups", change.GetProperty("target").GetString());
        Assert.Equal("POST with the body {\"displayName\":\"Finance\",\"mailEnabled\":false}", change.GetProperty("detail").GetString());
    }

    [Fact]
    public void TheHiddenFixtureNameOptionPrintsTheFileAndSendsNothing()
    {
        // No fixture folder and no sign-in: the option answers locally.
        string path = "/admin/exchange/tracing/messageTraces?$filter=receivedDateTime ge 2026-10-18T12:00:00Z and receivedDateTime le 2026-10-20T12:00:00Z and senderAddress eq 'alice@contoso.com'&$top=5000";
        (int exitCode, string stdout, _) = CliTests.RunWith([], "--json", "call", "GET", path, "--fixture-name");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Uri uri = GraphClient.BuildUri(GraphRequest.Get(path));
        Assert.Equal(FixtureGraphTransport.RelativePath(uri), data.GetProperty("fullName").GetString());
        Assert.Equal(FixtureGraphTransport.ShortRelativePath(uri), data.GetProperty("storedName").GetString());
        Assert.Equal("local", document.RootElement.GetProperty("meta").GetProperty("backend").GetString());
        Assert.Equal(0, document.RootElement.GetProperty("meta").GetProperty("requestIds").GetArrayLength());
    }

    [Fact]
    public void AVersionInThePathAndBetaIsAUsageError()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Fixtures, "--json", "call", "GET", "/v1.0/me", "--beta");

        Assert.Equal(2, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("usage", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void ANotFoundAnswerExitsFourWithTheRequestId()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Fixtures, "--json", "call", "GET", "/users/00000000-0000-0000-0000-000000000099");

        Assert.Equal(4, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement root = document.RootElement;
        Assert.Equal("not-found", root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("00000000-0000-0000-0000-0000000000fa", Assert.Single(root.GetProperty("meta").GetProperty("requestIds").EnumerateArray()).GetString());
    }

    [Fact]
    public void AForbiddenAnswerExitsEightAtATerminal()
    {
        (int exitCode, string stdout, string stderr) = CliTests.RunWith(Fixtures, "--table", "call", "GET", "/directoryRoles");

        Assert.Equal(8, exitCode);
        Assert.Equal("", stdout);
        Assert.Contains("(forbidden)", stderr, StringComparison.Ordinal);
        Assert.Contains("hint: ", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAProfileOrFixturesTheCallIsAnAuthFailure()
    {
        // The tests' home has no config.json, so the token is refused before anything is sent.
        (int exitCode, string stdout, _) = CliTests.RunWith([], "--json", "call", "GET", "/me");

        Assert.Equal(3, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("auth", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnObjectAnswerIsAFieldPerRow()
    {
        string rendering = await RenderAsync(new GraphCallOptions("GET", "/me", false, null, false, false));

        await Verify(rendering);
    }

    [Fact]
    public async Task ACollectionAnswerIsARowPerItem()
    {
        string rendering = await RenderAsync(new GraphCallOptions("GET", "/users?$select=id,displayName,userPrincipalName&$top=2", false, null, false, false));

        await Verify(rendering);
    }

    [Fact]
    public async Task ARefusedWriteShowsThePlanAndTheError()
    {
        (GraphClient graph, _) = GraphFixtures.Client();
        WritePlan write = GraphCall.Plan(graph, new GraphCallOptions("DELETE", "/groups/00000000-0000-0000-0000-000000000031", false, null, false, false));
        CommandResult<Plan> result = await PlanGuard.RunAsync(write, WriteMode.Refuse, PlanSamples.Store(PlanSamples.TempHome()), TestContext.Current.CancellationToken);
        var output = new StringWriter();
        var error = new StringWriter();
        Renderer.Render(result, new EnvelopeMeta([], 12, Backend.Graph), PlanTableView.Instance, OutputMode.Table, output, error);

        await Verify($"--- stdout ---\n{output}--- stderr ---\n{error}");
    }

    private static async Task<string> RenderAsync(GraphCallOptions options)
    {
        (GraphClient graph, _) = GraphFixtures.Client();
        CommandResult<JsonElement?> result = await GraphCall.RunAsync(graph, options, TestContext.Current.CancellationToken);
        var output = new StringWriter();
        var error = new StringWriter();
        Renderer.Render(result, new EnvelopeMeta(graph.RequestLog.Ids, 12, Backend.Graph), CallTableView.Instance, OutputMode.Table, output, error);
        return $"--- stdout ---\n{output}--- stderr ---\n{error}";
    }
}
