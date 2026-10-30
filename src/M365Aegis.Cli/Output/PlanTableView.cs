using System.Globalization;
using M365Aegis.Core.Operations;

namespace M365Aegis.Cli.Output;

/// <summary>
/// The table of every write (task 051): a row per change and per object already as asked (by the CSV line when the
/// plan comes from a file), the state now and the state the change makes (an object already as asked has its state
/// under now and <c>-</c> under after), and the result column (planned under a
/// dry run, not sent when refused, applied, failed or skipped after an apply). Under the table: the plan's line, the
/// counts, the warnings, each failed change with the service's message, and what was done.
/// </summary>
internal sealed class PlanTableView : ITableView<Plan>
{
    private static readonly string[] PlainHeaders = ["action", "target", "change", "now", "after", "result"];
    private static readonly string[] RowHeaders = ["line", "action", "target", "change", "now", "after", "result"];

    /// <summary>The one instance.</summary>
    internal static PlanTableView Instance { get; } = new();

    private PlanTableView()
    {
    }

    public IReadOnlyList<string> Headers => PlainHeaders;

    public IReadOnlyList<string> HeadersFor(Plan data) => FromFile(data) ? RowHeaders : PlainHeaders;

    public IEnumerable<IReadOnlyList<string>> Rows(Plan data)
    {
        bool lines = FromFile(data);
        var rows = new List<(int Row, int Order, IReadOnlyList<string> Cells)>();
        int order = 0;
        foreach (PlanChange change in data.Changes)
        {
            rows.Add((change.Row ?? 0, order++, Cells(lines, change.Row, Action(change.Action), change.Target, change.Detail, change.Current, change.Desired, Result(data.Mode, change))));
        }

        foreach (PlanUnchanged unchanged in data.Unchanged)
        {
            rows.Add((unchanged.Row ?? 0, order++, Cells(lines, unchanged.Row, "-", unchanged.Target, unchanged.Detail, unchanged.State, "-", "unchanged")));
        }

        // From a file the table follows the file's lines; from the arguments the changes come first.
        return lines ? rows.OrderBy(row => row.Row).ThenBy(row => row.Order).Select(row => row.Cells) : rows.Select(row => row.Cells);
    }

    public IEnumerable<string> Footer(Plan data)
    {
        yield return $"plan: {data.Title}";
        yield return string.Format(CultureInfo.InvariantCulture, "{0} change(s), {1} already as asked", data.Changes.Count, data.Unchanged.Count);
        if (data.Resumes is { } resumed)
        {
            yield return $"resumes: {Path.GetFileName(resumed)} (its failed and skipped changes, planned again)";
        }

        foreach (string warning in data.Warnings)
        {
            yield return $"warning: {warning}";
        }

        foreach (PlanChange change in data.Changes.Where(change => change.Result?.Status == ChangeStatus.Failed))
        {
            string where = change.Row is { } row ? string.Format(CultureInfo.InvariantCulture, "line {0}, ", row) : "";
            yield return $"failed ({where}{change.Target}, {change.Detail}): {change.Result!.Error?.Message} ({change.Result.Error?.Code})";
        }

        if (data.Mode == PlanMode.Refused)
        {
            // The error line on stderr says it was not sent and what to do.
            yield break;
        }

        yield return data.Mode switch
        {
            PlanMode.DryRun => "dry run: nothing was sent; --yes applies the plan",
            PlanMode.UpToDate => "nothing to change: everything is as asked, nothing was sent",
            PlanMode.Applying => $"the apply did not finish; the plan record is {data.Record}",
            _ => string.Format(
                CultureInfo.InvariantCulture,
                "applied {0} of {1} change(s); the plan record is {2}",
                data.Changes.Count(change => change.Result?.Status == ChangeStatus.Applied),
                data.Changes.Count,
                data.Record),
        };
    }

    private static bool FromFile(Plan data) =>
        data.Changes.Any(change => change.Row is not null) || data.Unchanged.Any(unchanged => unchanged.Row is not null);

    private static IReadOnlyList<string> Cells(bool lines, int? row, string action, string target, string detail, string current, string desired, string result) =>
        lines
            ? [row?.ToString(CultureInfo.InvariantCulture) ?? "-", action, target, detail, current, desired, result]
            : [action, target, detail, current, desired, result];

    private static string Action(PlanAction action) => action switch
    {
        PlanAction.Add => "add",
        PlanAction.Remove => "remove",
        PlanAction.Set => "set",
        PlanAction.Clear => "clear",
        PlanAction.Send => "send",
        _ => "?",
    };

    private static string Result(PlanMode mode, PlanChange change) => mode switch
    {
        PlanMode.DryRun => "planned",
        PlanMode.Refused => "not sent",
        _ => change.Result?.Status switch
        {
            ChangeStatus.Applied => "applied",
            ChangeStatus.Failed => $"failed: {change.Result.Error?.Code}",
            ChangeStatus.Skipped => "skipped",
            _ => "no result",
        },
    };
}
