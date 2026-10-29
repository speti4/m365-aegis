using System.Text.Json;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Graph;

/// <summary>The raw call: the path's version, the answer unchanged, the write guard, the pages.</summary>
public sealed class GraphCallTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Theory]
    [InlineData("/me", false, GraphVersion.V1, "/me")]
    [InlineData("/me", true, GraphVersion.Beta, "/me")]
    [InlineData("/v1.0/me", false, GraphVersion.V1, "/me")]
    [InlineData("/beta/me", false, GraphVersion.Beta, "/me")]
    [InlineData("/beta/me", true, GraphVersion.Beta, "/me")]
    [InlineData("/v1.0", false, GraphVersion.V1, "/")]
    public void ThePathNamesTheVersionOrTheSwitchDoes(string path, bool beta, GraphVersion version, string rest)
    {
        GraphRequest request = GraphCall.Describe(new GraphCallOptions("get", path, beta, null, false, false));

        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(version, request.Version);
        Assert.Equal(rest, request.Path);
    }

    [Theory]
    [InlineData("GET", "/v1.0/me", true, null)]
    [InlineData("FETCH", "/me", false, null)]
    [InlineData("GET", "me", false, null)]
    [InlineData("GET", "/me", false, "{}")]
    [InlineData("POST", "/groups", false, "{not json")]
    public void AWrongCallIsAUsageError(string method, string path, bool beta, string? body)
    {
        AegisException exception = Assert.Throws<AegisException>(() => GraphCall.Describe(new GraphCallOptions(method, path, beta, body, false, false)));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
    }

    [Fact]
    public async Task AGetPrintsTheAnswerUnchanged()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        CommandResult<JsonElement?> result = await GraphCall.RunAsync(graph, new GraphCallOptions("GET", "/v1.0/me", false, null, false, false), None);

        Assert.True(result.Ok);
        string expected = File.ReadAllText(Path.Combine(GraphFixtures.Root, "graph.microsoft.com", "v1.0", "me.json"));
        using JsonDocument document = JsonDocument.Parse(expected);
        Assert.True(JsonElement.DeepEquals(document.RootElement, result.Data!.Value));
    }

    [Fact]
    public async Task AWriteWithoutYesIsThePlanAndSendsNothing()
    {
        var transport = new ScriptedTransport(_ => throw new InvalidOperationException("nothing may be sent"));
        var graph = new GraphClient(transport);

        CommandResult<JsonElement?> result = await GraphCall.RunAsync(
            graph, new GraphCallOptions("patch", "/groups/00000000-0000-0000-0000-000000000031", false, "{\"description\":\"Finance team\"}", false, false), None);

        Assert.Equal(ExitCode.RefusedWithoutYes, result.ExitCode);
        Assert.Equal("PATCH", result.Data!.Value.GetProperty("method").GetString());
        Assert.Equal("https://graph.microsoft.com/v1.0/groups/00000000-0000-0000-0000-000000000031", result.Data!.Value.GetProperty("url").GetString());
        Assert.Equal("Finance team", result.Data!.Value.GetProperty("body").GetProperty("description").GetString());
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task AWriteWithYesIsSentAndAnEmptyAnswerIsNull()
    {
        var transport = new ScriptedTransport(_ => new GraphResponse(204, [], ""));
        var graph = new GraphClient(transport);

        CommandResult<JsonElement?> result = await GraphCall.RunAsync(
            graph, new GraphCallOptions("DELETE", "/groups/00000000-0000-0000-0000-000000000031/owners/00000000-0000-0000-0000-000000000001/$ref", false, null, true, false), None);

        Assert.True(result.Ok);
        Assert.Equal(JsonValueKind.Null, result.Data!.Value.ValueKind);
        Assert.Equal(HttpMethod.Delete, Assert.Single(transport.Requests).Method);
    }

    [Fact]
    public async Task AllFollowsThePagesIntoOneValue()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        CommandResult<JsonElement?> result = await GraphCall.RunAsync(
            graph, new GraphCallOptions("GET", "/users?$select=id,displayName,userPrincipalName&$top=2", false, null, false, true), None);

        Assert.Equal(3, result.Data!.Value.GetProperty("value").GetArrayLength());
        Assert.False(result.Data!.Value.TryGetProperty("@odata.nextLink", out _));
        Assert.Equal(2, graph.RequestLog.Ids.Count);
    }

    [Fact]
    public async Task WithoutAllTheNextLinkStays()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        CommandResult<JsonElement?> result = await GraphCall.RunAsync(
            graph, new GraphCallOptions("GET", "/users?$select=id,displayName,userPrincipalName&$top=2", false, null, false, false), None);

        Assert.Equal(2, result.Data!.Value.GetProperty("value").GetArrayLength());
        Assert.True(result.Data!.Value.TryGetProperty("@odata.nextLink", out _));
    }
}
