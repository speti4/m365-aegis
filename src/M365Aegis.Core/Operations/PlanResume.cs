using System.Globalization;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Operations;

/// <summary>
/// A resume of a half-applied plan from its record (<c>--resume &lt;record&gt;</c>, task 062). The record names the
/// changes its apply left failed or skipped (or, for an apply that was cut short, every change without a result); the
/// command plans its original changes again against the current state (the record keeps the arguments and a CSV
/// file's text, so the same rows come back), and <see cref="Narrow"/> keeps of that plan only what continues those
/// changes: a change still to make is planned again, one made in the meantime comes out unchanged, and one the record
/// applied is left out whatever its state now. The new plan names the record it resumes (<see cref="Plan.Resumes"/>),
/// and its own record links it. A raw <c>call</c> is never resumed, and a record whose changes were all applied has
/// nothing to resume. A record of version 2 (task 065) numbers each change's row and keys it by object ids:
/// <see cref="Select{T}"/> hands the write only the rows of the pending changes, so a resume of 60 pending changes of
/// 100 reads 60 objects, and <see cref="Narrow"/> matches a pending change by its object ids first, so a target
/// renamed since the apply still matches; an older record plans every row again and matches by the text.
/// </summary>
public sealed class PlanResume
{
    private const string Call = "call";

    private PlanResume(string record, PlanRecord original, IReadOnlyList<PlanChange> pending)
    {
        Record = record;
        Original = original;
        Pending = pending;
    }

    /// <summary>The record's file.</summary>
    public string Record { get; }

    /// <summary>The record as the file holds it.</summary>
    public PlanRecord Original { get; }

    /// <summary>The changes to continue: failed, skipped, or without a result (an apply cut short).</summary>
    public IReadOnlyList<PlanChange> Pending { get; }

    private string Name => Path.GetFileName(Record);

    /// <summary>Reads a record to resume.</summary>
    /// <param name="home">The home folder.</param>
    /// <param name="record">The record's name, as <c>plan list</c> shows it, or its path.</param>
    /// <exception cref="AegisException">No such record (exit code 4), or one that cannot be resumed: not a plan record, a raw call, every change applied (exit code 2).</exception>
    public static PlanResume Open(string home, string record)
    {
        (string path, PlanRecord original) = PlanStore.Open(home, record);
        string name = Path.GetFileName(path);
        if (string.Equals(original.Plan.Operation, Call, StringComparison.Ordinal))
        {
            throw new AegisException(
                FailureKind.Usage,
                $"'{name}' is the record of a raw call, which is not resumed",
                "send the call again; --resume continues the group and mailbox writes");
        }

        List<PlanChange> pending = original.Plan.Changes.Where(change => change.Result?.Status is null or ChangeStatus.Failed or ChangeStatus.Skipped).ToList();
        if (pending.Count == 0)
        {
            throw new AegisException(
                FailureKind.Usage,
                $"every change of '{name}' was applied: nothing to resume",
                $"`m365-aegis plan show {name}` lists the changes and their results");
        }

        return new PlanResume(path, original, pending);
    }

    /// <summary>
    /// The rows of the write to plan again (task 065): of a record of version 2 whose pending changes all name their row,
    /// only those rows, each keeping its line and its place, so the new plan reads only the objects they name; every row
    /// for an older record, which does not number them.
    /// </summary>
    /// <typeparam name="T">The write's row.</typeparam>
    /// <param name="rows">Every row of the record's arguments or file, in order.</param>
    public IReadOnlyList<T> Select<T>(IReadOnlyList<T> rows)
        where T : IPlanRow
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (Original.Version < PlanStore.RecordVersion || Pending.Any(change => change.Index is null))
        {
            return rows;
        }

