using System.Globalization;
using M365Aegis.Core.Output;
using Microsoft.Data.Sqlite;

namespace M365Aegis.Core.Snapshots;

/// <summary>Whether a row of a diff appeared or disappeared between the two snapshots.</summary>
public enum SnapshotChange
{
    /// <summary>In the first snapshot, not in the second.</summary>
    Removed,

    /// <summary>In the second snapshot, not in the first.</summary>
    Added,
}

/// <summary>One snapshot of a diff.</summary>
/// <param name="Snapshot">The snapshot's name.</param>
/// <param name="Taken">When it was taken.</param>
/// <param name="AgeHours">Its age when the diff was asked for, in hours.</param>
/// <param name="Pick">How it was named: a snapshot's name, <c>latest</c>, <c>yesterday</c> or a date.</param>
public sealed record SnapshotDiffSide(string Snapshot, DateTimeOffset Taken, double AgeHours, string Pick);

/// <summary>One domain of a diff: what appeared and what disappeared in it.</summary>
/// <param name="Name">The domain, as the manifest names it: <c>members</c>, <c>owners</c>, <c>sharing</c>, …</param>
/// <param name="Compared">Whether both snapshots read it; a warning says why one that was not is left out.</param>
/// <param name="Added">The rows that appeared.</param>
/// <param name="Removed">The rows that disappeared.</param>
public sealed record SnapshotDiffDomain(string Name, bool Compared, int Added, int Removed);

/// <summary>
/// One row that appeared or disappeared: who holds what, and how. The row is the same row in both snapshots when its
/// principals are the same objects (the object id, else the user principal name, else the address), so a renamed
/// display name is no change; the names shown are the snapshot's that holds the row.
/// </summary>
/// <param name="Domain">The domain.</param>
/// <param name="Change">Whether it appeared or disappeared.</param>
/// <param name="What">The group, the shared item (<c>site: /folder/item</c>), the mailbox or the calendar.</param>
/// <param name="Who">The member, the owner, the grantee, the holder or the forwarding target.</param>
/// <param name="How">The relation: <c>member</c>, <c>owner (graph)</c>, a link's scope and roles, a right, the rights.</param>
public sealed record SnapshotDiffEntry(string Domain, SnapshotChange Change, string What, string Who, string How)
{
    /// <summary>The object id of <see cref="What"/>, or null (a shared item, an object without one).</summary>
    public string? WhatId { get; init; }

    /// <summary>The object id of <see cref="Who"/>, or null (a link to everyone, a calendar's <c>Default</c>, an address).</summary>
    public string? WhoId { get; init; }
}

/// <summary>The answer of <c>snapshot diff &lt;a&gt; &lt;b&gt;</c> (task 098): what appeared and disappeared from the first snapshot to the second.</summary>
/// <param name="From">The first snapshot.</param>
/// <param name="To">The second snapshot.</param>
/// <param name="Domains">Each domain with its counts, in the take's order.</param>
/// <param name="Changes">The rows, by domain, then by what and who, a removed row before an added one.</param>
/// <param name="Warnings">A domain left out or partial, snapshots given newest first.</param>
public sealed record SnapshotDiffReport(
    SnapshotDiffSide From,
    SnapshotDiffSide To,
    IReadOnlyList<SnapshotDiffDomain> Domains,
    IReadOnlyList<SnapshotDiffEntry> Changes,
    IReadOnlyList<string> Warnings)
{
    /// <summary>The rows that appeared.</summary>
    public int Added => Changes.Count(change => change.Change == SnapshotChange.Added);

    /// <summary>The rows that disappeared.</summary>
    public int Removed => Changes.Count(change => change.Change == SnapshotChange.Removed);
}

/// <summary>
/// <c>snapshot diff &lt;a&gt; &lt;b&gt;</c> (task 098): who gained or lost access between two snapshots of a profile.
/// D-011's comparison: the first file opened read-only and the second attached to it, one statement per domain that
/// keys each row on its principals and keeps the rows one side has and the other has not. No request to any service.
/// </summary>
public static class SnapshotDiff
{
    /// <summary>The pick of the newest snapshot.</summary>
    public const string Latest = "latest";

