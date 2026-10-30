using System.CommandLine;
using System.Globalization;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Config;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli.Commands;

/// <summary>
/// <c>m365-aegis plan list</c> and <c>plan show &lt;record&gt;</c> (task 060): the plan records an apply writes under the
/// home folder's <c>plans</c> folder (task 051), read back. Both read the local files only: no sign-in, no request,
/// <c>local</c> as the backend. A record that does not exist is exit code 4.
/// </summary>
internal static class PlanCommand
{
    internal static Command Build(Option<bool> json, Option<bool> table)
    {
        Command plan = new("plan", "Read the plan records of the applies: what each one changed, with each change's result.");

        Command list = new("list", "List the plan records of the home folder, newest first: when, the write, the changes applied and failed.");
        list.SetAction(parsed => CommandRunner.Run(
            Program.ResolveMode(parsed, json, table),
            Backend.Local,
            () => CommandResult.Success(PlanStore.List(AegisHome.Resolve())),
            PlanListTableView.Instance));

        Argument<string> record = new("record") { Description = "The record's name as `plan list` shows it (with or without .json), or the path to its file." };
        Command show = new("show", "Show one plan record as its plan, each change with its result.");
        show.Arguments.Add(record);
        show.SetAction(parsed => CommandRunner.Run(
            Program.ResolveMode(parsed, json, table),
            Backend.Local,
            () => CommandResult.Success(PlanStore.Open(AegisHome.Resolve(), parsed.GetValue(record) ?? "").Record),
            PlanRecordTableView.Instance));

        plan.Subcommands.Add(list);
        plan.Subcommands.Add(show);
        return plan;
    }

    /// <summary>A moment as the tables write it: UTC, to the second.</summary>
    internal static string When(DateTimeOffset? moment) =>
        moment is { } value ? value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "-";
}

/// <summary>The table of <c>plan list</c>: a row per record, newest first; the folder under the table.</summary>
internal sealed class PlanListTableView : ITableView<PlanRecordList>
{
    internal static PlanListTableView Instance { get; } = new();

    private PlanListTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["started (UTC)", "operation", "mode", "changes", "applied", "failed", "skipped", "record"];

    public IEnumerable<IReadOnlyList<string>> Rows(PlanRecordList data) => data.Records.Select(record => (IReadOnlyList<string>)
    [
        PlanCommand.When(record.Started),
        record.Operation,
        record.Mode == PlanMode.Applying ? "cut short" : Mode(record.Mode),
        Count(record.Changes),
        Count(record.Applied),
        Count(record.Failed),
        Count(record.Skipped),
        record.Name,
    ]);

    public IEnumerable<string> Footer(PlanRecordList data)
    {
        yield return string.Format(CultureInfo.InvariantCulture, "{0} plan record(s) under {1}, newest first", data.Records.Count, data.Folder);
        if (data.Unreadable > 0)
        {
            yield return string.Format(CultureInfo.InvariantCulture, "{0} file(s) there are not plan records and are left out", data.Unreadable);
        }

        if (data.Records.Count > 0)
        {
            yield return "`m365-aegis plan show <record>` shows one; `--resume <record>` on its write continues the failed and skipped changes";
        }
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Mode(PlanMode mode) => mode switch
    {
        PlanMode.Applied => "applied",
        PlanMode.DryRun => "dry run",
        PlanMode.Refused => "refused",
        PlanMode.UpToDate => "up to date",
        _ => "?",
    };
}

/// <summary>
/// The table of <c>plan show</c>: the record's plan as every write renders it (<see cref="PlanTableView"/>), each change
/// with its result, and under it who applied it, with which profile and arguments.
/// </summary>
internal sealed class PlanRecordTableView : ITableView<PlanRecord>
{
    internal static PlanRecordTableView Instance { get; } = new();

    private PlanRecordTableView()
    {
    }

    public IReadOnlyList<string> Headers => PlanTableView.Instance.Headers;

    public IReadOnlyList<string> HeadersFor(PlanRecord data) => PlanTableView.Instance.HeadersFor(data.Plan);

    public IEnumerable<IReadOnlyList<string>> Rows(PlanRecord data) => PlanTableView.Instance.Rows(data.Plan);

    public IEnumerable<string> Footer(PlanRecord data)
    {
        foreach (string line in PlanTableView.Instance.Footer(data.Plan))
        {
            yield return line;
        }

        yield return string.Format(
            CultureInfo.InvariantCulture,
            "{0}: started {1}, finished {2}, profile {3}",
            data.Source,
            PlanCommand.When(data.Plan.Started),
            PlanCommand.When(data.Plan.Finished),
            data.Profile ?? "(the default one)");
        if (data.Arguments.Count > 0)
        {
            yield return $"arguments: {string.Join(" ", data.Arguments)}";
        }

        if (data.Input is { } input)
        {
            yield return $"from the file {input.File} (kept in the record)";
        }
    }
}
