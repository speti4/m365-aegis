using System.Text.Json;
using System.Text.Json.Nodes;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Core.Tests.Config;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Graph;

/// <summary>
/// The Graph client, the raw call and the fixture transport at their edges: empty and broken answers, a batch
/// throttled item by item, absolute URLs, the error sentence of every shape of error body.
/// </summary>
public sealed class GraphEdgeTests
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders = new Dictionary<string, string>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnEmptyAnswerIsAnEmptyCollection()
    {
        var graph = new GraphClient(new ScriptedTransport(_ => new GraphResponse(200, [], null!)));

        IReadOnlyList<JsonElement> items = await graph.ListAsync<JsonElement>(new GraphRequest(HttpMethod.Get, "/groups"), Ct);

        Assert.Empty(items);
    }

    [Fact]
    public async Task AnAnswerThatIsNotJsonIsAServiceErrorNamingThePath()
    {
        var graph = new GraphClient(new ScriptedTransport(_ => new GraphResponse(200, [], "not json")));

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() =>
            graph.GetAsync<JsonElement>(new GraphRequest(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me?$select=id"), Ct));

        Assert.Equal(FailureKind.ServiceError, exception.Failure.Kind);
        Assert.Contains("GET /me is not the JSON", exception.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyBatchSendsNothing()
    {
        var transport = new ScriptedTransport(_ => throw new InvalidOperationException("nothing is sent"));

        Assert.Empty(await new GraphClient(transport).BatchAsync([], Ct));
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task ABatchThrottledItemByItemWaitsTheLongestRetryAfterOnce()
    {
        var delays = new List<TimeSpan>();
        int calls = 0;
        var transport = new ScriptedTransport(request =>
        {
            calls++;
            JsonArray requests = JsonNode.Parse(request.Body!)!["requests"]!.AsArray();
            var responses = new JsonArray();
            foreach (JsonNode? item in requests)
            {
                string id = item!["id"]!.GetValue<string>();
                responses.Add(calls == 1
                    ? new JsonObject { ["id"] = id, ["status"] = 429, ["headers"] = new JsonObject { ["Retry-After"] = id switch { "1" => "2", "2" => "1", _ => "3" } } }
                    : new JsonObject { ["id"] = id, ["status"] = 200, ["body"] = new JsonObject { ["id"] = id } });
            }

            return new GraphResponse(200, [], new JsonObject { ["responses"] = responses }.ToJsonString());
        });
        var graph = new GraphClient(transport, options: new GraphClientOptions { Delay = (delay, _) => { delays.Add(delay); return Task.CompletedTask; }, Jitter = () => 0 });
        GraphRequest[] requests = [.. Enumerable.Range(1, 3).Select(index => new GraphRequest(HttpMethod.Get, $"/groups/{index}"))];

        IReadOnlyList<GraphBatchResponse> answers = await graph.BatchAsync(requests, Ct);

        Assert.All(answers, answer => Assert.True(answer.IsSuccess));
        Assert.Equal([TimeSpan.FromSeconds(3)], delays);
        Assert.Equal(TimeSpan.FromSeconds(3), graph.Waited);
    }

    [Fact]
    public async Task ABatchAnsweredWithAnEmptyBodyHasNoAnswerForItsRequests()
    {
        var graph = new GraphClient(new ScriptedTransport(_ => new GraphResponse(200, [], "")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => graph.BatchAsync([new GraphRequest(HttpMethod.Get, "/me")], Ct));
    }

    [Fact]
    public async Task AnAbsoluteUrlInABatchGoesWithoutTheServiceRoot()
    {
        var transport = new ScriptedTransport(_ => new GraphResponse(200, [], """{"responses":[{"id":"1","status":200}]}"""));
        var graph = new GraphClient(transport);

        IReadOnlyList<GraphBatchResponse> answers = await graph.BatchAsync([new GraphRequest(HttpMethod.Get, "https://graph.microsoft.com/v1.0/users/x")], Ct);

        Assert.Null(Assert.Single(answers).Body);
        Assert.Equal("/users/x", JsonNode.Parse(transport.Requests[0].Body!)!["requests"]![0]!["url"]!.GetValue<string>());
        await Assert.ThrowsAsync<ArgumentException>(() => graph.BatchAsync([new GraphRequest(HttpMethod.Get, "https://graph.microsoft.com/beta/users/x")], Ct));
    }

    [Fact]
    public void ARetryAfterThatIsNoNumberOrDateIsNoneAndAPastDateIsNoWait()
    {
        DateTimeOffset now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

        Assert.Null(GraphClient.RetryAfter(new Dictionary<string, string> { ["Retry-After"] = "soon" }, now));
        Assert.Equal(TimeSpan.Zero, GraphClient.RetryAfter(new Dictionary<string, string> { ["Retry-After"] = "Thu, 29 Oct 2026 12:00:00 GMT" }, now));
    }

    [Theory]
    [InlineData("", null, null)]
    [InlineData("not json", null, null)]
    [InlineData("null", null, null)]
    [InlineData("{}", null, null)]
    [InlineData("""{"error":{"code":"Request_BadRequest","message":"Invalid value."}}""", "Request_BadRequest", "Invalid value.")]
    public void AnErrorBodyGivesItsCodeAndMessageWhenItHasThem(string body, string? code, string? message)
    {
        Assert.Equal((code, message), GraphClient.ReadError(body));
    }

    [Theory]
    [InlineData(null, null, "Microsoft Graph answered 400 to GET /me")]
    [InlineData("", "Invalid value.", "Microsoft Graph answered 400 to GET /me: Invalid value.")]
    [InlineData("Request_BadRequest", "", "Microsoft Graph answered 400 to GET /me: Request_BadRequest")]
    [InlineData("Request_BadRequest", "Invalid value.", "Microsoft Graph answered 400 to GET /me: Request_BadRequest: Invalid value.")]
    public void TheFailureSentenceCarriesWhatTheErrorBodyHas(string? code, string? message, string sentence)
    {
        // A URL right under the service root, without a version, keeps its one segment.
        AegisException failure = GraphClient.Failure(new GraphRequest(HttpMethod.Get, "https://graph.microsoft.com/me"), 400, NoHeaders, code, message);

        Assert.Equal(sentence, failure.Failure.Message);
        Assert.Equal(400, failure.HttpStatus);
    }

    [Fact]
    public void ABatchAnswerWithoutABodyHasNone()
    {
        var request = new GraphRequest(HttpMethod.Get, "/groups/x");
        using JsonDocument jsonNull = JsonDocument.Parse("null");

        var missing = new GraphBatchResponse(0, request, 404, NoHeaders, default(JsonElement));
        var empty = new GraphBatchResponse(0, request, 100, NoHeaders, jsonNull.RootElement);

        Assert.Null(missing.Body);
        Assert.Null(empty.Body);
        Assert.False(missing.IsSuccess);
        Assert.False(empty.IsSuccess);
        Assert.Null(missing.Deserialize<GraphPage<JsonElement>>());
        AegisException exception = Assert.Throws<AegisException>(missing.EnsureSuccess);
        Assert.Equal(FailureKind.NotFound, exception.Failure.Kind);
    }

    [Fact]
    public void AResponseWithoutABodyHasAnEmptyOneAndAStatusUnder200IsNoSuccess()
    {
        var response = new GraphResponse(100, [], null!);

        Assert.Equal("", response.Body);
        Assert.False(response.IsSuccess);
    }

    [Fact]
    public void AVersionOutsideTheEnumHasNoSegmentAndAQueryWithoutValuesAddsNothing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((GraphVersion)9).Segment());
        Assert.Equal("/groups", GraphQuery.Path("/groups", ("$filter", null)));
        Assert.Equal("/groups?$top=5&$select=id", GraphQuery.Path("/groups?$top=5", ("$select", "id")));
    }

    [Fact]
    public async Task AWriteOfCallWithoutItsStoreGoesThroughItsPlan()
    {
        var graph = new GraphClient(new ScriptedTransport(_ => new GraphResponse(204, [], "")));
        var options = new GraphCallOptions("POST", "/groups", false, "{}", Yes: true, All: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => GraphCall.RunAsync(graph, options, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => GraphCall.RunAsync(graph, options with { Yes = false }, new PlanStore(ProfileStoreTests.TempHome(), new PlanRecordContext("call", "work", [])), Ct));
    }

    [Fact]
    public void AnAbsoluteUrlOfV10WithBetaIsAUsageError()
    {
        AegisException exception = Assert.Throws<AegisException>(() =>
            GraphCall.Describe(new GraphCallOptions("GET", "https://graph.microsoft.com/v1.0/me", true, null, false, false)));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
        Assert.Equal("https://graph.microsoft.com/beta/me", GraphCall.Describe(new GraphCallOptions("GET", "https://graph.microsoft.com/beta/me", true, null, false, false)).Path);
        Assert.Equal("https://graph.microsoft.com/v1.0/me", GraphCall.Describe(new GraphCallOptions("GET", "https://graph.microsoft.com/v1.0/me", false, null, false, false)).Path);
    }

    [Fact]
    public void TheBodyIsReadFromTheFileAfterTheAt()
    {
        string folder = ProfileStoreTests.TempHome();
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "body.json");
        File.WriteAllText(file, """{ "displayName": "Finance" }""");

        GraphRequest request = GraphCall.Describe(new GraphCallOptions("PATCH", "/groups/x", false, "@" + file, false, false));
        AegisException missing = Assert.Throws<AegisException>(() =>
            GraphCall.Describe(new GraphCallOptions("PATCH", "/groups/x", false, "@" + Path.Combine(folder, "none.json"), false, false)));

        Assert.Equal("""{ "displayName": "Finance" }""", request.Body);
        Assert.Equal(FailureKind.Usage, missing.Failure.Kind);
    }

    [Fact]
    public async Task AnAnswerThatIsNotJsonIsItsTextAsAString()
    {
        var graph = new GraphClient(new ScriptedTransport(_ => new GraphResponse(200, [], "plain text")));

        CommandResult<JsonElement?> result = await GraphCall.RunAsync(graph, new GraphCallOptions("GET", "/groups/$count", false, null, false, false), Ct);

        Assert.Equal("plain text", result.Data!.Value.GetString());
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("""{"id":"x"}""")]
    public async Task AllOverAnAnswerWithoutAValueListIsTheAnswer(string body)
    {
        var graph = new GraphClient(new ScriptedTransport(_ => new GraphResponse(200, [], body)));

        CommandResult<JsonElement?> result = await GraphCall.RunAsync(graph, new GraphCallOptions("GET", "/me", false, null, false, All: true), Ct);

        Assert.Equal(JsonNode.Parse(body)!.ToJsonString(), result.Data!.Value.GetRawText());
    }

    [Fact]
    public async Task AllFollowsEveryNextLink()
    {
        var graph = new GraphClient(new ScriptedTransport(request => new GraphResponse(200, [], request.Uri.Query switch
        {
            "?page=2" => """{"value":[2],"@odata.nextLink":"https://graph.microsoft.com/v1.0/groups?page=3"}""",
            "?page=3" => """{"value":[3]}""",
            _ => """{"value":[1],"@odata.nextLink":"https://graph.microsoft.com/v1.0/groups?page=2"}""",
        })));

        CommandResult<JsonElement?> result = await GraphCall.RunAsync(graph, new GraphCallOptions("GET", "/groups", false, null, false, All: true), Ct);

        Assert.Equal("""{"value":[1,2,3]}""", result.Data!.Value.GetRawText());
    }

    [Fact]
    public void TheFixtureNameOfAnAdminApiCallWithoutAReadableBodyIsItsPath()
    {
        var uri = new Uri("https://outlook.office365.com/adminapi/beta/00000000-0000-0000-0000-000000000001/InvokeCommand");

        Assert.Equal("outlook.office365.com/adminapi/beta/00000000-0000-0000-0000-000000000001/InvokeCommand", FixtureGraphTransport.RelativePath(uri, ""));
        Assert.Equal(FixtureGraphTransport.RelativePath(uri, ""), FixtureGraphTransport.RelativePath(uri, "not json"));
    }

    [Fact]
    public void AReferenceBodyThatIsNotJsonNamesNoObjectAndAControlCharacterIsEscaped()
    {
        var uri = new Uri("https://graph.microsoft.com/v1.0/groups/x/members/$ref");

        Assert.Equal("graph.microsoft.com/v1.0/groups/x/members/$ref", FixtureGraphTransport.RelativePath(uri, "{\"@odata.id\": broken"));
        Assert.Equal("graph.microsoft.com/v1.0/a%01b", FixtureGraphTransport.RelativePath(new Uri("https://graph.microsoft.com/v1.0/a%01b")));
    }

    [Fact]
    public async Task AMissingFixtureNamesTheFileItWouldBe()
    {
        var transport = new FixtureGraphTransport(GraphFixtures.Root);
        string query = new('x', 200);

        GraphResponse plain = await transport.SendAsync(Get("https://graph.microsoft.com/v1.0/nothing-recorded"), Ct);
        GraphResponse longer = await transport.SendAsync(Get($"https://graph.microsoft.com/v1.0/nothing-recorded?$filter={query}"), Ct);

        Assert.Equal(404, plain.StatusCode);
        Assert.Contains("graph.microsoft.com/v1.0/nothing-recorded.json", plain.Body, StringComparison.Ordinal);
        Assert.Equal(404, longer.StatusCode);
        Assert.Contains("~", longer.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABatchWithoutItsOwnFixtureIsAnsweredItemByItemWithTheDefaultsOfMissingFields()
    {
        string root = ProfileStoreTests.TempHome();
        Directory.CreateDirectory(Path.Combine(root, "graph.microsoft.com", "v1.0"));
        File.WriteAllText(Path.Combine(root, "graph.microsoft.com", "v1.0", "me.json"), """{"id":"00000000-0000-0000-0000-000000000001"}""");
        var transport = new FixtureGraphTransport(root);
        string body = """
            {"requests":[
              {"id":"1","method":null,"url":null,"headers":{"X-Test":null}},
              {"id":"2","method":"GET","url":"/me"}]}
            """;

        GraphResponse response = await transport.SendAsync(
            new GraphHttpRequest(HttpMethod.Post, new Uri("https://graph.microsoft.com/v1.0/$batch"), NoHeaders, body), Ct);

        using JsonDocument document = JsonDocument.Parse(response.Body);
        JsonElement[] answers = [.. document.RootElement.GetProperty("responses").EnumerateArray()];
        Assert.Equal(404, answers[0].GetProperty("status").GetInt32());
        Assert.Equal(200, answers[1].GetProperty("status").GetInt32());
        Assert.Equal(HttpMethod.Get, transport.BatchItems[0].Method);
        Assert.Equal("", transport.BatchItems[0].Headers["X-Test"]);
    }

    private static GraphHttpRequest Get(string url) => new(HttpMethod.Get, new Uri(url), NoHeaders, null);
}
