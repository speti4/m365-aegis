using System.Globalization;
using System.Text.Json;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Groups;

/// <summary>
/// The group writes' <c>$ref</c> requests twenty to a <c>$batch</c> call (task 058): a plan of a hundred changes over
/// the recorded batch answers of tests/fixtures/batches (a full batch, a batch with one failed part), and the results
/// of a batch the same as a single request's over the recorded answers of tests/fixtures/graph.
/// </summary>
public sealed class GroupWritesBatchTests
{
    private const string Finance = "00000000-0000-0000-0000-000000000031";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AHundredChangesApplyInFiveRequestsAndAFailedPartFailsItsChangeAlone()
    {
        var transport = new RecordedBatchTransport("ref-writes-twenty.json", "ref-writes-twenty.json", "ref-writes-twenty.json", "ref-writes-twenty.json", "ref-writes-one-failed.json");
        var graph = new GraphClient(transport);
        PlanBatch<GraphRequest> batch = GroupWrites.RefBatch(graph);
        var builder = new PlanBuilder("group-members-remove", "Remove members from Finance");
        for (int index = 0; index < 100; index++)
        {
            string member = string.Format(CultureInfo.InvariantCulture, "00000000-0000-0000-0000-0000000000{0:x2}", index);
            builder.Change(
                new PlanChange(PlanAction.Remove, "Finance", $"member {index} as a member", "a member", "not a member") { Row = index + 2 },
                PlanStep.Batched(batch, new GraphRequest(HttpMethod.Delete, $"/groups/{Finance}/members/{member}/$ref") { Permission = GroupWrites.MemberWritePermission }));
        }

        CommandResult<Plan> result = await PlanGuard.RunAsync(builder.Build(), WriteMode.Apply, PlanSamples.Store(PlanSamples.TempHome()), Ct);

        Assert.Equal(5, transport.Requests.Count);
        Assert.All(transport.Requests, request => Assert.EndsWith("/v1.0/$batch", request.Uri.AbsoluteUri, StringComparison.Ordinal));
        // The guard sends one call per twenty changes, and each call numbers its own items.
        Assert.Equal(5, transport.Batches.Count);
        Assert.All(transport.Batches, ids => Assert.Equal(Enumerable.Range(1, 20).Select(id => id.ToString(CultureInfo.InvariantCulture)), ids));
        Assert.Equal(5, graph.RequestLog.Ids.Count);

        Assert.Equal(ExitCode.Partial, result.ExitCode);
        Assert.Equal("99 of 100 change(s) applied, 1 failed", result.Failure!.Message);
        Plan applied = result.Data!;
        int failed = Assert.Single(Enumerable.Range(0, 100), index => applied.Changes[index].Result!.Status != ChangeStatus.Applied);
        Assert.Equal(86, failed);
        EnvelopeError error = applied.Changes[failed].Result!.Error!;
        Assert.Equal("forbidden", error.Code);
        Assert.Equal(
            $"Microsoft Graph answered 403 to DELETE /groups/{Finance}/members/00000000-0000-0000-0000-000000000056/$ref: Authorization_RequestDenied: Insufficient privileges to complete the operation.",
            error.Message);
        Assert.Contains(GroupWrites.MemberWritePermission, error.Hint, StringComparison.Ordinal);
        Assert.Equal(100, PlanStore.Read(applied.Record!)!.Plan.Changes.Count(change => change.Result is not null));
    }

    [Fact]
    public async Task ABatchedChangeHasTheResultASingleRequestWouldHave()
    {
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();
        WritePlan write = await GroupWrites.PlanAsync(graph, GroupRelation.Members, add: true, GroupWrites.FromCsv(PlanSamples.Csv("group-members-add.csv")), Ct);

        CommandResult<Plan> result = await PlanGuard.RunAsync(write, WriteMode.Apply, PlanSamples.Store(PlanSamples.TempHome()), Ct);

        // The resolution of the users is one $batch call, the apply of both changes one more.
        Assert.Equal(2, transport.Requests.Count(request => request.Uri.AbsolutePath.EndsWith("/$batch", StringComparison.Ordinal)));
        Assert.Equal(2, transport.BatchItems.Count(request => request.Method == HttpMethod.Post));
        Assert.DoesNotContain(transport.Requests, request => request.Uri.AbsolutePath.EndsWith("/$ref", StringComparison.Ordinal));
        Assert.Equal([ChangeStatus.Applied, ChangeStatus.Failed], result.Data!.Changes.Select(change => change.Result!.Status));

        (GraphClient single, _) = GraphFixtures.Client();
        var eve = new GraphRequest(HttpMethod.Post, $"/groups/{Finance}/members/$ref")
        {
            Body = JsonSerializer.Serialize(new Dictionary<string, string> { ["@odata.id"] = "https://graph.microsoft.com/v1.0/directoryObjects/00000000-0000-0000-0000-000000000007" }),
            Permission = GroupWrites.MemberWritePermission,
        };
        AegisException alone = await Assert.ThrowsAsync<AegisException>(() => single.SendAsync(eve, Ct));
        Assert.Equal(alone.Failure.ToError(), result.Data.Changes[1].Result!.Error);
    }
}
