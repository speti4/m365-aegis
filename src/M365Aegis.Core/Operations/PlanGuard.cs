using System.Globalization;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Operations;

/// <summary>What a write was asked to do with its plan (D-006).</summary>
public enum WriteMode
{
    /// <summary><c>--dry-run</c>: print the plan, send nothing, exit 0.</summary>
    DryRun,

    /// <summary>Neither flag: print the plan, send nothing, exit 7.</summary>
    Refuse,

    /// <summary><c>--yes</c>, or the window's confirmation: apply the plan.</summary>
    Apply,
}

/// <summary>
/// The write guard (D-006, task 051): the one place a plan's steps run. A dry run is the plan (exit 0); a plan
/// without <c>--yes</c> is the plan with the <see cref="FailureKind.RefusedWithoutYes"/> failure (exit 7); a plan with
/// nothing to change is the plan (exit 0) whatever the flags, since nothing would be sent. An apply records the plan
/// under the home folder first, sends the changes one after another (neighboring changes of one batch together, up to
/// its size, each with the result of its own part; task 058), keeps going past a change the service refused,
/// and stops at a failure no later change could pass (the sign-in, the sidecar, the allow-list), marking the rest
/// skipped; then it records each change's result. Every change applied is exit 0; otherwise one change, or every
/// change failing the same way, is that failure's own exit code, and anything else is
/// <see cref="FailureKind.Partial"/> (exit 6) with the plan as the data. There is no <c>--force</c>.
/// </summary>
public static class PlanGuard
{
    /// <summary>The failures that end an apply: every later change would fail the same way.</summary>
    private static readonly FailureKind[] Stopping = [FailureKind.Auth, FailureKind.SidecarUnavailable, FailureKind.CmdletNotAllowed, FailureKind.Internal];

    /// <summary>Prints, refuses or applies a plan.</summary>
    /// <param name="write">The plan and its steps.</param>
    /// <param name="mode">What the command was asked to do.</param>
    /// <param name="store">Where an apply is recorded.</param>
    /// <param name="cancellationToken">Stops the apply between two changes.</param>
    public static async Task<CommandResult<Plan>> RunAsync(WritePlan write, WriteMode mode, PlanStore store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(store);
        Plan plan = write.Plan;
        int total = plan.Changes.Count;
        if (total == 0)
        {
            return CommandResult.Success(plan with { Mode = PlanMode.UpToDate });
        }

        switch (mode)
        {
            case WriteMode.DryRun:
                return CommandResult.Success(plan with { Mode = PlanMode.DryRun });
            case WriteMode.Refuse:
                return CommandResult.Failure(
                    new AegisFailure(
                        FailureKind.RefusedWithoutYes,
                        string.Format(CultureInfo.InvariantCulture, "{0} change(s) planned; nothing was sent", total),
                        "check the plan and run the command again with --yes to apply it; --dry-run prints it with exit code 0"),
                    plan with { Mode = PlanMode.Refused });
            case WriteMode.Apply:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "a write mode the guard does not know");
        }

        Plan applying = plan with { Mode = PlanMode.Applying, Started = store.Clock.GetUtcNow() };
        string record = store.Begin(applying);
        applying = applying with { Record = record };
        var results = new ChangeResult?[total];
        (int First, int Count) inFlight = (-1, 0);
        try
        {
            for (int index = 0; index < total;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PlanStep step = write.Step(index);
                int count = step.Batch is { } batch ? Neighbors(write, index, total, batch) : 1;
                inFlight = (index, count);
                bool stop = step.Batch is { } sending
                    ? await SendBatchAsync(write, sending, index, count, results, cancellationToken).ConfigureAwait(false)
                    : await SendAloneAsync(step, index, results, cancellationToken).ConfigureAwait(false);
                inFlight = (-1, 0);
                index += count;
                if (stop)
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is not AegisException)
        {
            // A bug or a cancellation in the middle of a change (or a batch of them): whether it reached the service is
            // unknown, which the record says; the exception goes on to the command runner.
            string code = exception is OperationCanceledException ? "cancelled" : FailureKind.Internal.ToCode();
            for (int index = inFlight.First; index >= 0 && index < inFlight.First + inFlight.Count; index++)
            {
                results[index] ??= new ChangeResult(ChangeStatus.Failed, new EnvelopeError(code, "the apply stopped during this change; check the object before running the plan again"));
            }

            store.Finish(record, Finish(applying, results, store));
            throw;
        }

        Plan applied = Finish(applying, results, store);
        store.Finish(record, applied);
        return Outcome(applied, record);
    }

