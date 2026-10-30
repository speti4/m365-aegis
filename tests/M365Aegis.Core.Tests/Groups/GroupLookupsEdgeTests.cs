using System.Text.Json.Nodes;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Output;
using M365Aegis.Core.Tests.Graph;

namespace M365Aegis.Core.Tests.Groups;

/// <summary>The group lookups over scripted answers that the recorded ones do not hold: empty, nameless, paged, too many.</summary>
public sealed class GroupLookupsEdgeTests
{
    private const string G1 = "00000000-0000-0000-0000-000000000041";
    private const string G2 = "00000000-0000-0000-0000-000000000042";
    private const string U1 = "00000000-0000-0000-0000-000000000043";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task NoGroupNamedIsAUsageError(string? name)
    {
        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => GroupLookups.ResolveAsync(Answering(_ => ""), name!, Ct));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
    }

    [Theory]
    [InlineData(G1)]
    [InlineData("Nothing")]
    public async Task AnEmptyAnswerIsNoGroup(string name)
    {
        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => GroupLookups.ResolveAsync(Answering(_ => ""), name, Ct));

        Assert.Equal(FailureKind.NotFound, exception.Failure.Kind);
    }

    [Fact]
    public async Task TenGroupsOfTheSameNameMayBeMore()
    {
        var groups = new JsonArray([.. Enumerable.Range(1, 10).Select(index => (JsonNode)new JsonObject
        {
            ["id"] = $"00000000-0000-0000-0000-0000000000{index + 50:D2}",
            ["displayName"] = "Finance",
        })]);
        string page = new JsonObject { ["value"] = groups }.ToJsonString();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => GroupLookups.ResolveAsync(Answering(_ => page), "Finance", Ct));

        Assert.Equal(FailureKind.NotFound, exception.Failure.Kind);
        Assert.Contains("10 groups, and maybe more", exception.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoGroupsHaveNoMembers()
    {
        GraphClient graph = Answering(_ => throw new InvalidOperationException("nothing is sent"));

        Assert.Empty(await GroupLookups.MembersOfAsync(graph, [], Ct));
        Assert.Empty(await GroupLookups.ExpandAsync(graph, [], Ct));
    }

    [Fact]
    public async Task ABatchAnswerWithoutABodyHasNoMembersAndOneWithANextLinkHasTheRest()
    {
        GraphClient graph = Batches(
            url => url.Contains(G1, StringComparison.Ordinal)
                ? new JsonObject { ["id"] = "", ["status"] = 200 }
                : new JsonObject
                {
                    ["status"] = 200,
                    ["body"] = new JsonObject
                    {
                        ["value"] = new JsonArray(Member(U1, "#microsoft.graph.user", "Alice")),
                        ["@odata.nextLink"] = $"https://graph.microsoft.com/v1.0/groups/{G2}/members?page=2",
                    },
                },
            page => new JsonObject { ["value"] = new JsonArray(Member("00000000-0000-0000-0000-000000000044", "#microsoft.graph.user", "Bob")) });

        IReadOnlyDictionary<string, IReadOnlyList<GroupMember>> members = await GroupLookups.MembersOfAsync(graph, [G1, G2], Ct);

        Assert.Empty(members[G1]);
        Assert.Equal(["Alice", "Bob"], members[G2].Select(member => member.DisplayName));
    }

    [Fact]
    public async Task ANestedGroupWithoutANameIsNamedByItsId()
    {
        GraphClient graph = Batches(
            url => new JsonObject
            {
                ["status"] = 200,
                ["body"] = new JsonObject
                {
                    ["value"] = url.StartsWith($"/groups/{G1}/members", StringComparison.Ordinal)
                        ? new JsonArray(Member(G2, "#microsoft.graph.group", null))
                        : url.StartsWith($"/groups/{G1}/transitiveMembers", StringComparison.Ordinal)
                            ? new JsonArray(Member(G2, "#microsoft.graph.group", null), Member(U1, "#microsoft.graph.user", "Alice"))
                            : new JsonArray(Member(U1, "#microsoft.graph.user", "Alice")),
                },
            },
            _ => new JsonObject());

        IReadOnlyDictionary<string, IReadOnlyList<GroupMember>> expanded = await GroupLookups.ExpandAsync(graph, [G1], Ct);

        GroupMember alice = Assert.Single(expanded[G1], member => member.Id == U1);
        Assert.True(alice.Nested);
        Assert.Equal(G2, Assert.Single(alice.Via!).DisplayName);
    }

    [Fact]
    public async Task TheGroupsOfAnUnknownUserAreNotFoundWithTheUserNamed()
    {
        var graph = new GraphClient(new ScriptedTransport(_ => new GraphResponse(404, [], """{"error":{"code":"Request_ResourceNotFound"}}""")));

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => GroupLookups.MemberOfAsync(graph, "nobody@contoso.com", Ct));

        Assert.Equal(FailureKind.NotFound, exception.Failure.Kind);
        Assert.Contains("no user 'nobody@contoso.com'", exception.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AGroupWithoutANameOrTypesAndAnObjectOfAnotherTypeFamily()
    {
        GroupInfo group = new GraphGroup { Id = G1 }.ToInfo();
        GroupMember member = new GraphDirectoryObject { Id = U1, ODataType = "#other.type" }.ToMember();

        Assert.Equal(G1, group.DisplayName);
        Assert.Equal(GroupKind.Distribution, group.Kind);
        Assert.Equal("directoryObject", member.Type);
    }

    private static JsonObject Member(string id, string type, string? name)
    {
        var member = new JsonObject { ["id"] = id, ["@odata.type"] = type };
        if (name is not null)
        {
            member["displayName"] = name;
        }

        return member;
    }

    private static GraphClient Answering(Func<GraphHttpRequest, string> body) =>
        new(new ScriptedTransport(request => new GraphResponse(200, [], body(request))));

    /// <summary>A client whose <c>$batch</c> calls answer each item by its URL, and whose other GETs answer a next page.</summary>
    private static GraphClient Batches(Func<string, JsonObject> item, Func<string, JsonObject> page) =>
        new(new ScriptedTransport(request =>
        {
            if (!request.Uri.AbsolutePath.EndsWith("/$batch", StringComparison.Ordinal))
            {
                return new GraphResponse(200, [], page(request.Uri.ToString()).ToJsonString());
            }

            var responses = new JsonArray();
            foreach (JsonNode? sent in JsonNode.Parse(request.Body!)!["requests"]!.AsArray())
            {
                JsonObject answer = item(sent!["url"]!.GetValue<string>());
                answer["id"] = sent["id"]!.GetValue<string>();
                responses.Add(answer);
            }

            return new GraphResponse(200, [], new JsonObject { ["responses"] = responses }.ToJsonString());
        }));
}
