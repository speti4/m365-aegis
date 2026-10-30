using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Operations;

/// <summary>
/// The plans, the guard, the records and the CSV reader at their edges: a mode the guard does not know, a step that
/// breaks, a failure without a hint, records that are not records, a resume by CSV line, the CSV syntax's corners.
/// </summary>
public sealed class OperationsEdgeTests
{
    private static readonly string[] Columns = ["group", "user"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AModeTheGuardDoesNotKnowIsRefused()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            PlanGuard.RunAsync(OneChange(_ => Task.CompletedTask), (WriteMode)99, PlanSamples.Store(PlanSamples.TempHome()), Ct));
    }

    [Fact]
    public async Task AStepThatBreaksIsAnInternalFailureInTheRecordAndTheExceptionGoesOn()
    {
        string home = PlanSamples.TempHome();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PlanGuard.RunAsync(OneChange(_ => throw new InvalidOperationException("a bug")), WriteMode.Apply, PlanSamples.Store(home), Ct));

        PlanRecordSummary record = Assert.Single(PlanStore.List(home).Records);
        Assert.Equal(1, record.Failed);
    }

    [Fact]
    public async Task AStepThatIsCancelledIsMarkedSoInTheRecord()
    {
        string home = PlanSamples.TempHome();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            PlanGuard.RunAsync(OneChange(_ => throw new OperationCanceledException()), WriteMode.Apply, PlanSamples.Store(home), Ct));

        (_, PlanRecord record) = PlanStore.Open(home, Assert.Single(PlanStore.List(home).Records).Name);
        Assert.Equal("cancelled", Assert.Single(record.Plan.Changes).Result!.Error!.Code);
    }

    [Fact]
    public async Task AFailureWithoutAHintPointsAtTheRecord()
    {
        CommandResult<Plan> result = await PlanGuard.RunAsync(
            OneChange(_ => throw new AegisException(new AegisFailure(FailureKind.Forbidden, "refused"))),
            WriteMode.Apply,
            PlanSamples.Store(PlanSamples.TempHome()),
            Ct);

        Assert.Equal(FailureKind.Forbidden, result.Failure!.Kind);
        Assert.Contains("the plan record is", result.Failure.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void APlanAndItsStepsAndTheBuildersCounts()
    {
        var plan = new Plan("op", "title", [Change()], [], []);

        Assert.Throws<ArgumentNullException>(() => new WritePlan(plan, null!));
        Assert.Same(plan, new WritePlan(plan, [_ => Task.CompletedTask]).Plan);
        Assert.Throws<ArgumentNullException>(() => new PlanBatch<string>(20, null!));
        PlanBuilder builder = new PlanBuilder("op", "title").Change(Change(), _ => Task.CompletedTask).Warn("twice").Warn("twice");
        Assert.Equal(1, builder.Changes);
        Assert.Equal(["twice"], builder.Build().Plan.Warnings);
    }

    [Fact]
    public void ARecordWithoutAStartIsNamedByTheClock()
    {
        string home = PlanSamples.TempHome();
        var store = new PlanStore(home, new PlanRecordContext("tests", null, []), new FixedClock(new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero)));

        string path = store.Begin(new Plan("group-members-add", "title", [Change()], [], []));

        Assert.Equal("20261030T080000.000Z-group-members-add.json", Path.GetFileName(path));
    }

    [Theory]
    [InlineData("  ", FailureKind.Usage)]
    [InlineData("missing.json", FailureKind.NotFound)]
    public void ARecordNamedBlankOrMissingIsRefused(string name, FailureKind kind)
    {
        AegisException exception = Assert.Throws<AegisException>(() => PlanStore.Open(PlanSamples.TempHome(), name));

        Assert.Equal(kind, exception.Failure.Kind);
    }

    [Fact]
    public void AMissingRecordGivenByItsPathIsNotFound()
    {
        string home = PlanSamples.TempHome();

        AegisException exception = Assert.Throws<AegisException>(() => PlanStore.Open(home, Path.Combine(home, "plans", "none.json")));

        Assert.Equal(FailureKind.NotFound, exception.Failure.Kind);
    }

    [Fact]
    public void FilesThatAreNotRecordsAreCountedAndAResumeIsNamedByItsFile()
    {
        string home = PlanSamples.TempHome();
        string folder = Path.Combine(home, "plans");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "a.json"), "null");
        File.WriteAllText(Path.Combine(folder, "b.json"), "{}");
        File.WriteAllText(Path.Combine(folder, "c.json"), """{"plan":{"changes":[]}}""");
        File.WriteAllText(Path.Combine(folder, "d.json"), """{"plan":{"operation":"group-members-add"}}""");
        var store = new PlanStore(home, new PlanRecordContext("tests", null, []));
        store.Begin(new Plan("group-members-add", "title", [Change()], [], []) { Started = PlanSamples.Now, Resumes = Path.Combine(folder, "earlier.json") });

        PlanRecordList list = PlanStore.List(home);

        Assert.Equal(4, list.Unreadable);
        Assert.Equal("earlier.json", Assert.Single(list.Records).Resumes);
    }

    [Fact]
    public void AResumeByCsvLineKeepsTheLinesTheNewPlanNamesAndWarnsOfTheOthers()
    {
        string home = PlanSamples.TempHome();
        var store = new PlanStore(home, new PlanRecordContext("tests", null, []));
        PlanChange line2 = Change(2) with { Result = new ChangeResult(ChangeStatus.Failed, new EnvelopeError("throttled", "429", null)) };
        PlanChange line3 = Change(3) with { Result = new ChangeResult(ChangeStatus.Skipped) };
        PlanChange line4 = Change(4);
        string path = store.Begin(new Plan("group-members-add", "title", [line2, line3, line4], [], []) { Started = PlanSamples.Now });
        WritePlan fresh = new PlanBuilder("group-members-add", "title")
            .Change(Change(2), _ => Task.CompletedTask)
            .Unchanged(new PlanUnchanged("Finance", "line 3's user as a member", "a member already") { Row = 3 })
            .Build();

        WritePlan narrowed = PlanResume.Open(home, path).Narrow(fresh);

        Assert.Equal([2], narrowed.Plan.Changes.Select(change => change.Row));
        Assert.Single(narrowed.Plan.Unchanged);
        string warning = Assert.Single(narrowed.Plan.Warnings);
        Assert.StartsWith("line 4, Finance", warning, StringComparison.Ordinal);
        Assert.Contains("without a result", warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("group;x,user\nSales,alice\n", "unknown column 'group;x'")]
    [InlineData("group,user,extra\nSales,alice,x\n", "unknown column 'extra'")]
    [InlineData("group,,user\nSales,,alice\n", "the header has an empty column name")]
    [InlineData("group,user\n\"Sales\" \"x\",alice\n", "a quote inside a cell")]
    public void ACsvThatBreaksTheSyntaxIsRefused(string text, string what)
    {
        AegisException exception = Assert.Throws<AegisException>(() => CsvRows.Parse(text, "rows.csv", Columns, []));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
        Assert.Contains(what, exception.Failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("group,user\rSales,alice\r")]
    [InlineData("group,user\nSales,alice")]
    [InlineData("group,user\n\"Sales\" ,alice")]
    public void OldLineBreaksAndALastLineWithoutABreakAreRead(string text)
    {
        CsvRow row = Assert.Single(CsvRows.Parse(text, "rows.csv", Columns, []));

        Assert.Equal(("Sales", "alice"), (row["group"], row["user"]));
    }

    [Theory]
    [InlineData("group,user\nSales,")]
    [InlineData("user\n\"\"")]
    public void ALastLineOfEmptyCellsIsStillALine(string text)
    {
        AegisException exception = Assert.Throws<AegisException>(() => CsvRows.Parse(text, "rows.csv", text.StartsWith("user", StringComparison.Ordinal) ? ["user"] : Columns, []));

        Assert.Contains("line 2", exception.Failure.Message, StringComparison.Ordinal);
    }

    private static PlanChange Change(int? row = null) =>
        new(PlanAction.Add, "Finance", row is { } line ? $"line {line}'s user as a member" : "Alice as a member", "not a member", "a member") { Row = row };

    private static WritePlan OneChange(Func<CancellationToken, Task> apply) =>
        new PlanBuilder("group-members-add", "title").Change(Change(), apply).Build();

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