    /// <summary>A change sent alone; true when its failure stops the apply.</summary>
    private static async Task<bool> SendAloneAsync(PlanStep step, int index, ChangeResult?[] results, CancellationToken cancellationToken)
    {
        try
        {
            await step.Apply!(cancellationToken).ConfigureAwait(false);
            results[index] = new ChangeResult(ChangeStatus.Applied);
            return false;
        }
        catch (AegisException exception)
        {
            results[index] = new ChangeResult(ChangeStatus.Failed, exception.Failure.ToError());
            return Stopping.Contains(exception.Failure.Kind);
        }
    }

    /// <summary>
    /// The changes at <paramref name="first"/> and after sent as one request of their batch (task 058), each with the
    /// outcome of its own part; a request that failed as a whole fails each of its changes the same way. True when a
    /// failure stops the apply.
    /// </summary>
    private static async Task<bool> SendBatchAsync(WritePlan write, PlanBatch batch, int first, int count, ChangeResult?[] results, CancellationToken cancellationToken)
    {
        List<object> items = Enumerable.Range(first, count).Select(index => write.Step(index).Item!).ToList();
        IReadOnlyList<AegisFailure?> outcomes;
        try
        {
            outcomes = await batch.SendAsync(items, cancellationToken).ConfigureAwait(false);
        }
        catch (AegisException exception)
        {
            outcomes = Enumerable.Repeat<AegisFailure?>(exception.Failure, count).ToList();
        }

        bool stop = false;
        for (int offset = 0; offset < count; offset++)
        {
            AegisFailure? failure = outcomes[offset];
            results[first + offset] = failure is null ? new ChangeResult(ChangeStatus.Applied) : new ChangeResult(ChangeStatus.Failed, failure.ToError());
            stop |= failure is not null && Stopping.Contains(failure.Kind);
        }

        return stop;
    }

    /// <summary>How many changes from <paramref name="first"/> on go in one request of <paramref name="batch"/>: its neighbors in the plan, at most its size.</summary>
    private static int Neighbors(WritePlan write, int first, int total, PlanBatch batch)
    {
        int count = 1;
        while (count < batch.Size && first + count < total && ReferenceEquals(write.Step(first + count).Batch, batch))
        {
            count++;
        }

        return count;
    }

    /// <summary>The applied plan: each change with its result, an unsent one skipped.</summary>
    private static Plan Finish(Plan applying, ChangeResult?[] results, PlanStore store) => applying with
    {
        Mode = PlanMode.Applied,
        Finished = store.Clock.GetUtcNow(),
        Changes = applying.Changes.Select((change, index) => change with { Result = results[index] ?? new ChangeResult(ChangeStatus.Skipped) }).ToList(),
    };

    private static CommandResult<Plan> Outcome(Plan applied, string record)
    {
        int total = applied.Changes.Count;
        int done = applied.Changes.Count(change => change.Result?.Status == ChangeStatus.Applied);
        if (done == total)
        {
            return CommandResult.Success(applied);
        }

        List<EnvelopeError> errors = applied.Changes.Where(change => change.Result?.Status == ChangeStatus.Failed).Select(change => change.Result!.Error!).ToList();
        int skipped = applied.Changes.Count(change => change.Result?.Status == ChangeStatus.Skipped);
        string tail = skipped > 0 ? string.Format(CultureInfo.InvariantCulture, ", {0} skipped", skipped) : "";
        if (done == 0 && errors.Count > 0 && errors.All(error => error.Code == errors[0].Code) && Kind(errors[0].Code) is { } kind)
        {
            string message = total == 1
                ? errors[0].Message
                : string.Format(CultureInfo.InvariantCulture, "none of the {0} changes was applied{1}: {2}", total, tail, errors[0].Message);
            return CommandResult.Failure(new AegisFailure(kind, message, errors[0].Hint ?? RecordHint(record)), applied);
        }

        return CommandResult.Failure(
            new AegisFailure(
                FailureKind.Partial,
                string.Format(CultureInfo.InvariantCulture, "{0} of {1} change(s) applied, {2} failed{3}", done, total, errors.Count, tail),
                RecordHint(record)),
            applied);
    }

    private static string RecordHint(string record) =>
        $"the data lists each change's result and the service's message; the plan record is {record}";

    /// <summary>The failure kind of an envelope's error code, or null for one the contract does not name (a cancellation).</summary>
    private static FailureKind? Kind(string code) =>
        Enum.GetValues<FailureKind>().Cast<FailureKind?>().FirstOrDefault(kind => kind!.Value.ToCode() == code);
}
