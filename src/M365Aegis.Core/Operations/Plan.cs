using M365Aegis.Core.Output;

namespace M365Aegis.Core.Operations;

/// <summary>What a planned change does to its target (task 051).</summary>
public enum PlanAction
{
    /// <summary>Adds something: a member, an owner, a right.</summary>
    Add,

    /// <summary>Removes something: a member, an owner, a right.</summary>
    Remove,

    /// <summary>Sets a value: a forwarding address.</summary>
    Set,

    /// <summary>Clears a value: the forwarding.</summary>
    Clear,

    /// <summary>Sends a request as it was given (the raw <c>call</c>).</summary>
    Send,
}

/// <summary>Where a plan stands: printed, refused, being applied, applied, or with nothing to change.</summary>
public enum PlanMode
{
    /// <summary>Printed under <c>--dry-run</c>; nothing was sent.</summary>
    DryRun,

    /// <summary>Printed without <c>--yes</c> (exit code 7); nothing was sent.</summary>
    Refused,

    /// <summary>The apply started: what a plan record says until the apply ends (a record left so was cut short).</summary>
    Applying,

    /// <summary>The apply ended; every change carries its result.</summary>
    Applied,

    /// <summary>Every object is already as asked: there is nothing to send, whatever the flags.</summary>
    UpToDate,
}

/// <summary>What became of one change of an applied plan.</summary>
public enum ChangeStatus
{
    /// <summary>The service accepted the change.</summary>
    Applied,

    /// <summary>The service refused the change, or it could not be sent; the error says why.</summary>
    Failed,

    /// <summary>Not sent: an earlier failure stopped the apply (the sign-in, the sidecar).</summary>
    Skipped,
}

/// <summary>The result of one change of an applied plan.</summary>
/// <param name="Status">Applied, failed or skipped.</param>
/// <param name="Error">For a failed change, the failure as the envelope writes it; null otherwise.</param>
public sealed record ChangeResult(ChangeStatus Status, EnvelopeError? Error = null);

/// <summary>One change of a plan: what changes on which object, from what to what.</summary>
/// <param name="Action">What the change does.</param>
/// <param name="Target">The object that changes (a group, a mailbox), by name and address or id.</param>
/// <param name="Detail">What changes on it, in the team's words (<c>Dave Example (dave@contoso.com) as a member</c>).</param>
/// <param name="Current">The state now, as the plan read it.</param>
/// <param name="Desired">The state the change makes.</param>
public sealed record PlanChange(PlanAction Action, string Target, string Detail, string Current, string Desired)
{
    /// <summary>The line of the CSV file the change comes from (task 054); null for a change from the arguments.</summary>
    public int? Row { get; init; }

    /// <summary>What the apply did with the change; null until it was applied.</summary>
    public ChangeResult? Result { get; init; }
}

/// <summary>Something the command was asked for that is already so, and is left alone.</summary>
/// <param name="Target">The object, as a change names it.</param>
/// <param name="Detail">What was asked for on it.</param>
/// <param name="State">Why nothing changes (<c>a member already</c>).</param>
public sealed record PlanUnchanged(string Target, string Detail, string State)
{
    /// <summary>The line of the CSV file it comes from; null for the arguments.</summary>
    public int? Row { get; init; }
}

/// <summary>
/// A write as a plan (D-006, task 051): the current state against the desired one, computed before anything is sent.
/// <c>--dry-run</c> prints it, <c>--yes</c> applies it, neither prints it and exits 7; the window shows the same plan
/// and asks for a confirmation. An applied plan carries each change's result and the plan record's file. The shape
/// is the one the task card sketch of task 018 made up (a title, the changes, what is already so, the warnings), so
/// a card reads this type.
/// </summary>
/// <param name="Operation">The write, stable and kebab-case (<c>group-members-add</c>): a script matches it, the record's file name carries it.</param>
/// <param name="Title">One line: what the plan does.</param>
/// <param name="Changes">What would change, in the order the apply sends it.</param>
/// <param name="Unchanged">What is already as asked.</param>
/// <param name="Warnings">What to know before confirming.</param>
public sealed record Plan(string Operation, string Title, IReadOnlyList<PlanChange> Changes, IReadOnlyList<PlanUnchanged> Unchanged, IReadOnlyList<string> Warnings)
{
    /// <summary>Where the plan stands.</summary>
    public PlanMode Mode { get; init; } = PlanMode.DryRun;

    /// <summary>The plan record's file under the home folder's <c>plans</c> folder, once the apply started.</summary>
    public string? Record { get; init; }

    /// <summary>When the apply started; null before.</summary>
    public DateTimeOffset? Started { get; init; }

    /// <summary>When the apply ended; null before.</summary>
    public DateTimeOffset? Finished { get; init; }

    /// <summary>
    /// The plan record this plan continues (<c>--resume</c>, task 062): its changes are the ones that record left
    /// failed or skipped, planned again against the current state; null for a plan of its own.
    /// </summary>
    public string? Resumes { get; init; }
}
