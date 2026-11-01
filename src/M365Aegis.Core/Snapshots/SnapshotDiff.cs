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
/// <param name="Name">
/// The domain, as the manifest names it: <c>members</c>, <c>owners</c>, <c>sharing</c>, …; for one user's diff
/// (task 114) the access view's section: <c>groups</c>, <c>owners</c>, <c>sites</c>, <c>sharedItems</c>,
/// <c>mailboxes</c>, <c>calendars</c>, <c>forwarding</c>.
/// </param>
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

    /// <summary>
    /// In one user's diff (task 114), the group the user's row comes through: the member group of a nested membership,
    /// the group a right or a share is granted to, the group behind a site; null for a row of the user's own.
    /// </summary>
    public string? Through { get; init; }
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

    /// <summary>The user of <c>snapshot diff --user</c> (task 114), as the second snapshot knows them, else the first; null for the whole tenant.</summary>
    public AccessUser? User { get; init; }
}

/// <summary>
/// <c>snapshot diff &lt;a&gt; &lt;b&gt;</c> (task 098): who gained or lost access between two snapshots of a profile.
/// D-011's comparison: the first file opened read-only and the second attached to it, one statement per domain that
/// keys each row on its principals and keeps the rows one side has and the other has not. No request to any service.
/// With a user (task 114), the statements are the access view's sections of that one user in each snapshot, the
/// nested memberships and what comes through them included, compared the same way.
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
                case when m.kind = 'user' then 'member' else 'member (' || m.kind || ')' end as how, null as through
            from {schema}.members e join {schema}.objects g on g.key = e.groupKey join {schema}.objects m on m.key = e.memberKey
            """),
        ("owners", ["groups", "owners"], schema => $"""
            select distinct {Id("g")} || '|' || {Id("o")} || '|' || e.source as k, coalesce({Name("g")}, '?') as what, g.objectId as whatId,
                coalesce({Name("o")}, '?') as who, o.objectId as whoId, 'owner (' || e.source || ')' as how, null as through
            from {schema}.owners e join {schema}.objects g on g.key = e.groupKey join {schema}.objects o on o.key = e.ownerKey
            """),
        ("sharing", ["sites", "sharing"], schema => $"""
            select distinct {SharingKey} as k, {SharingWhat} as what, null as whatId,
                coalesce({Name("p")}, case l.scope when 'organization' then 'everyone in the organization' when 'anonymous' then 'anyone with the link' else '-' end) as who,
                p.objectId as whoId, {SharingHow} as how, null as through
            from {schema}.links l join {schema}.sites s on s.key = l.siteKey
                left join {schema}.linkGrantees lg on lg.linkKey = l.key left join {schema}.objects p on p.key = lg.principalKey
            """),
        ("grants", ["grants"], schema => $"""
            select distinct {Id("m")} || '|' || g.right || '|' || {Id("h")} as k, {Mailbox("m")} as what, m.objectId as whatId,
                coalesce({Name("h")}, '?') as who, h.objectId as whoId, g.right as how, null as through
            from {schema}.grants g join {schema}.objects m on m.key = g.mailboxKey join {schema}.objects h on h.key = g.holderKey
            """),
        ("folderPermissions", ["mailboxes", "folderPermissions"], schema => $"""
            select distinct {Id("m")} || '|' || lower(f.folder) || '|' || coalesce({Id("p")}, 'name:' || lower(f.principalName)) || '|' || f.rights as k,
                {Mailbox("m")} || ': ' || f.folder as what, m.objectId as whatId,
                coalesce({Name("p")}, f.principalName) as who, p.objectId as whoId, f.rights as how, null as through
            from {schema}.folderPermissions f join {schema}.objects m on m.key = f.mailboxKey left join {schema}.objects p on p.key = f.principalKey
            """),
        ("forwarding", ["forwarding"], schema => $"""
            select distinct {ForwardingKey} as k, {ForwardingRow} as how, null as through
            from {schema}.forwarding f join {schema}.objects m on m.key = f.mailboxKey left join {schema}.objects t on t.key = f.targetKey
            """),
    ];

    /// <summary>
    /// The sections of one user's diff (task 114), the access view's (task 084) and the user's ownerships and forwarding:
    /// the statement of the user's rows in one snapshot, the user's key in it the parameter <see cref="UserKey"/> names.
    /// A row held by one of the user's groups (direct or nested, <c>userGroups</c>) is the user's too, keyed on that
    /// group, so a membership gained through a new group shows with what comes through it.
    /// </summary>
    private static readonly (string Name, string[] RestsOn, Func<string, string> Rows)[] UserSections =
    [
        ("groups", ["groups", "userGroups"], schema => $"""
            select distinct {Id("g")} || '|' || coalesce({Id("v")}, '') as k, coalesce({Name("g")}, '?') as what, g.objectId as whatId,
                coalesce({Name("u")}, '?') as who, u.objectId as whoId, 'member' as how, {Name("v")} as through
            from {schema}.userGroups ug join {schema}.objects g on g.key = ug.groupKey join {schema}.objects u on u.key = ug.userKey
                left join {schema}.objects v on v.key = ug.viaKey
            where ug.userKey = {UserKey(schema)}
            """),
        ("owners", ["groups", "owners"], schema => $"""
            select distinct {Id("g")} || '|' || e.source as k, coalesce({Name("g")}, '?') as what, g.objectId as whatId,
                coalesce({Name("o")}, '?') as who, o.objectId as whoId, 'owner (' || e.source || ')' as how, null as through
            from {schema}.owners e join {schema}.objects g on g.key = e.groupKey join {schema}.objects o on o.key = e.ownerKey
            where e.ownerKey = {UserKey(schema)}
            """),
        ("sites", ["sites", "userGroups", "owners"], schema => $"""
            select s.siteId || '|' || r.role as k, coalesce(s.name, s.siteId) as what, null as whatId,
                coalesce({Name("u")}, '?') as who, u.objectId as whoId, r.role as how, {Name("g")} as through
            from (select groupKey, 'member' as role from {schema}.userGroups where userKey = {UserKey(schema)}
                    union select groupKey, 'owner' from {schema}.owners where ownerKey = {UserKey(schema)}) r
                join {schema}.sites s on s.groupKey = r.groupKey join {schema}.objects g on g.key = s.groupKey
                join {schema}.objects u on u.key = {UserKey(schema)}
            """),
        ("sharedItems", ["sites", "sharing", "userGroups"], schema => $"""
            select distinct {SharingKey} as k, {SharingWhat} as what, null as whatId, coalesce({Name("p")}, '?') as who, p.objectId as whoId,
                {SharingHow} as how, {Through("p", schema)} as through
            from {schema}.linkGrantees lg join {schema}.links l on l.key = lg.linkKey join {schema}.sites s on s.key = l.siteKey
                join {schema}.objects p on p.key = lg.principalKey
            where lg.principalKey = {UserKey(schema)} or lg.principalKey in ({GroupsOfUser(schema)})
            """),
        ("mailboxes", ["grants", "userGroups"], schema => $"""
            select distinct {Id("m")} || '|' || g.right || '|' || {Id("h")} as k, {Mailbox("m")} as what, m.objectId as whatId,
                coalesce({Name("h")}, '?') as who, h.objectId as whoId, g.right as how, {Through("h", schema)} as through
            from {schema}.grants g join {schema}.objects m on m.key = g.mailboxKey join {schema}.objects h on h.key = g.holderKey
            where g.holderKey = {UserKey(schema)} or g.holderKey in ({GroupsOfUser(schema)})
            """),
        ("calendars", ["mailboxes", "folderPermissions", "userGroups"], schema => $"""
            select distinct {Id("m")} || '|' || lower(f.folder) || '|' || {Id("p")} || '|' || f.rights as k,
                {Mailbox("m")} || ': ' || f.folder as what, m.objectId as whatId,
                coalesce({Name("p")}, f.principalName) as who, p.objectId as whoId, f.rights as how, {Through("p", schema)} as through
            from {schema}.folderPermissions f join {schema}.objects m on m.key = f.mailboxKey join {schema}.objects p on p.key = f.principalKey
            where f.principalKey = {UserKey(schema)} or f.principalKey in ({GroupsOfUser(schema)})
            """),
        ("forwarding", ["forwarding"], schema => $"""
            select distinct {ForwardingKey} as k, {ForwardingRow} as how, null as through
            from {schema}.forwarding f join {schema}.objects m on m.key = f.mailboxKey left join {schema}.objects t on t.key = f.targetKey
            where f.mailboxKey = {UserKey(schema)} or f.targetKey = {UserKey(schema)}
            """),
    ];

    /// <summary>A shared item's grant as one key: the site, the item, the link or permission with its scope, type, roles, expiry and password, the grantee.</summary>
    private static string SharingKey =>
        $"""
        s.siteId || '|' || l.itemId || '|' || l.kind || '|' || coalesce(l.scope, '') || '|' || coalesce(l.type, '') || '|'
            || coalesce(l.roles, '') || '|' || coalesce(l.expires, '') || '|' || coalesce(l.hasPassword, '') || '|' || coalesce({Id("p")}, '')
        """;

    /// <summary>A shared item as the diff shows it: <c>site: /folder/item</c>.</summary>
    private const string SharingWhat = "coalesce(s.name, s.siteId) || ': ' || rtrim(coalesce(l.itemPath, ''), '/') || '/' || coalesce(l.itemName, l.itemId)";

    /// <summary>A link or a permission as the diff shows it: its kind, scope, roles, expiry and password.</summary>
    private const string SharingHow = """
        l.kind || coalesce(' ' || l.scope, '') || coalesce(' ' || l.roles, '') || coalesce(' until ' || l.expires, '')
            || case when l.hasPassword = 1 then ' with a password' else '' end
        """;

    /// <summary>A forwarding as one key: the mailbox, the kind, the target, whether the mailbox keeps a copy.</summary>
    private static string ForwardingKey =>
        $"{Id("m")} || '|' || f.kind || '|' || coalesce({Id("t")}, lower(f.targetAddress), lower(f.target)) || '|' || f.keepsCopy";

    /// <summary>A forwarding's columns from what to how: the mailbox, the target, the kind, external, a copy kept.</summary>
    private static string ForwardingRow =>
        $"""
        {Mailbox("m")} as what, m.objectId as whatId, coalesce({Name("t")}, f.targetAddress, f.target) as who, t.objectId as whoId,
            f.kind || case when f.external = 1 then ', external' else '' end || case when f.keepsCopy = 1 then ', keeps a copy' else '' end
        """;

    /// <summary>
    /// The diff of two snapshots of a profile, each named by a pick (<see cref="Resolve"/>); with a user (task 114),
    /// that user's diff in the access view's sections, the user found in each snapshot (<see cref="FindInBoth"/>).
    /// </summary>
    /// <exception cref="AegisException">
    /// A usage error for a pick that is none or a blank user; not found when a pick finds no snapshot, or neither
    /// snapshot knows the user.
    /// </exception>
    public static SnapshotDiffReport Compare(string home, string profile, string from, string to, DateTimeOffset now, string? user = null)
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

        var sections = Domains;
        var parameters = new Dictionary<string, long>(StringComparer.Ordinal);
        AccessUser? found = null;
        if (user is not null)
        {
            (long? first, long? second, found) = FindInBoth(connection, user, fromDetail.Snapshot.Name, toDetail.Snapshot.Name);
            parameters[UserKey("main")] = first ?? -1;
            parameters[UserKey("other")] = second ?? -1;
            if (first is null)
            {
                warnings.Add($"the user '{user.Trim()}' is not in {fromDetail.Snapshot.Name}: what they reach in {toDetail.Snapshot.Name} is listed as added");
            }

            if (second is null)
            {
                warnings.Add($"the user '{user.Trim()}' is not in {toDetail.Snapshot.Name}: what they reached in {fromDetail.Snapshot.Name} is listed as removed");
            }

            sections = UserSections;
        }

        // One warning per snapshot and partial domain, at its first place, naming every section that rests on it.
        var partials = new Dictionary<(string Snapshot, string Domain), (int At, SnapshotDomain Read, List<string> Sections)>();
        foreach ((string name, string[] restsOn, Func<string, string> rows) in sections)
        {
            List<string> unread = [.. Unread(fromDetail, restsOn), .. Unread(toDetail, restsOn)];
            foreach ((SnapshotDetail detail, SnapshotDomain read) in Partial(fromDetail, restsOn).Concat(Partial(toDetail, restsOn)))
            {
                if (!partials.TryGetValue((detail.Snapshot.Name, read.Name), out var partial))
                {
                    partial = (warnings.Count, read, []);
                    partials[(detail.Snapshot.Name, read.Name)] = partial;
                    warnings.Add("");
                }

                if (!partial.Sections.Contains(name))
                {
                    // A snapshot compared with itself names the section once.
                    partial.Sections.Add(name);
                }
            }

            if (unread.Count > 0)
            {
                warnings.Add($"{name} is left out: {string.Join("; ", unread.Distinct(StringComparer.Ordinal))}");
                domains.Add(new SnapshotDiffDomain(name, false, 0, 0));
                continue;
            }

            List<SnapshotDiffEntry> rowsFound = Rows(connection, name, rows, parameters);
            changes.AddRange(rowsFound);
            domains.Add(new SnapshotDiffDomain(name, true, rowsFound.Count(entry => entry.Change == SnapshotChange.Added), rowsFound.Count(entry => entry.Change == SnapshotChange.Removed)));
        }

        foreach (((string snapshot, _), (int at, SnapshotDomain read, List<string> rested)) in partials)
        {
            warnings[at] = PartialWarning(snapshot, read, rested);
        }

        return new SnapshotDiffReport(
            Side(fromDetail, from, now),
            Side(toDetail, to, now),
            domains,
            changes,
            [.. warnings.Distinct(StringComparer.Ordinal)])
        {
            User = found,
        };
    }

    /// <summary>
    /// The window's diff (task 115): the second snapshot the newest unless named, the first the newest readable one
    /// taken before the second unless named; null when the first is not named and there is none before the second, so
    /// fewer than two snapshots to compare.
    /// </summary>
    /// <exception cref="AegisException">As <see cref="Compare"/>, for a pick that is named.</exception>
    public static SnapshotDiffReport? CompareWithThePrevious(string home, string profile, string? from, string? to, string? user, DateTimeOffset now)
    {
        string second = string.IsNullOrWhiteSpace(to) ? Latest : to;
        string? first = from;
        if (string.IsNullOrWhiteSpace(first))
        {
            DateTimeOffset? before = string.IsNullOrWhiteSpace(to) ? null : Resolve(home, profile, to, now).Detail.Snapshot.Taken;
            IEnumerable<SnapshotDetail> readable = SnapshotStore.Files(SnapshotStore.Folder(home, profile)).Select(SnapshotStore.TryRead).OfType<SnapshotDetail>();
            SnapshotDetail? previous = before is { } taken
                ? readable.FirstOrDefault(detail => detail.Snapshot.Taken < taken)
                : readable.Skip(1).FirstOrDefault();
            if (previous is null)
            {
                return null;
            }

            first = previous.Snapshot.Name;
        }

        return Compare(home, profile, first, second, now, user);
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
    private static List<SnapshotDiffEntry> Rows(SqliteConnection connection, string domain, Func<string, string> rows, Dictionary<string, long> parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            with a as ({rows("main")}), b as ({rows("other")})
            select 0, what, whatId, who, whoId, how, through from a where k not in (select k from b)
            union all
            select 1, what, whatId, who, whoId, how, through from b where k not in (select k from a)
            order by 2 collate nocase, 4 collate nocase, 1, 6, 7
            """;
        foreach ((string name, long value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        using SqliteDataReader reader = command.ExecuteReader();
        var found = new List<SnapshotDiffEntry>();
        while (reader.Read())
        {
            found.Add(new SnapshotDiffEntry(domain, (SnapshotChange)reader.GetInt32(0), reader.GetString(1), reader.GetString(3), reader.GetString(5))
            {
                WhatId = Text(reader, 2),
                WhoId = Text(reader, 4),
                Through = Text(reader, 6),
            });
        }

        return found;
    }

    /// <summary>Why a snapshot cannot speak for a domain: one it rests on failed, was skipped or is not in its manifest.</summary>
    private static IEnumerable<string> Unread(SnapshotDetail detail, string[] restsOn) =>
        restsOn.Select(name => (Name: name, Domain: detail.Domains.FirstOrDefault(domain => domain.Name == name)))
            .Where(pair => pair.Domain is null || pair.Domain.Status is SnapshotDomainStatus.Failed or SnapshotDomainStatus.Skipped)
            .Select(pair => $"{pair.Name} was not read in {detail.Snapshot.Name}");

    /// <summary>
    /// A domain read in part: a change among the items it could not read shows as removed or added. A manifest written
    /// before the take counted the unread items has none to give, so the warning says the count is unknown.
    /// </summary>
    private static IEnumerable<(SnapshotDetail Detail, SnapshotDomain Read)> Partial(SnapshotDetail detail, string[] restsOn) =>
        detail.Domains
            .Where(read => read.Status == SnapshotDomainStatus.Partial && restsOn.Contains(read.Name))
            .Select(read => (detail, read));

    /// <summary>
    /// The warning of a domain read in part in one snapshot, naming the sections that rest on it (task 114: one warning,
    /// not one per section): "the sites and sharedItems rows of those items may show as removed or added".
    /// </summary>
    private static string PartialWarning(string snapshot, SnapshotDomain read, List<string> sections)
    {
        string named = sections.Count == 1 ? sections[0] : $"{string.Join(", ", sections[..^1])} and {sections[^1]}";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{read.Name} is partial in {snapshot} ({(read.ItemsFailed > 0 ? $"{read.ItemsFailed} item(s) not read" : "items not read: unknown")}): the {named} rows of those items may show as removed or added");
    }

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

    /// <summary>
    /// The user of one user's diff in both snapshots: found by name in the second (an object id, else the user principal
    /// name or the address, as the access view finds a user), then the same object in the first by its identity (the
    /// object id, else the address), so a renamed user principal name still matches; when the second does not know the
    /// name, the first is asked and the second looked up by that identity. The keys are null for a snapshot without the
    /// user.
    /// </summary>
    /// <exception cref="AegisException">A usage error for a blank name; not found when neither snapshot knows the user.</exception>
    private static (long? First, long? Second, AccessUser User) FindInBoth(SqliteConnection connection, string user, string first, string second)
    {
        string name = user.Trim();
        if (name.Length == 0)
        {
            throw new AegisException(FailureKind.Usage, "no user was named", "give the user's user principal name, address or object id");
        }

        string id = Guid.TryParse(name, out Guid guid) ? guid.ToString("D") : name;
        (long Key, AccessUser User)? inSecond = FindUser(connection, "other", id, name);
        (long Key, AccessUser User)? inFirst = inSecond is { } known ? Same(connection, "main", known.User) : FindUser(connection, "main", id, name);
        if (inSecond is null && inFirst is { } before)
        {
            inSecond = Same(connection, "other", before.User);
        }

        return inFirst is null && inSecond is null
            ? throw new AegisException(
                FailureKind.NotFound,
                $"neither {first} nor {second} knows a user '{name}'",
                "give the user principal name, the address or the object id; `m365-aegis snapshot list` shows the snapshots")
            : (inFirst?.Key, inSecond?.Key, (inSecond ?? inFirst)!.Value.User);
    }

    /// <summary>The same object in the other snapshot: by its object id, else by its address (an external grantee has no id).</summary>
    private static (long Key, AccessUser User)? Same(SqliteConnection connection, string schema, AccessUser user) =>
        user.ObjectId is { } objectId ? FindUser(connection, schema, objectId, null) : FindUser(connection, schema, null, user.Mail);

    /// <summary>A user (or an external address) of one snapshot by object id, or by user principal name or address; a member account before an external one.</summary>
    private static (long Key, AccessUser User)? FindUser(SqliteConnection connection, string schema, string? id, string? name)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            select key, objectId, name, userPrincipalName, mail from {schema}.objects
            where kind in ('user', 'external') and (objectId = $id or userPrincipalName = $name collate nocase or mail = $name collate nocase)
            order by kind = 'user' desc, key limit 1
            """;
        command.Parameters.AddWithValue("$id", (object?)id ?? DBNull.Value);
        command.Parameters.AddWithValue("$name", (object?)name ?? DBNull.Value);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetInt64(0), new AccessUser(Text(reader, 1), Text(reader, 2), Text(reader, 3), Text(reader, 4))) : null;
    }

    /// <summary>The parameter that holds the user's key in a snapshot (<c>main</c> or <c>other</c>); -1 when the snapshot does not know the user.</summary>
    private static string UserKey(string schema) => "$user_" + schema;

    /// <summary>The groups of the user in a snapshot, direct and nested.</summary>
    private static string GroupsOfUser(string schema) => $"select groupKey from {schema}.userGroups where userKey = {UserKey(schema)}";

    /// <summary>The group a row is held through: the holder when it is not the user, else null.</summary>
    private static string Through(string alias, string schema) => $"case when {alias}.key = {UserKey(schema)} then null else {Name(alias)} end";

    private static string? Text(SqliteDataReader reader, int column) => reader.IsDBNull(column) ? null : reader.GetString(column);

    /// <summary>How a mailbox is shown: by its address.</summary>
    private static string Mailbox(string alias) =>
        $"coalesce({alias}.mail, {alias}.userPrincipalName, {alias}.name, {alias}.objectId, '?')";
}
