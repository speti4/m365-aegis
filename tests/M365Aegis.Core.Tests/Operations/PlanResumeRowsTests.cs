using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Operations;

/// <summary>
/// A resume of a record of version 2 (task 065): it plans only the rows of the changes the record left to do (a
/// hundred users added to Finance, sixty failed, resume with sixty reads, counted over the recorded Graph answers and a
/// transport that answers the users' reads), it matches a pending change by its object ids (a group and a user renamed
/// since the apply), and a record of version 1 still plans every row and matches by the text.
/// </summary>
public sealed class PlanResumeRowsTests
{
    private const string Finance = "00000000-0000-0000-0000-000000000031";
    private const string Dave = "00000000-0000-0000-0000-000000000005";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A made-up user's object id, …060 to …0c3.</summary>
    private static string User(int index) => string.Format(CultureInfo.InvariantCulture, "00000000-0000-0000-0000-0000000000{0:x2}", 0x60 + index);

    [Fact]
    public async Task SixtyPendingChangesOfAHundredResumeWithSixtyReads()
    {
        string home = PlanSamples.TempHome();
        List<string> users = Enumerable.Range(0, 100).Select(User).ToList();
        IReadOnlyList<GroupChangeRow> rows = GroupWrites.FromArguments("Finance", users);
        // The first forty applied, the other sixty failed.
        List<PlanChange> changes = rows.Select((row, index) => new PlanChange(PlanAction.Add, "Finance", $"User {index} ({row.User}) as a member", "not a member", "a member")
        {
            Index = row.Index,
            Key = PlanRows.Key(Finance, row.User),
            Result = new ChangeResult(index < 40 ? ChangeStatus.Applied : ChangeStatus.Failed),
        }).ToList();
        string name = PlanRecordSamples.Record(home, PlanSamples.Now, ["group", "members", "add", "Finance", .. users, "--yes"], null, new Plan("group-members-add", "Add members to Finance", changes, [], []));
        PlanResume resume = PlanResume.Open(home, name);
        var transport = new DirectoryObjectsTransport();

        IReadOnlyList<GroupChangeRow> pending = resume.Select(rows);
        WritePlan fresh = await GroupWrites.PlanAsync(new GraphClient(transport), GroupRelation.Members, add: true, pending, Ct);
        WritePlan narrowed = resume.Narrow(fresh);

        Assert.Equal(Enumerable.Range(41, 60), pending.Select(row => row.Index));
        Assert.Equal(60, transport.Reads.Count);
        Assert.Equal(users[40..], transport.Reads);
        // Twenty to a $batch call: three calls for sixty users, not five for a hundred.
        Assert.Equal(3, transport.Batches);
        Assert.Equal(60, narrowed.Plan.Changes.Count);
        Assert.Equal(Enumerable.Range(41, 60), narrowed.Plan.Changes.Select(change => change.Index!.Value));
        Assert.Empty(narrowed.Plan.Warnings);
    }

    [Fact]
    public async Task ARenamedTargetStillMatchesByItsObjectIds()
    {
        string home = PlanSamples.TempHome();
        string name = Renamed(home);
        PlanResume resume = PlanResume.Open(home, name);
        (GraphClient graph, _) = GraphFixtures.Client();

        WritePlan fresh = await GroupWrites.PlanAsync(graph, GroupRelation.Members, add: true, resume.Select(GroupWrites.FromArguments("Finance", ["dave@contoso.com"])), Ct);
        WritePlan narrowed = resume.Narrow(fresh);

        PlanChange change = Assert.Single(narrowed.Plan.Changes);
        Assert.Equal(("Finance", "Dave Example (dave@contoso.com) as a member", $"{Finance}/{Dave}"), (change.Target, change.Detail, change.Key));
        Assert.Empty(narrowed.Plan.Warnings);
    }

    [Fact]
    public async Task ARecordOfVersionOnePlansEveryRowAndMatchesByTheText()
    {
        string home = PlanSamples.TempHome();
        string name = Renamed(home);
        PlanRecordSamples.AsVersionOne(Path.Combine(home, "plans", name));
        PlanResume resume = PlanResume.Open(home, name);
        (GraphClient graph, _) = GraphFixtures.Client();
        IReadOnlyList<GroupChangeRow> rows = GroupWrites.FromArguments("Finance", ["dave@contoso.com", "bob@contoso.com"]);

        IReadOnlyList<GroupChangeRow> selected = resume.Select(rows);
        WritePlan narrowed = resume.Narrow(await GroupWrites.PlanAsync(graph, GroupRelation.Members, add: true, selected, Ct));

        Assert.Equal(1, resume.Original.Version);
        Assert.Same(rows, selected);
        Assert.Null(resume.Pending[0].Key);
        // The text of the old names no longer matches: the pending change is a warning, as before task 065.
        Assert.Empty(narrowed.Plan.Changes);
        Assert.Contains(narrowed.Plan.Warnings, warning => warning.Contains("the new plan does not name it", StringComparison.Ordinal));
    }