    /// <summary>The pick of the newest snapshot taken before today (UTC).</summary>
    public const string Yesterday = "yesterday";

    private const string PickHint = "give a snapshot's name as `m365-aegis snapshot list` shows it, `latest`, `yesterday` or a date (yyyy-MM-dd, UTC)";

    /// <summary>
    /// The domains compared, in the take's order: the name, the manifest's domains the rows rest on, and the statement
    /// of the rows of one snapshot (its schema's name in, the key, what, its id, who, its id and how out).
    /// </summary>
    private static readonly (string Name, string[] RestsOn, Func<string, string> Rows)[] Domains =
    [
        ("members", ["groups", "members"], schema => $"""
            select distinct {Id("g")} || '|' || {Id("m")} as k, coalesce({Name("g")}, '?') as what, g.objectId as whatId,
                coalesce({Name("m")}, '?') as who, m.objectId as whoId,
                case when m.kind = 'user' then 'member' else 'member (' || m.kind || ')' end as how
            from {schema}.members e join {schema}.objects g on g.key = e.groupKey join {schema}.objects m on m.key = e.memberKey
            """),
        ("owners", ["groups", "owners"], schema => $"""
            select distinct {Id("g")} || '|' || {Id("o")} || '|' || e.source as k, coalesce({Name("g")}, '?') as what, g.objectId as whatId,
                coalesce({Name("o")}, '?') as who, o.objectId as whoId, 'owner (' || e.source || ')' as how
            from {schema}.owners e join {schema}.objects g on g.key = e.groupKey join {schema}.objects o on o.key = e.ownerKey
            """),
        ("sharing", ["sites", "sharing"], schema => $"""
            select distinct
                s.siteId || '|' || l.itemId || '|' || l.kind || '|' || coalesce(l.scope, '') || '|' || coalesce(l.type, '') || '|'
                    || coalesce(l.roles, '') || '|' || coalesce(l.expires, '') || '|' || coalesce(l.hasPassword, '') || '|' || coalesce({Id("p")}, '') as k,
                coalesce(s.name, s.siteId) || ': ' || rtrim(coalesce(l.itemPath, ''), '/') || '/' || coalesce(l.itemName, l.itemId) as what, null as whatId,
                coalesce({Name("p")}, case l.scope when 'organization' then 'everyone in the organization' when 'anonymous' then 'anyone with the link' else '-' end) as who,
                p.objectId as whoId,
                l.kind || coalesce(' ' || l.scope, '') || coalesce(' ' || l.roles, '') || coalesce(' until ' || l.expires, '')
                    || case when l.hasPassword = 1 then ' with a password' else '' end as how
            from {schema}.links l join {schema}.sites s on s.key = l.siteKey
                left join {schema}.linkGrantees lg on lg.linkKey = l.key left join {schema}.objects p on p.key = lg.principalKey
            """),
        ("grants", ["grants"], schema => $"""
            select distinct {Id("m")} || '|' || g.right || '|' || {Id("h")} as k, {Mailbox("m")} as what, m.objectId as whatId,
                coalesce({Name("h")}, '?') as who, h.objectId as whoId, g.right as how
            from {schema}.grants g join {schema}.objects m on m.key = g.mailboxKey join {schema}.objects h on h.key = g.holderKey
            """),
        ("folderPermissions", ["mailboxes", "folderPermissions"], schema => $"""
            select distinct {Id("m")} || '|' || lower(f.folder) || '|' || coalesce({Id("p")}, 'name:' || lower(f.principalName)) || '|' || f.rights as k,
                {Mailbox("m")} || ': ' || f.folder as what, m.objectId as whatId,
                coalesce({Name("p")}, f.principalName) as who, p.objectId as whoId, f.rights as how
            from {schema}.folderPermissions f join {schema}.objects m on m.key = f.mailboxKey left join {schema}.objects p on p.key = f.principalKey
            """),
        ("forwarding", ["forwarding"], schema => $"""
            select distinct {Id("m")} || '|' || f.kind || '|' || coalesce({Id("t")}, lower(f.targetAddress), lower(f.target)) || '|' || f.keepsCopy as k,
                {Mailbox("m")} as what, m.objectId as whatId, coalesce({Name("t")}, f.targetAddress, f.target) as who, t.objectId as whoId,
                f.kind || case when f.external = 1 then ', external' else '' end || case when f.keepsCopy = 1 then ', keeps a copy' else '' end as how
            from {schema}.forwarding f join {schema}.objects m on m.key = f.mailboxKey left join {schema}.objects t on t.key = f.targetKey
            """),
    ];