        HashSet<int> indexes = Pending.Select(change => change.Index!.Value).ToHashSet();
        return rows.Where(row => indexes.Contains(row.Index)).ToList();
    }

    /// <summary>Refuses a record of another write than <paramref name="operation"/>.</summary>
    /// <exception cref="AegisException">A usage error naming the record's operation.</exception>
    public void EnsureOperation(string operation)
    {
        if (!string.Equals(Original.Plan.Operation, operation, StringComparison.Ordinal))
        {
            throw new AegisException(
                FailureKind.Usage,
                $"'{Name}' is a record of {Original.Plan.Operation}, not of {operation}",
                $"resume it with the command that wrote it: {Words(Original.Plan.Operation)} --resume {Name}");
        }
    }

    /// <summary>
    /// The plan of the resume: of <paramref name="fresh"/>, the original changes planned again against the current
    /// state, the changes and the objects already as asked that continue a pending change of the record (by the object
    /// ids when both carry them, task 065; else by the CSV line when the record has one, else by the action, the target
    /// and the change), with their steps; a pending change the new plan does not name becomes a warning.
    /// </summary>
    /// <exception cref="AegisException">A usage error when <paramref name="fresh"/> is the plan of another write.</exception>
    public WritePlan Narrow(WritePlan fresh)
    {
        ArgumentNullException.ThrowIfNull(fresh);
        EnsureOperation(fresh.Plan.Operation);
        HashSet<int> rows = Pending.Where(change => change.Row is not null).Select(change => change.Row!.Value).ToHashSet();
        HashSet<string> keys = Pending.Where(change => change.Row is null).Select(change => Key(change.Action, change.Target, change.Detail)).ToHashSet(StringComparer.Ordinal);
        HashSet<string> objects = Pending.Where(change => change.Row is null).Select(change => Key(null, change.Target, change.Detail)).ToHashSet(StringComparer.Ordinal);
        HashSet<string> ids = Pending.Where(change => change.Key is not null).Select(change => Ids(change.Action, change.Key!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> objectIds = Pending.Where(change => change.Key is not null).Select(change => Ids(null, change.Key!)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var kept = new List<int>();
        for (int index = 0; index < fresh.Plan.Changes.Count; index++)
        {
            PlanChange change = fresh.Plan.Changes[index];
            if ((change.Key is { } key && ids.Contains(Ids(change.Action, key)))
                || (change.Row is { } row ? rows.Contains(row) : keys.Contains(Key(change.Action, change.Target, change.Detail))))
            {
                kept.Add(index);
            }
        }

        List<PlanUnchanged> unchanged = fresh.Plan.Unchanged
            .Where(entry => (entry.Key is { } key && objectIds.Contains(Ids(null, key)))
                || (entry.Row is { } row ? rows.Contains(row) : objects.Contains(Key(null, entry.Target, entry.Detail))))
            .ToList();

        var warnings = fresh.Plan.Warnings.ToList();
        // A plain loop, not a Where over a lambda: the lambda would capture the locals, and its cached delegate is a
        // branch no call can take twice.
        foreach (PlanChange change in Pending)
        {
            if (Continued(change, fresh.Plan, kept, unchanged))
            {
                continue;
            }

            string where = change.Row is { } line ? string.Format(CultureInfo.InvariantCulture, "line {0}, ", line) : "";
            warnings.Add($"{where}{change.Target}, {change.Detail}: {Name} left it {Status(change)}, and the new plan does not name it; check it with a lookup");
        }

        Plan plan = fresh.Plan with
        {
            Changes = kept.Select(index => fresh.Plan.Changes[index]).ToList(),
            Unchanged = unchanged,
            Warnings = warnings,
            Resumes = Record,
        };
        return fresh.Select(plan, kept);
    }

    /// <summary>Whether the new plan names a pending change of the record, as a change or as an object already as asked.</summary>
    private static bool Continued(PlanChange pending, Plan fresh, List<int> kept, List<PlanUnchanged> unchanged)
    {
        if (pending.Key is { } ids
            && (kept.Any(index => fresh.Changes[index].Action == pending.Action && string.Equals(fresh.Changes[index].Key, ids, StringComparison.OrdinalIgnoreCase))
                || unchanged.Any(entry => string.Equals(entry.Key, ids, StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        if (pending.Row is { } row)
        {
            return kept.Any(index => fresh.Changes[index].Row == row) || unchanged.Any(entry => entry.Row == row);
        }

        return kept.Any(index => Key(fresh.Changes[index].Action, fresh.Changes[index].Target, fresh.Changes[index].Detail) == Key(pending.Action, pending.Target, pending.Detail))
            || unchanged.Any(entry => entry.Row is null && Key(null, entry.Target, entry.Detail) == Key(null, pending.Target, pending.Detail));
    }

    private static string Status(PlanChange change) => change.Result?.Status switch
    {
        ChangeStatus.Failed => "failed",
        ChangeStatus.Skipped => "skipped",
        _ => "without a result",
    };

    private static string Key(PlanAction? action, string target, string detail) =>
        $"{action?.ToString() ?? "*"}\u001f{target}\u001f{detail}";

    private static string Ids(PlanAction? action, string key) => $"{action?.ToString() ?? "*"}\u001f{key}";

    /// <summary>The command of an operation's stable name: <c>group-members-add</c> is <c>m365-aegis group members add</c>.</summary>
    private static string Words(string operation) => $"m365-aegis {operation.Replace('-', ' ')}";
}
