using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Operations;

/// <summary>
/// The guard's batches (task 058) over made-up batches: neighboring changes of one batch go in one request up to its
/// size, a change sent alone splits them, each change has the outcome of its own item, and a request that fails as a
/// whole fails each of its changes, an auth failure stopping the apply there.
/// </summary>
public sealed class PlanBatchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly AegisFailure Refused = new(FailureKind.Forbidden, "refused on purpose", "a made-up refusal");

    /// <summary>A batch of <paramref name="size"/> that records each call's items and answers each item from <paramref name="answer"/>.</summary>
    private static PlanBatch<string> Batch(int size, List<IReadOnlyList<string>> calls, Func<int, IReadOnlyList<string>, IReadOnlyList<AegisFailure?>>? answer = null) =>
        new(size, (items, _) =>
        {
            calls.Add(items);
            return Task.FromResult(answer?.Invoke(calls.Count, items) ?? items.Select(_ => (AegisFailure?)null).ToList());
        });

    private static PlanChange Change(string name) => new(PlanAction.Add, "Finance", name, "not a member", "a member");

    [Fact]
    public async Task NeighborsGoTogetherUpToTheSizeAndAChangeSentAloneSplitsThem()
    {
        var calls = new List<IReadOnlyList<string>>();
        var alone = new List<string>();
        PlanBatch<string> batch = Batch(2, calls, (_, items) => items.Select(item => item == "c" ? Refused : null).ToList());
        WritePlan write = new PlanBuilder("group-members-add", "made up")
            .Change(Change("a"), PlanStep.Batched(batch, "a"))
            .Change(Change("b"), PlanStep.Batched(batch, "b"))
            .Change(Change("c"), PlanStep.Batched(batch, "c"))
            .Change(Change("d"), _ =>
            {
                alone.Add("d");
                return Task.CompletedTask;
            })
            .Change(Change("e"), PlanStep.Batched(batch, "e"))
            .Build();

        CommandResult<Plan> result = await PlanGuard.RunAsync(write, WriteMode.Apply, PlanSamples.Store(PlanSamples.TempHome()), Ct);

        Assert.Equal([["a", "b"], ["c"], ["e"]], calls);
        Assert.Equal(["d"], alone);
        Assert.Equal(ExitCode.Partial, result.ExitCode);
        Assert.Equal(
            [ChangeStatus.Applied, ChangeStatus.Applied, ChangeStatus.Failed, ChangeStatus.Applied, ChangeStatus.Applied],
            result.Data!.Changes.Select(change => change.Result!.Status));
        Assert.Equal("forbidden", result.Data.Changes[2].Result!.Error!.Code);
    }

    [Fact]
    public async Task ARequestThatFailsAsAWholeFailsItsChangesAndASignInFailureStopsTheApply()
    {
        var calls = new List<IReadOnlyList<string>>();
        var batch = new PlanBatch<string>(2, (items, _) =>
        {
            calls.Add(items);
            return calls.Count == 2
                ? Task.FromException<IReadOnlyList<AegisFailure?>>(new AegisException(FailureKind.Auth, "the token was refused", "sign in again"))
                : Task.FromResult<IReadOnlyList<AegisFailure?>>(items.Select(_ => (AegisFailure?)null).ToList());
        });
        var builder = new PlanBuilder("group-members-add", "made up");
        foreach (string name in new[] { "a", "b", "c", "d", "e" })
        {
            builder.Change(Change(name), PlanStep.Batched(batch, name));
        }

        CommandResult<Plan> result = await PlanGuard.RunAsync(builder.Build(), WriteMode.Apply, PlanSamples.Store(PlanSamples.TempHome()), Ct);

        Assert.Equal(2, calls.Count);
        Assert.Equal(
            [ChangeStatus.Applied, ChangeStatus.Applied, ChangeStatus.Failed, ChangeStatus.Failed, ChangeStatus.Skipped],
            result.Data!.Changes.Select(change => change.Result!.Status));
        Assert.Equal("auth", result.Data.Changes[3].Result!.Error!.Code);
        Assert.Equal("2 of 5 change(s) applied, 2 failed, 1 skipped", result.Failure!.Message);
    }

    [Fact]
    public async Task ABatchMustAnswerEveryItem()
    {
        var batch = new PlanBatch<string>(5, (_, _) => Task.FromResult<IReadOnlyList<AegisFailure?>>([null]));
        WritePlan write = new PlanBuilder("group-members-add", "made up")
            .Change(Change("a"), PlanStep.Batched(batch, "a"))
            .Change(Change("b"), PlanStep.Batched(batch, "b"))
            .Build();
        string home = PlanSamples.TempHome();

        await Assert.ThrowsAsync<InvalidOperationException>(() => PlanGuard.RunAsync(write, WriteMode.Apply, PlanSamples.Store(home), Ct));

        PlanRecord record = PlanStore.Read(Assert.Single(Directory.GetFiles(Path.Combine(home, "plans"))))!;
        Assert.All(record.Plan.Changes, change => Assert.Equal("internal", change.Result!.Error!.Code));
    }
}
