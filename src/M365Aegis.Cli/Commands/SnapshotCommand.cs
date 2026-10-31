using System.CommandLine;
using System.Globalization;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Config;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Output;
using M365Aegis.Core.Snapshots;

namespace M365Aegis.Cli.Commands;

/// <summary>
/// <c>m365-aegis snapshot take|list|show</c> (task 087, D-011): the snapshot store under the home folder.
/// <c>take</c> reads every domain the access views need over the Graph client, the Admin API and the sidecar, with the
/// silent sign-in only, and writes one SQLite file under <c>snapshots\&lt;profile&gt;\</c>, then prunes by the retention;
/// <c>list</c> and <c>show</c> read the files only (<c>local</c>).
/// </summary>
internal static class SnapshotCommand
{
    internal static Command Build(Option<bool> json, Option<bool> table, Option<string?> profile)
    {
        Command snapshot = new("snapshot", "Take and read the snapshots of the tenant that the access view answers from.");

        Command take = new("take", "Read the groups, sites, sharing and mailbox permissions into a new snapshot, then prune the older ones.");
        take.SetAction((parsed, cancellationToken) => ExchangeRunner.RunAsync(
            parsed, json, table, profile,
            (ExchangeBackends backends, CancellationToken ct) =>
            {
                string home = AegisHome.Resolve();
                TenantProfile tenant = new ProfileStore(home).Resolve(parsed.GetValue(profile));
                return SnapshotTake.RunAsync(backends, new SnapshotTakeOptions(home, tenant), ct);
            },
            SnapshotTakeTableView.Instance,
            cancellationToken));

        Command list = new("list", "List the snapshots of the profile, newest first.");
        list.SetAction(parsed => CommandRunner.Run(
            Program.ResolveMode(parsed, json, table),
            Backend.Local,
            () =>
            {
                string home = AegisHome.Resolve();
                return CommandResult.Success(SnapshotStore.List(home, SnapshotStore.ProfileName(home, parsed.GetValue(profile))));
            },
            SnapshotListTableView.Instance));

        Argument<string?> name = new("snapshot")
        {
            Description = "The snapshot's name as `snapshot list` shows it (with or without .sqlite); the newest when omitted.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        Command show = new("show", "Show a snapshot's manifest: when, which read answered each domain, what failed, the rows.");
        show.Arguments.Add(name);
        show.SetAction(parsed => CommandRunner.Run(
            Program.ResolveMode(parsed, json, table),
            Backend.Local,
            () =>
            {
                string home = AegisHome.Resolve();
                return CommandResult.Success(SnapshotStore.Show(home, SnapshotStore.ProfileName(home, parsed.GetValue(profile)), parsed.GetValue(name)));
            },
            SnapshotDetailTableView.Instance));

        snapshot.Subcommands.Add(take);
        snapshot.Subcommands.Add(list);
        snapshot.Subcommands.Add(show);
        return snapshot;
    }

    /// <summary>A domain's row: its name, how it was read, its rows, and its note.</summary>
    internal static IReadOnlyList<string> DomainRow(SnapshotDomain domain) =>
    [
        domain.Name,
        domain.Status.ToString().ToLowerInvariant(),
        domain.Rows.ToString(CultureInfo.InvariantCulture),
        domain.Note ?? "",
    ];
}

/// <summary>The table of <c>snapshot take</c>: a row per domain; the file, the prune and the warnings under it.</summary>
internal sealed class SnapshotTakeTableView : ITableView<SnapshotTakeResult>
{
    internal static SnapshotTakeTableView Instance { get; } = new();

    private SnapshotTakeTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["domain", "status", "rows", "note"];

    public IEnumerable<IReadOnlyList<string>> Rows(SnapshotTakeResult data) => data.Domains.Select(SnapshotCommand.DomainRow);