    /// <summary>The diff of two snapshots of a profile, each named by a pick (<see cref="Resolve"/>).</summary>
    /// <exception cref="AegisException">A usage error for a pick that is none; not found when a pick finds no snapshot.</exception>
    public static SnapshotDiffReport Compare(string home, string profile, string from, string to, DateTimeOffset now)
    {
        (string fromFile, SnapshotDetail fromDetail) = Resolve(home, profile, from, now);
        (string toFile, SnapshotDetail toDetail) = Resolve(home, profile, to, now);
        var warnings = new List<string>();
        if (fromDetail.Snapshot.Taken > toDetail.Snapshot.Taken)
        {
            warnings.Add($"the first snapshot, {fromDetail.Snapshot.Name}, is newer than the second: what appeared since is listed as removed");
        }

        var domains = new List<SnapshotDiffDomain>();
        var changes = new List<SnapshotDiffEntry>();
        using SqliteConnection connection = SnapshotStore.OpenRead(fromFile)!;
        using (SqliteCommand attach = connection.CreateCommand())
        {
            attach.CommandText = "attach database $file as other";
            attach.Parameters.AddWithValue("$file", toFile);
            attach.ExecuteNonQuery();
        }

        foreach ((string name, string[] restsOn, Func<string, string> rows) in Domains)
        {
            List<string> unread = [.. Unread(fromDetail, restsOn), .. Unread(toDetail, restsOn)];
            warnings.AddRange(Partial(fromDetail, restsOn, name).Concat(Partial(toDetail, restsOn, name)));
            if (unread.Count > 0)
            {
                warnings.Add($"{name} is left out: {string.Join("; ", unread.Distinct(StringComparer.Ordinal))}");
                domains.Add(new SnapshotDiffDomain(name, false, 0, 0));
                continue;
            }

            List<SnapshotDiffEntry> found = Rows(connection, name, rows);
            changes.AddRange(found);
            domains.Add(new SnapshotDiffDomain(name, true, found.Count(entry => entry.Change == SnapshotChange.Added), found.Count(entry => entry.Change == SnapshotChange.Removed)));
        }

        return new SnapshotDiffReport(
            Side(fromDetail, from, now),
            Side(toDetail, to, now),
            domains,
            changes,
            [.. warnings.Distinct(StringComparer.Ordinal)]);
    }

    /// <summary>
    /// The snapshot a pick names: <c>latest</c>, the newest readable one; <c>yesterday</c> or a date (<c>yyyy-MM-dd</c>),
    /// the newest readable one taken on that UTC day or before it, as the tenant stood at the day's end; else a
    /// snapshot's name as <c>snapshot list</c> shows it, with or without <c>.sqlite</c>.
    /// </summary>
    /// <exception cref="AegisException">A usage error for a pick that is none; not found when it finds no snapshot.</exception>
    internal static (string File, SnapshotDetail Detail) Resolve(string home, string profile, string pick, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pick);
        string text = pick.Trim();
        if (text.Equals(Latest, StringComparison.OrdinalIgnoreCase))
        {
            return SnapshotStore.Newest(home, profile);
        }

