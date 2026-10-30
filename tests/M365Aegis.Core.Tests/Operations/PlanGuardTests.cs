using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Operations;

/// <summary>
/// The write guard (task 051) over a made-up plan: a dry run and a refusal send nothing and write no record, an apply
/// runs the steps in order, records the plan with each result, keeps going past a refused change and stops at one no
/// later change could pass; the exit codes follow D-006.
/// </summary>
public sealed class PlanGuardTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADryRunIsThePlanAndSendsNothing()
    {
        var sent = new List<string>();
        string home = PlanSamples.TempHome();

        CommandResult<Plan> result = await PlanGuard.RunAsync(PlanSamples.GroupWrite(sent), WriteMode.DryRun, PlanSamples.Store(home), Ct);

        Assert.Equal(ExitCode.Ok, result.ExitCode);
        Assert.Equal(PlanMode.DryRun, result.Data!.Mode);
        Assert.Equal(2, result.Data.Changes.Count);
        Assert.All(result.Data.Changes, change => Assert.Null(change.Result));
        Assert.Empty(sent);
        Assert.False(Directory.Exists(Path.Combine(home, "plans")));
    }

    [Fact]
    public async Task WithoutYesThePlanIsRefusedWithSeven()
    {
        var sent = new List<string>();
        string home = PlanSamples.TempHome();

        CommandResult<Plan> result = await PlanGuard.RunAsync(PlanSamples.GroupWrite(sent), WriteMode.Refuse, PlanSamples.Store(home), Ct);

        Assert.Equal(ExitCode.RefusedWithoutYes, result.ExitCode);
        Assert.Equal("refused-without-yes", result.Failure!.Code);
        Assert.Contains("--yes", result.Failure.Hint, StringComparison.Ordinal);
        Assert.Equal(PlanMode.Refused, result.Data!.Mode);
        Assert.Empty(sent);
        Assert.False(Directory.Exists(Path.Combine(home, "plans")));
    }

    [Theory]
    [InlineData(WriteMode.DryRun)]
    [InlineData(WriteMode.Refuse)]
    [InlineData(WriteMode.Apply)]
    public async Task APlanWithNothingToChangeIsUpToDateWhateverTheFlags(WriteMode mode)
    {
        string home = PlanSamples.TempHome();
        WritePlan write = new PlanBuilder("group-members-add", "Add members to Finance")
            .Unchanged(new PlanUnchanged("Finance", "Bob Example (bob@contoso.com) as a member", "a member already"))
            .Build();

        CommandResult<Plan> result = await PlanGuard.RunAsync(write, mode, PlanSamples.Store(home), Ct);

        Assert.Equal(ExitCode.Ok, result.ExitCode);
        Assert.Equal(PlanMode.UpToDate, result.Data!.Mode);
        Assert.False(Directory.Exists(Path.Combine(home, "plans")));
    }

    [Fact]
    public async Task AnApplyRunsTheStepsInOrderAndRecordsThePlanWithTheResults()
    {
        var sent = new List<string>();
        string home = PlanSamples.TempHome();

        CommandResult<Plan> result = await PlanGuard.RunAsync(PlanSamples.GroupWrite(sent), WriteMode.Apply, PlanSamples.Store(home), Ct);

        Assert.Equal(ExitCode.Ok, result.ExitCode);
        Assert.Equal(["dave", "eve"], sent);
        Plan applied = result.Data!;
        Assert.Equal(PlanMode.Applied, applied.Mode);
        Assert.All(applied.Changes, change => Assert.Equal(ChangeStatus.Applied, change.Result!.Status));
        Assert.Equal(PlanSamples.Now, applied.Started);
        Assert.Equal(PlanSamples.Now, applied.Finished);
        string file = Assert.Single(Directory.GetFiles(Path.Combine(home, "plans")));
        Assert.Equal("20261030T120000.000Z-group-members-add.json", Path.GetFileName(file));
        Assert.Equal(file, applied.Record);
        PlanRecord record = PlanStore.Read(file)!;
        Assert.Equal(PlanStore.RecordVersion, record.Version);
        Assert.Equal("tests", record.Source);
        Assert.Equal(["group", "members", "add"], record.Arguments);
        Assert.Equal(PlanMode.Applied, record.Plan.Mode);
        Assert.Equal([ChangeStatus.Applied, ChangeStatus.Applied], record.Plan.Changes.Select(change => change.Result!.Status));
        Assert.Single(record.Plan.Unchanged);
    }

    [Fact]
    public async Task TwoAppliesInTheSameMillisecondGetTwoRecords()
    {
        string home = PlanSamples.TempHome();

        await PlanGuard.RunAsync(PlanSamples.GroupWrite([]), WriteMode.Apply, PlanSamples.Store(home), Ct);
        CommandResult<Plan> second = await PlanGuard.RunAsync(PlanSamples.GroupWrite([]), WriteMode.Apply, PlanSamples.Store(home), Ct);

        Assert.Equal(2, Directory.GetFiles(Path.Combine(home, "plans")).Length);
        Assert.EndsWith("-group-members-add-2.json", second.Data!.Record, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusedChangeMakesTheApplyPartialAndTheRestStillRuns()
    {
        var sent = new List<string>();
        string home = PlanSamples.TempHome();
        WritePlan write = new PlanBuilder("group-members-add", "three changes")
            .Change(new PlanChange(PlanAction.Add, "Finance", "first", "not a member", "a member"), _ => Task.FromException(PlanSamples.Forbidden))
            .Change(new PlanChange(PlanAction.Add, "Finance", "second", "not a member", "a member"), _ =>
            {
                sent.Add("second");
                return Task.CompletedTask;
            })
            .Build();

        CommandResult<Plan> result = await PlanGuard.RunAsync(write, WriteMode.Apply, PlanSamples.Store(home), Ct);

        Assert.Equal(ExitCode.Partial, result.ExitCode);
        Assert.Equal("1 of 2 change(s) applied, 1 failed", result.Failure!.Message);
        Assert.Contains(result.Data!.Record!, result.Failure.Hint, StringComparison.Ordinal);
        Assert.Equal(["second"], sent);
        Assert.Equal(ChangeStatus.Failed, result.Data.Changes[0].Result!.Status);
        Assert.Equal("forbidden", result.Data.Changes[0].Result!.Error!.Code);
        Assert.Equal(ChangeStatus.Applied, result.Data.Changes[1].Result!.Status);
        Assert.Equal(ChangeStatus.Failed, PlanStore.Read(result.Data.Record!)!.Plan.Changes[0].Result!.Status);
    }

    [Fact]
    public async Task OneFailedChangeIsItsOwnExitCode()
    {
        WritePlan write = new PlanBuilder("group-owners-add", "one change")
            .Change(new PlanChange(PlanAction.Add, "Finance", "Bob as an owner", "not an owner", "an owner"), _ => Task.FromException(PlanSamples.Forbidden))
            .Build();

        CommandResult<Plan> result = await PlanGuard.RunAsync(write, WriteMode.Apply, PlanSamples.Store(PlanSamples.TempHome()), Ct);

        Assert.Equal(ExitCode.Forbidden, result.ExitCode);
        Assert.Equal(PlanSamples.Forbidden.Failure.Message, result.Failure!.Message);
        Assert.Equal(PlanSamples.Forbidden.Failure.Hint, result.Failure.Hint);
        Assert.Equal(PlanMode.Applied, result.Data!.Mode);
    }

    [Fact]
    public async Task ASignInThatFailsStopsTheApplyAndSkipsTheRest()
    {
        var sent = new List<string>();
        var auth = new AegisException(FailureKind.Auth, "no usable sign-in", "run `m365-aegis login`");

        CommandResult<Plan> result = await PlanGuard.RunAsync(PlanSamples.GroupWrite(sent, second: auth), WriteMode.Apply, PlanSamples.Store(PlanSamples.TempHome()), Ct);

        Assert.Equal(ExitCode.Partial, result.ExitCode);
        Assert.Equal([ChangeStatus.Applied, ChangeStatus.Failed], result.Data!.Changes.Select(change => change.Result!.Status));

        WritePlan three = new PlanBuilder("group-members-add", "three changes")
            .Change(new PlanChange(PlanAction.Add, "Finance", "first", "-", "-"), _ => Task.FromException(auth))
            .Change(new PlanChange(PlanAction.Add, "Finance", "second", "-", "-"), _ =>
            {
                sent.Add("second");
                return Task.CompletedTask;
            })
            .Build();
        CommandResult<Plan> stopped = await PlanGuard.RunAsync(three, WriteMode.Apply, PlanSamples.Store(PlanSamples.TempHome()), Ct);

        Assert.Equal(ExitCode.Auth, stopped.ExitCode);
        Assert.StartsWith("none of the 2 changes was applied, 1 skipped: no usable sign-in", stopped.Failure!.Message, StringComparison.Ordinal);
        Assert.Equal([ChangeStatus.Failed, ChangeStatus.Skipped], stopped.Data!.Changes.Select(change => change.Result!.Status));
        Assert.DoesNotContain("second", sent);
    }

    [Fact]
    public async Task ABugInAStepRecordsTheChangeAsUnknownAndGoesOnToTheRunner()
    {
        string home = PlanSamples.TempHome();
        WritePlan write = new PlanBuilder("group-members-add", "a bug")
            .Change(new PlanChange(PlanAction.Add, "Finance", "first", "-", "-"), _ => Task.FromException(new InvalidOperationException("a bug")))
            .Change(new PlanChange(PlanAction.Add, "Finance", "second", "-", "-"), _ => Task.CompletedTask)
            .Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => PlanGuard.RunAsync(write, WriteMode.Apply, PlanSamples.Store(home), Ct));

        PlanRecord record = PlanStore.Read(Assert.Single(Directory.GetFiles(Path.Combine(home, "plans"))))!;
        Assert.Equal(PlanMode.Applied, record.Plan.Mode);
        Assert.Equal("internal", record.Plan.Changes[0].Result!.Error!.Code);
        Assert.Equal(ChangeStatus.Skipped, record.Plan.Changes[1].Result!.Status);
    }

    [Fact]
    public void AStepPerChangeIsTheContract()
    {
        var plan = new Plan("x", "y", [new PlanChange(PlanAction.Add, "t", "d", "c", "w")], [], []);

        Assert.Throws<ArgumentException>(() => new WritePlan(plan, []));
    }

    [Fact]
    public async Task ThePartialApplyAsTheEnvelopeWritesIt()
    {
        string home = PlanSamples.TempHome();
        CommandResult<Plan> result = await PlanGuard.RunAsync(PlanSamples.GroupWrite([], second: PlanSamples.Forbidden), WriteMode.Apply, PlanSamples.Store(home), Ct);

        string envelope = EnvelopeJson.Serialize(result.ToEnvelope(new EnvelopeMeta(["00000000-0000-0000-0000-0000000000ec"], 12, Backend.Graph)));
        await Verify(PlanSamples.Scrub(envelope, home), extension: "json");
    }
}
