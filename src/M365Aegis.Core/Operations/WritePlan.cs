namespace M365Aegis.Core.Operations;

/// <summary>
/// A <see cref="Operations.Plan"/> with what applies it: one step per change, in the plan's order, each sending that
/// change only (task 051). A write operation of the core returns it; <see cref="PlanGuard"/> prints it, refuses it or
/// runs the steps, so no write is sent outside the guard.
/// </summary>
public sealed class WritePlan
{
    private readonly IReadOnlyList<Func<CancellationToken, Task>> _steps;

    /// <summary>A plan and its steps, one per change.</summary>
    /// <exception cref="ArgumentException">The steps are not one per change.</exception>
    public WritePlan(Plan plan, IReadOnlyList<Func<CancellationToken, Task>> steps)
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

    /// <summary>The plan.</summary>
    public Plan Plan { get; }

    /// <summary>The step that applies the change at <paramref name="index"/>.</summary>
    internal Func<CancellationToken, Task> Step(int index) => _steps[index];
}

/// <summary>Collects a plan's changes with their steps, what is already so and the warnings, in order.</summary>
/// <param name="operation">The write's stable name (<see cref="Plan.Operation"/>).</param>
/// <param name="title">The plan's one line.</param>
public sealed class PlanBuilder(string operation, string title)
{
    private readonly List<PlanChange> _changes = [];
    private readonly List<Func<CancellationToken, Task>> _steps = [];
    private readonly List<PlanUnchanged> _unchanged = [];
    private readonly List<string> _warnings = [];

    /// <summary>The plan's one line; a write that knows its scope only at the end sets it then.</summary>
    public string Title { get; set; } = title;

    /// <summary>How many changes so far.</summary>
    public int Changes => _changes.Count;

    /// <summary>Adds a change and the step that applies it.</summary>
    public PlanBuilder Change(PlanChange change, Func<CancellationToken, Task> apply)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(apply);
        _changes.Add(change);
        _steps.Add(apply);
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
    public WritePlan Build() => new(new Plan(operation, Title, _changes.ToList(), _unchanged.ToList(), _warnings.ToList()), _steps.ToList());
}
