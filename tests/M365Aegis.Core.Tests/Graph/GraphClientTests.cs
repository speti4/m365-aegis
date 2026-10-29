using System.Text.Json;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Graph;

/// <summary>The Graph client over the recorded answers: the URL, paging, throttling, batch, errors and request ids.</summary>
public sealed class GraphClientTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public void APathGoesUnderTheVersionAndANextLinkStaysAsItIs()
    {
        Assert.Equal("https://graph.microsoft.com/v1.0/me", GraphClient.BuildUri(GraphRequest.Get("/me")).ToString());
        Assert.Equal("https://graph.microsoft.com/beta/me", GraphClient.BuildUri(GraphRequest.Get("/me") with { Version = GraphVersion.Beta }).ToString());
        Uri next = GraphClient.BuildUri(GraphRequest.Get("https://graph.microsoft.com/v1.0/users?$skiptoken=page-2") with { Version = GraphVersion.Beta });
        Assert.Equal("/v1.0/users", next.AbsolutePath);
    }

    [Theory]
    [InlineData("me")]
    [InlineData("https://contoso.com/v1.0/me")]
    [InlineData("http://graph.microsoft.com/v1.0/me")]
    public void APathThatIsNotGraphsIsAUsageError(string path)
    {
        AegisException exception = Assert.Throws<AegisException>(() => GraphClient.BuildUri(GraphRequest.Get(path)));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
    }

    [Fact]
    public void TheQueryValuesAreEscapedAndAStringLiteralDoublesItsQuotes()
    {
        string path = GraphQuery.Path("/groups", ("$filter", $"displayName eq {GraphQuery.Literal("R&D O'Brien")}"), ("$top", null), ("$select", "id"));

        Assert.Equal("/groups?$filter=displayName%20eq%20%27R%26D%20O%27%27Brien%27&$select=id", path);
        Assert.Equal("graph.microsoft.com/v1.0/groups@$filter=displayName eq 'R&D O''Brien'&$select=id", FixtureGraphTransport.RelativePath(GraphClient.BuildUri(GraphRequest.Get(path))));
    }

    [Fact]
    public void AFixtureNameSortsTheQueryAndEscapesWhatAFileNameCannotHold()
    {
        var uri = new Uri("https://graph.microsoft.com/v1.0/users?$search=%22displayName:Alice%22&$count=true");

        Assert.Equal("graph.microsoft.com/v1.0/users@$count=true&$search=%22displayName%3AAlice%22", FixtureGraphTransport.RelativePath(uri));
    }

    [Fact]
    public void ALongFixtureNameHasAShortFormAndAShortOneKeepsItsName()
    {
        var shortUri = new Uri("https://graph.microsoft.com/v1.0/groups?$filter=owners/$count+eq+0");
        var longUri = new Uri("https://graph.microsoft.com/v1.0/users?$filter=" + new string('a', 150));

        Assert.Equal("graph.microsoft.com/v1.0/groups@$filter=owners%2F$count eq 0", FixtureGraphTransport.ShortRelativePath(shortUri));
        string name = FixtureGraphTransport.ShortRelativePath(longUri);
        Assert.StartsWith("graph.microsoft.com/v1.0/users@$filter=aaaa", name, StringComparison.Ordinal);
        Assert.Matches("~[0-9a-f]{8}$", name);
        Assert.True(name.Length < FixtureGraphTransport.RelativePath(longUri).Length);
    }

    [Fact]
    public async Task ATypedRecordKeepsTheFieldsItDoesNotName()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        Person? me = await graph.GetAsync<Person>(GraphRequest.Get("/me"), None);

        Assert.NotNull(me);
        Assert.Equal("Alice Example", me.DisplayName);
        Assert.NotNull(me.AdditionalData);
        Assert.Equal("IT administrator", me.AdditionalData["jobTitle"].GetString());
        Assert.Equal(["00000000-0000-0000-0000-0000000000f1"], graph.RequestLog.Ids);
        // Written back, the unknown fields are still there.
        Assert.Contains("\"jobTitle\":\"IT administrator\"", JsonSerializer.Serialize(me, GraphJson.Options), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheBetaSwitchCallsTheBetaEndpoint()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();

        Person? me = await graph.GetAsync<Person>(GraphRequest.Get("/me") with { Version = GraphVersion.Beta }, None);

        Assert.Equal("Member", me?.AdditionalData?["userType"].GetString());
        Assert.Equal("https://graph.microsoft.com/beta/me", Assert.Single(transport.Requests).Uri.ToString());
    }

    [Fact]
    public async Task PagingFollowsTheNextLinkToTheLastPage()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();
        GraphRequest request = GraphRequest.Get(GraphQuery.Path("/users", ("$select", "id,displayName,userPrincipalName"), ("$top", "2")));

        IReadOnlyList<Person> people = await graph.ListAsync<Person>(request, None);

        Assert.Equal(["alice@contoso.com", "bob@contoso.com", "carol@contoso.com"], people.Select(person => person.UserPrincipalName));
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(["00000000-0000-0000-0000-0000000000f3", "00000000-0000-0000-0000-0000000000f4"], graph.RequestLog.Ids);
    }

    [Fact]
    public async Task AnAdvancedQuerySendsConsistencyLevelEventual()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();
        GraphRequest request = GraphRequest.Get(GraphQuery.Path("/users", ("$search", "\"displayName:Alice\""), ("$count", "true"))) with { EventualConsistency = true };

        GraphPage<Person> page = await graph.GetPagesAsync<Person>(request, None).FirstAsync(None);

        Assert.Equal(1, page.Count);
        Assert.Equal("eventual", Assert.Single(transport.Requests).Headers["ConsistencyLevel"]);
    }

    [Fact]
    public async Task A429IsRetriedAfterItsRetryAfter()
    {
        var delays = new List<TimeSpan>();
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client(delays);

        GraphResponse response = await graph.SendAsync(GraphRequest.Get("/organization"), None);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(2)], delays);
        // Both answers' request ids, the throttled one's included, go into the meta.
        Assert.Equal(["00000000-0000-0000-0000-0000000000f5", "00000000-0000-0000-0000-0000000000f6"], graph.RequestLog.Ids);
    }

    [Fact]
    public async Task A503WithoutRetryAfterBacksOffExponentially()
    {
        var delays = new List<TimeSpan>();
        (GraphClient graph, _) = GraphFixtures.Client(delays);

        GraphResponse response = await graph.SendAsync(GraphRequest.Get("/domains"), None);

        Assert.Equal(200, response.StatusCode);
        // The first back-off is 1 s, halved by the jitter's lower bound (the tests' jitter is 0).
        Assert.Equal([TimeSpan.FromSeconds(0.5)], delays);
    }

    [Fact]
    public async Task ARetryAfterBeyondTheBudgetFailsAsThrottledAtOnce()
    {
        var delays = new List<TimeSpan>();
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client(delays);

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => graph.SendAsync(GraphRequest.Get("/subscribedSkus"), None));

        Assert.Equal(FailureKind.Throttled, exception.Failure.Kind);
        Assert.Equal(ExitCode.Throttled, exception.Failure.ExitCode);
        Assert.Contains("wait 120 s", exception.Failure.Hint, StringComparison.Ordinal);
        Assert.Empty(delays);
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task TheRetriesOfARequestAreCounted()
    {
        var delays = new List<TimeSpan>();
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client(delays, new GraphClientOptions { MaxRetries = 0 });

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => graph.SendAsync(GraphRequest.Get("/organization"), None));

        Assert.Equal(FailureKind.Throttled, exception.Failure.Kind);
        Assert.Single(transport.Requests);
    }

    [Theory]
    [InlineData("/directoryRoles", FailureKind.Forbidden, "Authorization_RequestDenied")]
    [InlineData("/users/00000000-0000-0000-0000-000000000099", FailureKind.NotFound, "Request_ResourceNotFound")]
    [InlineData("/users?$filter=bogus", FailureKind.ServiceError, "BadRequest")]
    public async Task AFailedAnswerIsTheContractsFailure(string path, FailureKind kind, string code)
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => graph.SendAsync(GraphRequest.Get(path), None));

        Assert.Equal(kind, exception.Failure.Kind);
        Assert.Contains(code, exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.Failure.Hint);
        Assert.Single(graph.RequestLog.Ids);
    }

    [Fact]
    public async Task AForbiddenAnswerNamesThePermissionTheRequestNeeds()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() =>
            graph.SendAsync(GraphRequest.Get("/directoryRoles") with { Permission = "RoleManagement.Read.Directory" }, None));

        Assert.Equal(ExitCode.Forbidden, exception.Failure.ExitCode);
        Assert.Contains("RoleManagement.Read.Directory", exception.Failure.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingFixtureNamesTheFile()
    {
        (GraphClient graph, _) = GraphFixtures.Client();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => graph.SendAsync(GraphRequest.Get("/no/such/thing?$top=1"), None));

        Assert.Contains("graph.microsoft.com/v1.0/no/such/thing@$top=1.json", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABatchRetriesTheThrottledItemAndAnswersInTheOrderGiven()
    {
        var delays = new List<TimeSpan>();
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client(delays);
        GraphRequest[] requests =
        [
            GraphRequest.Get("/users/00000000-0000-0000-0000-000000000001"),
            GraphRequest.Get("/users/00000000-0000-0000-0000-000000000099"),
            GraphRequest.Get("/groups/00000000-0000-0000-0000-000000000031") with { EventualConsistency = true },
        ];

        IReadOnlyList<GraphBatchResponse> answers = await graph.BatchAsync(requests, None);

        Assert.Equal([200, 404, 200], answers.Select(answer => answer.StatusCode));
        Assert.Equal("Alice Example", answers[0].Deserialize<Person>()?.DisplayName);
        Assert.Equal("Finance", answers[2].Deserialize<Person>()?.DisplayName);
        Assert.Equal(FailureKind.NotFound, Assert.Throws<AegisException>(answers[1].EnsureSuccess).Failure.Kind);
        Assert.Equal([TimeSpan.FromSeconds(1)], delays);
        Assert.Equal(["00000000-0000-0000-0000-0000000000fc", "00000000-0000-0000-0000-0000000000fd"], graph.RequestLog.Ids);

        // The first round carries the three requests, the second only the throttled one, with its header.
        IReadOnlyList<GraphHttpRequest> sent = transport.Requests;
        Assert.Equal(2, sent.Count);
        Assert.All(sent, request => Assert.Equal(HttpMethod.Post, request.Method));
        using JsonDocument first = JsonDocument.Parse(sent[0].Body!);
        Assert.Equal(["1", "2", "3"], first.RootElement.GetProperty("requests").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
        Assert.Equal("/users/00000000-0000-0000-0000-000000000001", first.RootElement.GetProperty("requests")[0].GetProperty("url").GetString());
        using JsonDocument second = JsonDocument.Parse(sent[1].Body!);
        JsonElement retried = Assert.Single(second.RootElement.GetProperty("requests").EnumerateArray());
        Assert.Equal("3", retried.GetProperty("id").GetString());
        Assert.Equal("eventual", retried.GetProperty("headers").GetProperty("ConsistencyLevel").GetString());
    }

    [Fact]
    public async Task ABatchOfMoreThanTwentyIsSplit()
    {
        var transport = new ScriptedTransport(request =>
        {
            using JsonDocument body = JsonDocument.Parse(request.Body!);
            string[] ids = body.RootElement.GetProperty("requests").EnumerateArray().Select(item => item.GetProperty("id").GetString()!).ToArray();
            string responses = string.Join(",", ids.Select(id => $"{{\"id\":\"{id}\",\"status\":204}}"));
            return new GraphResponse(200, [], $"{{\"responses\":[{responses}]}}");
        });
        var graph = new GraphClient(transport);
        GraphRequest[] requests = Enumerable.Range(1, 45).Select(index => GraphRequest.Get($"/users/{index}")).ToArray();

        IReadOnlyList<GraphBatchResponse> answers = await graph.BatchAsync(requests, None);

        Assert.Equal(45, answers.Count);
        Assert.All(answers, answer => Assert.Equal(204, answer.StatusCode));
        Assert.Equal(3, transport.Requests.Count);
    }

    [Fact]
    public async Task ABatchMixingVersionsIsRefused()
    {
        var graph = new GraphClient(new ScriptedTransport(_ => throw new InvalidOperationException("not sent")));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            graph.BatchAsync([GraphRequest.Get("/me"), GraphRequest.Get("/me") with { Version = GraphVersion.Beta }], None));
    }

    [Fact]
    public void RetryAfterReadsSecondsAndADate()
    {
        var now = new DateTimeOffset(2026, 10, 29, 10, 0, 0, TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(7), GraphClient.RetryAfter(new Dictionary<string, string> { ["Retry-After"] = "7" }, now));
        Assert.Equal(TimeSpan.FromSeconds(30), GraphClient.RetryAfter(new Dictionary<string, string> { ["Retry-After"] = "Thu, 29 Oct 2026 10:00:30 GMT" }, now));
        Assert.Null(GraphClient.RetryAfter(new Dictionary<string, string>(), now));
    }

    /// <summary>A user as the tests read it: two named fields, the rest in the extension data.</summary>
    private sealed record Person : GraphObject
    {
        public string? DisplayName { get; init; }

        public string? UserPrincipalName { get; init; }
    }
}
