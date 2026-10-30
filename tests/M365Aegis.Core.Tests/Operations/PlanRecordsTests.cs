using M365Aegis.Core.Graph;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Operations;

/// <summary>
/// The plan records read back (task 060) and a resume from one (task 062), over the three records of
/// <see cref="PlanRecordSamples"/> in a temporary home and the recorded Graph answers.
/// </summary>
public sealed class PlanRecordsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string HomeWithThreeRecords()
    {
        string home = PlanSamples.TempHome();
        PlanRecordSamples.Write(home);
        return home;
    }

    [Fact]
    public void TheListIsNewestFirstWithTheCountsOfEachApply()
    {
        string home = HomeWithThreeRecords();
        File.WriteAllText(Path.Combine(home, "plans", "notes.json"), "not a record");

        PlanRecordList list = PlanStore.List(home);

        Assert.Equal([PlanRecordSamples.Stopped, PlanRecordSamples.Grants, PlanRecordSamples.Owners], list.Records.Select(record => record.Name));
        PlanRecordSummary stopped = list.Records[0];
        Assert.Equal(("group-members-add", PlanMode.Applied, 6, 1, 2, 3), (stopped.Operation, stopped.Mode, stopped.Changes, stopped.Applied, stopped.Failed, stopped.Skipped));
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 11, 0, 0, TimeSpan.Zero), stopped.Started);
        Assert.Equal((3, 2, 1, 0), (list.Records[1].Changes, list.Records[1].Applied, list.Records[1].Failed, list.Records[1].Skipped));
        Assert.Equal(1, list.Unreadable);
        Assert.Equal(Path.Combine(home, "plans"), list.Folder);
    }

    [Fact]
    public void AHomeWithoutRecordsListsNone()
    {
        PlanRecordList list = PlanStore.List(PlanSamples.TempHome());

        Assert.Empty(list.Records);
        Assert.Equal(0, list.Unreadable);
    }

    [Theory]
    [InlineData(PlanRecordSamples.Grants)]
    [InlineData("20261029T100000.000Z-mailbox-access-grant")]
    public void ARecordOpensByItsNameWithOrWithoutTheExtension(string name)
    {
        string home = HomeWithThreeRecords();

        (string path, PlanRecord record) = PlanStore.Open(home, name);

        Assert.Equal(Path.Combine(home, "plans", PlanRecordSamples.Grants), path);
        Assert.Equal("mailbox-access-grant", record.Plan.Operation);
        Assert.Equal("mailbox-grants.csv", record.Input!.File);
        Assert.Equal(PlanStore.Open(home, path).Record.Plan.Title, record.Plan.Title);
    }

    [Fact]
    public void AMissingRecordIsNotFoundAndAForeignFileIsAUsageError()
    {
        string home = HomeWithThreeRecords();
        File.WriteAllText(Path.Combine(home, "plans", "notes.json"), "{\"hello\":1}");

        AegisException missing = Assert.Throws<AegisException>(() => PlanStore.Open(home, "20200101T000000.000Z-nothing"));
        AegisException foreign = Assert.Throws<AegisException>(() => PlanStore.Open(home, "notes"));

        Assert.Equal(ExitCode.NotFound, missing.Failure.ExitCode);
        Assert.StartsWith("no plan record '20200101T000000.000Z-nothing' under ", missing.Failure.Message, StringComparison.Ordinal);
        Assert.Equal(ExitCode.Usage, foreign.Failure.ExitCode);
        Assert.Equal("'notes.json' is not a plan record", foreign.Failure.Message);
    }

    [Fact]
    public async Task AResumePlansOnlyTheFailedAndSkippedChangesAndFindsTheOnesDoneInTheMeantime()
    {
        string home = HomeWithThreeRecords();
        (GraphClient graph, FixtureGraphTransport transport) = GraphFixtures.Client();
        PlanResume resume = PlanResume.Open(home, PlanRecordSamples.Stopped);
        WritePlan fresh = await GroupWrites.PlanAsync(
            graph, GroupRelation.Members, add: true, GroupWrites.FromArguments("Finance", PlanRecordSamples.StoppedArguments[5..11]), Ct);

        WritePlan resumed = resume.Narrow(fresh);
        CommandResult<Plan> result = await PlanGuard.RunAsync(resumed, WriteMode.Apply, PlanSamples.Store(home), Ct);

        Assert.Equal(5, resume.Pending.Count);
        Assert.Equal(["Dave Example (dave@contoso.com) as a member", "Eve Example (eve@contoso.com) as a member"], resumed.Plan.Changes.Select(change => change.Detail));
        // Bob, Carol and Finance EU are members now: a change the record left to do that is done comes out unchanged;
        // Alice, applied by the record, is left out.
        Assert.Equal(
            ["Bob Example (bob@contoso.com) as a member", "Carol Example (carol@contoso.com) as a member", "Finance EU (finance-eu@contoso.com) as a member"],
            resumed.Plan.Unchanged.Select(unchanged => unchanged.Detail));
        Assert.Empty(resumed.Plan.Warnings);
        Assert.Equal(Path.Combine(home, "plans", PlanRecordSamples.Stopped), resumed.Plan.Resumes);

        Assert.Equal(ExitCode.Partial, result.ExitCode);
        Assert.Equal(2, transport.BatchItems.Count(request => request.Method == HttpMethod.Post));
        PlanRecord written = PlanStore.Read(result.Data!.Record!)!;
        Assert.Equal(resumed.Plan.Resumes, written.Plan.Resumes);
    }

    [Fact]
    public void ACallRecordAndARecordWithEveryChangeAppliedAreNotResumed()
    {
        string home = HomeWithThreeRecords();
        var store = new PlanStore(home, new PlanRecordContext("command line", null, ["call", "POST", "/groups", "--yes"]));
        Plan call = new("call", "POST /groups", [new PlanChange(PlanAction.Send, "/groups", "POST", "-", "-") { Result = new ChangeResult(ChangeStatus.Failed, new EnvelopeError("forbidden", "no", null)) }], [], [])
        {
            Mode = PlanMode.Applied,
            Started = PlanSamples.Now,
        };
        string callRecord = store.Begin(call);

        AegisException raw = Assert.Throws<AegisException>(() => PlanResume.Open(home, callRecord));
        AegisException done = Assert.Throws<AegisException>(() => PlanResume.Open(home, PlanRecordSamples.Owners));

        Assert.Equal(ExitCode.Usage, raw.Failure.ExitCode);
        Assert.Contains("raw call", raw.Failure.Message, StringComparison.Ordinal);
        Assert.Equal(ExitCode.Usage, done.Failure.ExitCode);
        Assert.Equal($"every change of '{PlanRecordSamples.Owners}' was applied: nothing to resume", done.Failure.Message);
    }

    [Fact]
    public void ARecordOfAnotherWriteIsRefused()
    {
        PlanResume resume = PlanResume.Open(HomeWithThreeRecords(), PlanRecordSamples.Grants);

        AegisException other = Assert.Throws<AegisException>(() => resume.EnsureOperation("group-members-add"));

        Assert.Equal(ExitCode.Usage, other.Failure.ExitCode);
        Assert.Equal($"'{PlanRecordSamples.Grants}' is a record of mailbox-access-grant, not of group-members-add", other.Failure.Message);
        Assert.Equal($"resume it with the command that wrote it: m365-aegis mailbox access grant --resume {PlanRecordSamples.Grants}", other.Failure.Hint);
        Assert.Equal([3], resume.Pending.Select(change => change.Row!.Value));
    }

    [Fact]
    public void APendingChangeTheNewPlanDoesNotNameIsAWarning()
    {
        PlanResume resume = PlanResume.Open(HomeWithThreeRecords(), PlanRecordSamples.Stopped);
        WritePlan fresh = new PlanBuilder("group-members-add", "Add members to Finance")
            .Change(new PlanChange(PlanAction.Add, "Finance", "Dave Example (dave@contoso.com) as a member", "not a member", "a member"), _ => Task.CompletedTask)
            .Build();

        WritePlan resumed = resume.Narrow(fresh);

        Assert.Single(resumed.Plan.Changes);
        Assert.Equal(4, resumed.Plan.Warnings.Count);
        Assert.Equal(
            $"Finance, Bob Example (bob@contoso.com) as a member: {PlanRecordSamples.Stopped} left it failed, and the new plan does not name it; check it with a lookup",
            resumed.Plan.Warnings[0]);
    }
}
