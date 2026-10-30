using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Operations;

/// <summary>
/// <c>plan prune</c> and the totals of <c>plan list</c> (task 066), over the three records of
/// <see cref="PlanRecordSamples"/> (started 2026-10-28, 2026-10-29 and 2026-10-30) and a file that is no record, in a
/// temporary home.
/// </summary>
public sealed class PlanPruneTests
{
    /// <summary>A month after the samples: the 28th is a little over 33 days old, the 29th over 32, the 30th over 31.</summary>
    private static readonly DateTimeOffset Later = new(2026, 11, 30, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string HomeWithThreeRecordsAndANote()
    {
        string home = PlanSamples.TempHome();
        PlanRecordSamples.Write(home);
        File.WriteAllText(Path.Combine(home, "plans", "notes.json"), "not a record");
        return home;
    }

    private static string[] Names(string home) => Directory.GetFiles(Path.Combine(home, "plans")).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    [Fact]
    public void TheListCountsTheRecordsAndTheSizeOfTheFolder()
    {
        string home = HomeWithThreeRecordsAndANote();

        PlanRecordList list = PlanStore.List(home);

        Assert.Equal(3, list.Count);
        Assert.Equal(Directory.GetFiles(Path.Combine(home, "plans")).Sum(file => new FileInfo(file).Length), list.Bytes);
        Assert.True(list.Bytes > "not a record".Length);
        Assert.Equal(0, PlanStore.List(PlanSamples.TempHome()).Bytes);
    }

    [Fact]
    public void TheDryRunListsTheOlderRecordsOnlyAndRemovesNothing()
    {
        string home = HomeWithThreeRecordsAndANote();

        WritePlan prune = PlanPrune.Plan(home, 33, Later);

        Assert.Equal(PlanPrune.Operation, prune.Plan.Operation);
        PlanChange change = Assert.Single(prune.Plan.Changes);
        Assert.Equal((PlanAction.Remove, PlanRecordSamples.Owners, "kept", "removed"), (change.Action, change.Target, change.Current, change.Desired));
        Assert.StartsWith("the record of group-owners-add, started 2026-10-28 09:00:00 UTC, 1 change(s), ", change.Detail, StringComparison.Ordinal);
        Assert.Equal("Remove the plan records started before 2026-10-28 12:00:00 UTC (older than 33 day(s)): 1 of 3", prune.Plan.Title);
        // The owner write was applied in full: nothing is lost with it.
        Assert.Empty(prune.Plan.Warnings);
        Assert.Equal(4, Names(home).Length);
    }

    [Fact]
    public async Task TheApplyRemovesTheOlderRecordsKeepsTheNewerOnesAndRecordsItself()
    {
        string home = HomeWithThreeRecordsAndANote();

        CommandResult<Plan> result = await PlanGuard.RunAsync(PlanPrune.Plan(home, 32, Later), WriteMode.Apply, PlanSamples.Store(home), Ct);

        Assert.Equal(ExitCode.Ok, result.ExitCode);
        Assert.Equal([ChangeStatus.Applied, ChangeStatus.Applied], result.Data!.Changes.Select(change => change.Result!.Status));
        // The 28th and the 29th went; the 30th, the note and the prune's own record stay.
        Assert.Equal(["20261030T110000.000Z-group-members-add.json", "20261030T120000.000Z-plan-prune.json", "notes.json"], Names(home));
        // The batch of the 29th had a failed line its resume could have continued.
        Assert.Equal(
            $"{PlanRecordSamples.Grants} left 1 change(s) to do: once it is removed, --resume cannot continue them (`m365-aegis plan show {PlanRecordSamples.Grants}` lists them)",
            Assert.Single(result.Data.Warnings));
    }

    [Fact]
    public async Task NothingOlderIsUpToDateAndWritesNoRecord()
    {
        string home = HomeWithThreeRecordsAndANote();

        CommandResult<Plan> result = await PlanGuard.RunAsync(PlanPrune.Plan(home, 365, Later), WriteMode.Apply, PlanSamples.Store(home), Ct);

        Assert.True(result.Ok);
        Assert.Equal(PlanMode.UpToDate, result.Data!.Mode);
        Assert.Equal(4, Names(home).Length);
        Assert.Equal(PlanMode.UpToDate, (await PlanGuard.RunAsync(PlanPrune.Plan(PlanSamples.TempHome(), 1, Later), WriteMode.Apply, PlanSamples.Store(PlanSamples.TempHome()), Ct)).Data!.Mode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void AnAgeBelowOneDayIsAUsageError(int days)
    {
        AegisException refused = Assert.Throws<AegisException>(() => PlanPrune.Plan(PlanSamples.TempHome(), days, Later));

        Assert.Equal(FailureKind.Usage, refused.Failure.Kind);
    }
}