    [Fact]
    public void AVersionTwoRecordWithoutTheRowsPlacesStillPlansEveryRow()
    {
        string home = PlanSamples.TempHome();
        PlanRecordSamples.Write(home);
        PlanResume resume = PlanResume.Open(home, PlanRecordSamples.Stopped);
        IReadOnlyList<GroupChangeRow> rows = GroupWrites.FromArguments("Finance", ["alice@contoso.com", "bob@contoso.com"]);

        Assert.Equal(PlanStore.RecordVersion, resume.Original.Version);
        Assert.Same(rows, resume.Select(rows));
    }

    [Fact]
    public void TheRowsAreNumberedInTheirOrder()
    {
        Assert.Equal([1, 2, 3], GroupWrites.FromArguments("Finance", ["a@contoso.com", "b@contoso.com", "c@contoso.com"]).Select(row => row.Index));
        Assert.Equal([1, 2, 3], GroupWrites.FromCsv(PlanSamples.Csv("group-members-add.csv")).Select(row => row.Index));
    }

    /// <summary>
    /// A record of Dave's failed addition to Finance under the names of the day it was applied: the group was "Finance
    /// (old)" and Dave "Dave Old"; the change keeps their object ids.
    /// </summary>
    private static string Renamed(string home) => PlanRecordSamples.Record(
        home,
        PlanSamples.Now,
        ["group", "members", "add", "Finance", "dave@contoso.com", "bob@contoso.com", "--yes"],
        null,
        new Plan(
            "group-members-add",
            "Add members to Finance (old)",
            [
                new PlanChange(PlanAction.Add, "Finance (old)", "Dave Old (dave@contoso.com) as a member", "not a member", "a member")
                {
                    Index = 1,
                    Key = $"{Finance}/{Dave}",
                    Result = new ChangeResult(ChangeStatus.Failed, new EnvelopeError("throttled", "Microsoft Graph answered 429", null)),
                },
            ],
            [new PlanUnchanged("Finance (old)", "Bob Example (bob@contoso.com) as a member", "a member already") { Index = 2 }],
            []));

    /// <summary>
    /// The recorded Graph answers, and the reads of users by object id inside a <c>$batch</c> call answered here (each a
    /// made-up user of the id it was asked for), counted.
    /// </summary>
    private sealed class DirectoryObjectsTransport : IGraphTransport
    {
        private readonly FixtureGraphTransport _fixtures = new(GraphFixtures.Root);

        /// <summary>The object ids read, in order.</summary>
        internal List<string> Reads { get; } = [];

        /// <summary>How many <c>$batch</c> calls read users.</summary>
        internal int Batches { get; private set; }

        public async Task<GraphResponse> SendAsync(GraphHttpRequest request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Post || !request.Uri.AbsolutePath.EndsWith("/$batch", StringComparison.Ordinal))
            {
                return await _fixtures.SendAsync(request, cancellationToken);
            }

            Batches++;
            var responses = new JsonArray();
            foreach (JsonNode? item in JsonNode.Parse(request.Body!)!["requests"]!.AsArray())
            {
                string url = item!["url"]!.GetValue<string>();
                string id = url[(url.LastIndexOf('/') + 1)..];
                Reads.Add(id);
                responses.Add(new JsonObject
                {
                    ["id"] = item["id"]!.GetValue<string>(),
                    ["status"] = 200,
                    ["body"] = new JsonObject
                    {
                        ["@odata.type"] = "#microsoft.graph.user",
                        ["id"] = id,
                        ["displayName"] = $"User {id[^2..]}",
                        ["userPrincipalName"] = $"user{id[^2..]}@contoso.com",
                    },
                });
            }

            return new GraphResponse(200, [], new JsonObject { ["responses"] = responses }.ToJsonString(new JsonSerializerOptions()));
        }
    }
}