        string folder = SnapshotStore.Folder(home, profile);
        DateOnly? day = text.Equals(Yesterday, StringComparison.OrdinalIgnoreCase)
            ? DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1)
            : DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date) ? date : null;
        if (day is { } until)
        {
            var end = new DateTimeOffset(until.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            foreach (string file in SnapshotStore.Files(folder))
            {
                if (SnapshotStore.TakenOf(Path.GetFileName(file))!.Value < end && SnapshotStore.TryRead(file) is { } detail)
                {
                    return (file, detail);
                }
            }

            throw new AegisException(
                FailureKind.NotFound,
                string.Create(CultureInfo.InvariantCulture, $"no snapshot of the profile '{profile}' taken on {until:yyyy-MM-dd} (UTC) or before"),
                "`m365-aegis snapshot list` shows the snapshots by name");
        }

        string name = text.EndsWith(SnapshotStore.Extension, StringComparison.Ordinal) ? text : text + SnapshotStore.Extension;
        if (SnapshotStore.TakenOf(name) is null)
        {
            throw new AegisException(FailureKind.Usage, $"'{text}' names no snapshot", PickHint);
        }

        SnapshotDetail named = SnapshotStore.Show(home, profile, name);
        return (Path.Combine(folder, named.Snapshot.Name), named);
    }

    /// <summary>The rows of one domain that one snapshot has and the other has not, removed before added.</summary>
    private static List<SnapshotDiffEntry> Rows(SqliteConnection connection, string domain, Func<string, string> rows)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            with a as ({rows("main")}), b as ({rows("other")})
            select 0, what, whatId, who, whoId, how from a where k not in (select k from b)
            union all
            select 1, what, whatId, who, whoId, how from b where k not in (select k from a)
            order by 2 collate nocase, 4 collate nocase, 1, 6
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        var found = new List<SnapshotDiffEntry>();
        while (reader.Read())
        {
            found.Add(new SnapshotDiffEntry(domain, (SnapshotChange)reader.GetInt32(0), reader.GetString(1), reader.GetString(3), reader.GetString(5))
            {
                WhatId = reader.IsDBNull(2) ? null : reader.GetString(2),
                WhoId = reader.IsDBNull(4) ? null : reader.GetString(4),
            });
        }

        return found;
    }

    /// <summary>Why a snapshot cannot speak for a domain: one it rests on failed, was skipped or is not in its manifest.</summary>
    private static IEnumerable<string> Unread(SnapshotDetail detail, string[] restsOn) =>
        restsOn.Select(name => (Name: name, Domain: detail.Domains.FirstOrDefault(domain => domain.Name == name)))
            .Where(pair => pair.Domain is null || pair.Domain.Status is SnapshotDomainStatus.Failed or SnapshotDomainStatus.Skipped)
            .Select(pair => $"{pair.Name} was not read in {detail.Snapshot.Name}");

    /// <summary>A domain read in part: a change among the items it could not read shows as removed or added.</summary>
    private static IEnumerable<string> Partial(SnapshotDetail detail, string[] restsOn, string domain) =>
        detail.Domains
            .Where(read => read.Status == SnapshotDomainStatus.Partial && restsOn.Contains(read.Name))
            .Select(read => string.Create(
                CultureInfo.InvariantCulture,
                $"{read.Name} is partial in {detail.Snapshot.Name} ({read.ItemsFailed} item(s) not read): the {domain} rows of those items may show as removed or added"));

    private static SnapshotDiffSide Side(SnapshotDetail detail, string pick, DateTimeOffset now) =>
        new(detail.Snapshot.Name, detail.Snapshot.Taken, Math.Round((now - detail.Snapshot.Taken).TotalHours, 1), pick.Trim());

    /// <summary>
    /// What makes an object the same object in two snapshots (the builder's identity): its object id, else its user
    /// principal name, else its kind and its address or name; never its display name alone when it has an id.
    /// </summary>
    private static string Id(string alias) =>
        $"coalesce({alias}.objectId, 'upn:' || lower({alias}.userPrincipalName), {alias}.kind || ':' || lower(coalesce({alias}.mail, {alias}.name, '')))";

    /// <summary>How a principal is shown: a group by its name, anyone else by the user principal name or the address.</summary>
    private static string Name(string alias) =>
        $"case when {alias}.kind = 'group' then coalesce({alias}.name, {alias}.mail, {alias}.objectId) else coalesce({alias}.userPrincipalName, {alias}.mail, {alias}.name, {alias}.objectId) end";

    /// <summary>How a mailbox is shown: by its address.</summary>
    private static string Mailbox(string alias) =>
        $"coalesce({alias}.mail, {alias}.userPrincipalName, {alias}.name, {alias}.objectId, '?')";
}
