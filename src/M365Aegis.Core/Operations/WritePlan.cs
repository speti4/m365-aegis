using M365Aegis.Core.Output;

namespace M365Aegis.Core.Operations;

/// <summary>
/// A <see cref="Operations.Plan"/> with what applies it: one step per change, in the plan's order (task 051). A step
/// sends its change alone, or is one item of a <see cref="PlanBatch"/> that sends several changes in one request (the
/// Graph <c>$batch</c> of task 058), each change's result read from its own part of the answer. A write operation of
/// the core returns it; <see cref="PlanGuard"/> prints it, refuses it or runs the steps, so no write is sent outside
/// the guard.
/// </summary>
public sealed class WritePlan
{
    private readonly IReadOnlyList<PlanStep> _steps;

    /// <summary>A plan and its steps, one per change, each sending its change alone.</summary>
    /// <exception cref="ArgumentException">The steps are not one per change.</exception>
    public WritePlan(Plan plan, IReadOnlyList<Func<CancellationToken, Task>> steps)
        : this((steps ?? throw new ArgumentNullException(nameof(steps))).Select(PlanStep.Alone).ToList(), plan)
    {
    }

    private WritePlan(IReadOnlyList<PlanStep> steps, Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count != plan.Changes.Count)
        {
            throw new ArgumentException($"{steps.Count} step(s) for {plan.Changes.Count} change(s)", nameof(steps));
        }

        Plan = plan;
        _steps = steps;
    }

    /// <summary>A plan and its steps, one per change, each alone or an item of a batch.</summary>
    /// <exception cref="ArgumentException">The steps are not one per change.</exception>
    public static WritePlan WithSteps(Plan plan, IReadOnlyList<PlanStep> steps) => new(steps, plan);

    /// <summary>The plan.</summary>
    public Plan Plan { get; }

    /// <summary>The step that applies the change at <paramref name="index"/>.</summary>
    internal PlanStep Step(int index) => _steps[index];

    /// <summary>
    /// The same writes over another plan whose changes are the ones at <paramref name="indexes"/> of this plan, in
    /// that order (a resume keeps the changes it continues, task 062).
    /// </summary>
    internal WritePlan Select(Plan plan, IReadOnlyList<int> indexes) => new(indexes.Select(index => _steps[index]).ToList(), plan);
}

/// <summary>How one change of a <see cref="WritePlan"/> is sent: alone, or as an item of a batch (task 058).</summary>
public sealed class PlanStep
{
    private PlanStep(Func<CancellationToken, Task>? apply, PlanBatch? batch, object? item)
    {
        Apply = apply;
        Batch = batch;
        Item = item;
    }

    /// <summary>The step of a change sent alone; null for a batch item.</summary>
    internal Func<CancellationToken, Task>? Apply { get; }

    /// <summary>The batch a batch item belongs to; null for a change sent alone.</summary>
    internal PlanBatch? Batch { get; }

    /// <summary>What the batch sends for this change.</summary>
    internal object? Item { get; }

    /// <summary>A change sent alone: the step throws an <see cref="AegisException"/> when the service refuses it.</summary>
    public static PlanStep Alone(Func<CancellationToken, Task> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        return new PlanStep(apply, null, null);
    }

    /// <summary>A change sent as one item of <paramref name="batch"/>, with its neighbors in the plan of the same batch.</summary>
    public static PlanStep Batched<T>(PlanBatch<T> batch, T item)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(item);
        return new PlanStep(null, batch, item);
    }
}

/// <summary>
/// Sends several changes of a plan in one request (task 058). The guard hands it the items of up to <see cref="Size"/>
/// neighboring changes of the plan that belong to it, and reads one outcome per item back.
/// </summary>
public abstract class PlanBatch
{
    /// <summary>A batch of at most <paramref name="size"/> items per request.</summary>
    protected PlanBatch(int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        Size = size;
    }

    /// <summary>The most items one request carries.</summary>
    public int Size { get; }

    /// <summary>Sends the items; one entry per item, in order: null when it was applied, its failure otherwise.</summary>
    /// <exception cref="AegisException">The request itself failed: none of its items was applied.</exception>
    internal abstract Task<IReadOnlyList<AegisFailure?>> SendAsync(IReadOnlyList<object> items, CancellationToken cancellationToken);
}

/// <summary>A <see cref="PlanBatch"/> whose items are of one type (a Graph request).</summary>
/// <typeparam name="T">The item a change sends.</typeparam>
/// <param name="size">The most items one request carries.</param>
/// <param name="send">Sends the items; one entry per item, in order: null when applied, the failure otherwise.</param>
public sealed class PlanBatch<T>(int size, Func<IReadOnlyList<T>, CancellationToken, Task<IReadOnlyList<AegisFailure?>>> send) : PlanBatch(size)
    where T : notnull
{
    private readonly Func<IReadOnlyList<T>, CancellationToken, Task<IReadOnlyList<AegisFailure?>>> _send = send ?? throw new ArgumentNullException(nameof(send));

    internal override async Task<IReadOnlyList<AegisFailure?>> SendAsync(IReadOnlyList<object> items, CancellationToken cancellationToken)
    {
        IReadOnlyList<AegisFailure?> outcomes = await _send(items.Cast<T>().ToList(), cancellationToken).ConfigureAwait(false);
        return outcomes.Count == items.Count
            ? outcomes
            : throw new InvalidOperationException($"a batch answered {outcomes.Count} outcome(s) for {items.Count} item(s)");
    }
}

/// <summary>Collects a plan's changes with their steps, what is already so and the warnings, in order.</summary>
/// <param name="operation">The write's stable name (<see cref="Plan.Operation"/>).</param>
/// <param name="title">The plan's one line.</param>
public sealed class PlanBuilder(string operation, string title)
{
    private readonly List<PlanChange> _changes = [];
    private readonly List<PlanStep> _steps = [];
    private readonly List<PlanUnchanged> _unchanged = [];
    private readonly List<string> _warnings = [];

    /// <summary>The plan's one line; a write that knows its scope only at the end sets it then.</summary>
    public string Title { get; set; } = title;

    /// <summary>How many changes so far.</summary>
    public int Changes => _changes.Count;

    /// <summary>Adds a change and the step that applies it alone.</summary>
    public PlanBuilder Change(PlanChange change, Func<CancellationToken, Task> apply) => Change(change, PlanStep.Alone(apply));

    /// <summary>Adds a change and its step (alone, or an item of a batch).</summary>
    public PlanBuilder Change(PlanChange change, PlanStep step)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(step);
        _changes.Add(change);
        _steps.Add(step);
        return this;
    }

    /// <summary>Adds something that is already as asked.</summary>
    public PlanBuilder Unchanged(PlanUnchanged unchanged)
    {
        ArgumentNullException.ThrowIfNull(unchanged);
        _unchanged.Add(unchanged);
        return this;
    }

    /// <summary>Adds a warning; the same sentence twice is kept once.</summary>
    public PlanBuilder Warn(string warning)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(warning);
        if (!_warnings.Contains(warning, StringComparer.Ordinal))
        {
            _warnings.Add(warning);
        }

        return this;
    }

    /// <summary>The plan with its steps.</summary>
    public WritePlan Build() => WritePlan.WithSteps(new Plan(operation, Title, _changes.ToList(), _unchanged.ToList(), _warnings.ToList()), _steps.ToList());
}