    public IEnumerable<string> Footer(SnapshotTakeResult data)
    {
        yield return string.Format(
            CultureInfo.InvariantCulture,
            "snapshot {0} ({1}) in {2}, read in {3:0.0} s",
            data.Snapshot.Name,
            MailboxCommand.Size(data.Snapshot.Bytes),
            Path.GetDirectoryName(data.Path),
            data.ElapsedMs / 1000.0);
        yield return data.Pruned.Count > 0
            ? $"pruned {data.Pruned.Count} by the retention ({data.Retention.KeepDaily} days, {data.Retention.KeepWeekly} weeks): {string.Join(", ", data.Pruned)}"
            : data.Snapshot.Failed.Count > 0
                ? "nothing pruned: a domain failed, so the older snapshots are kept"
                : string.Format(CultureInfo.InvariantCulture, "nothing to prune ({0} days, {1} weeks kept)", data.Retention.KeepDaily, data.Retention.KeepWeekly);
        foreach (string partial in data.RemovedPartials)
        {
            yield return $"removed {partial}, left by a take that did not finish";
        }

        foreach (string warning in data.Warnings)
        {
            yield return $"warning: {warning}";
        }
    }
}

/// <summary>The table of <c>snapshot list</c>: a row per snapshot, newest first; the folder and the total under it.</summary>
internal sealed class SnapshotListTableView : ITableView<SnapshotList>
{
    internal static SnapshotListTableView Instance { get; } = new();

    private SnapshotListTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["taken (UTC)", "snapshot", "size", "failed domains"];

    public IEnumerable<IReadOnlyList<string>> Rows(SnapshotList data) => data.Snapshots.Select(snapshot => (IReadOnlyList<string>)
    [
        PlanCommand.When(snapshot.Taken),
        snapshot.Name,
        MailboxCommand.Size(snapshot.Bytes),
        snapshot.Failed.Count > 0 ? string.Join(", ", snapshot.Failed) : "-",
    ]);

    public IEnumerable<string> Footer(SnapshotList data)
    {
        yield return string.Format(
            CultureInfo.InvariantCulture,
            "{0} snapshot(s) of the profile {1} under {2}, {3} on disk",
            data.Snapshots.Count,
            data.Profile,
            data.Folder,
            MailboxCommand.Size(data.Bytes));
        if (data.Unreadable > 0)
        {
            yield return string.Format(CultureInfo.InvariantCulture, "{0} file(s) named like a snapshot cannot be read as one and are left out", data.Unreadable);
        }

        yield return data.Snapshots.Count > 0
            ? "`m365-aegis snapshot show <snapshot>` shows one; `m365-aegis access <user>` answers from the newest"
            : SnapshotStore.TakeHint;
    }
}

/// <summary>The table of <c>snapshot show</c>: a row per domain with its read; the manifest and the tables under it.</summary>
internal sealed class SnapshotDetailTableView : ITableView<SnapshotDetail>
{
    internal static SnapshotDetailTableView Instance { get; } = new();

    private SnapshotDetailTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["domain", "status", "rows", "read", "note"];

    public IEnumerable<IReadOnlyList<string>> Rows(SnapshotDetail data) => data.Domains.Select(domain =>
    {
        IReadOnlyList<string> row = SnapshotCommand.DomainRow(domain);
        return (IReadOnlyList<string>)[row[0], row[1], row[2], domain.Read, row[3]];
    });

    public IEnumerable<string> Footer(SnapshotDetail data)
    {
        yield return string.Format(
            CultureInfo.InvariantCulture,
            "snapshot {0} ({1}) of the profile {2} ({3}), taken {4} UTC in {5:0.0} s by version {6}, schema {7}",
            data.Snapshot.Name,
            MailboxCommand.Size(data.Snapshot.Bytes),
            data.Profile,
            data.Tenant,
            PlanCommand.When(data.Snapshot.Taken),
            data.ElapsedMs / 1000.0,
            data.ToolVersion,
            data.SchemaVersion);
        yield return "rows: " + string.Join(", ", data.Tables.Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key} {pair.Value}")));
    }
}
