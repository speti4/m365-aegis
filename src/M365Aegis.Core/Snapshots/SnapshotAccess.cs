using System.Globalization;
using M365Aegis.Core.Output;
using Microsoft.Data.Sqlite;

namespace M365Aegis.Core.Snapshots;

/// <summary>The user an access view is about, as the snapshot knows them.</summary>
/// <param name="ObjectId">The object id, or null for an address no directory object stands for.</param>
/// <param name="DisplayName">The display name, or null.</param>
/// <param name="UserPrincipalName">The user principal name, or null.</param>
/// <param name="Mail">The address, or null.</param>
public sealed record AccessUser(string? ObjectId, string? DisplayName, string? UserPrincipalName, string? Mail);

/// <summary>A group the user is a member of.</summary>
/// <param name="Id">The group's object id.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="Kind">The group's kind (<c>Microsoft365</c>, <c>Security</c>, …), or null when the snapshot does not know it.</param>
/// <param name="Via">The direct member groups the membership comes through; empty for a direct member.</param>
public sealed record AccessGroup(string? Id, string? DisplayName, string? Kind, IReadOnlyList<string> Via)
{
    /// <summary>Whether the user is a direct member.</summary>
    public bool Direct => Via.Count == 0;
}

/// <summary>A site the user reaches through the Microsoft 365 group behind it.</summary>
/// <param name="Name">The site's name.</param>
/// <param name="WebUrl">The site's address, or null.</param>
/// <param name="Group">The group behind the site.</param>
/// <param name="Role">How the user holds the group: <c>member</c> or <c>owner</c>.</param>
public sealed record AccessSite(string Name, string? WebUrl, string? Group, string Role);

/// <summary>A shared item granted to the user or to one of their groups.</summary>
/// <param name="Site">The site whose library holds the item.</param>
/// <param name="Item">The item's name.</param>
/// <param name="Path">The folder it sits in, or null.</param>
/// <param name="Kind"><c>link</c> for a sharing link that names the user, <c>direct</c> for a permission granted directly.</param>
/// <param name="Scope">A link's scope (<c>users</c>), or null for a direct grant.</param>
/// <param name="Roles">The roles (<c>read</c>, <c>write</c>).</param>
/// <param name="Expires">When the grant expires, or null.</param>
/// <param name="Through">The group the grant is made to, or null for one made to the user.</param>
public sealed record AccessSharedItem(string Site, string? Item, string? Path, string Kind, string? Scope, string? Roles, string? Expires, string? Through);

/// <summary>A right the user holds on a mailbox.</summary>
/// <param name="Mailbox">The mailbox's display name.</param>
/// <param name="Address">The mailbox's address.</param>
/// <param name="Right"><c>full-access</c>, <c>send-as</c> or <c>send-on-behalf</c>.</param>
/// <param name="Through">The group holding the right, or null for a right granted to the user.</param>
public sealed record AccessMailbox(string? Mailbox, string? Address, string Right, string? Through);

/// <summary>A calendar permission the user holds.</summary>
/// <param name="Mailbox">The mailbox's display name.</param>
/// <param name="Address">The mailbox's address.</param>
/// <param name="Folder">The calendar folder as the mailbox names it.</param>
/// <param name="Rights">The access rights.</param>
/// <param name="Through">The group holding them, or null for rights granted to the user.</param>
public sealed record AccessCalendar(string? Mailbox, string? Address, string Folder, string Rights, string? Through);

/// <summary>What reaches everyone, as counts (D-011): it reaches this user as it reaches any other.</summary>
/// <param name="OrganizationLinks">Sharing links anyone in the organization can open.</param>
/// <param name="AnonymousLinks">Sharing links anyone with the link can open, signed in or not.</param>
/// <param name="DefaultCalendars">Calendars whose <c>Default</c> entry gives every user more than free/busy.</param>
/// <param name="AnonymousCalendars">Calendars whose <c>Anonymous</c> entry gives anything.</param>
public sealed record AccessEveryone(long OrganizationLinks, long AnonymousLinks, long DefaultCalendars, long AnonymousCalendars);

/// <summary>The answer of <c>access &lt;user&gt;</c> (task 084): what one user reaches, from the newest snapshot.</summary>
/// <param name="Snapshot">The snapshot's name.</param>
/// <param name="Taken">When it was taken.</param>
/// <param name="AgeHours">Its age when the view was asked for, in hours.</param>
/// <param name="User">The user.</param>
/// <param name="Groups">The groups, direct and nested.</param>
/// <param name="Sites">The sites whose group the user is a member or an owner of.</param>
/// <param name="SharedItems">The shared items granted to the user or their groups.</param>
/// <param name="Mailboxes">The mailbox rights of the user or their groups.</param>
/// <param name="Calendars">The calendar permissions of the user or their groups.</param>
/// <param name="Everyone">What reaches everyone, as counts.</param>
/// <param name="Warnings">An old snapshot, a domain it lacks.</param>
public sealed record AccessReport(
    string Snapshot,
    DateTimeOffset Taken,
    double AgeHours,
    AccessUser User,
    IReadOnlyList<AccessGroup> Groups,
    IReadOnlyList<AccessSite> Sites,
    IReadOnlyList<AccessSharedItem> SharedItems,
    IReadOnlyList<AccessMailbox> Mailboxes,
    IReadOnlyList<AccessCalendar> Calendars,
    AccessEveryone Everyone,
    IReadOnlyList<string> Warnings);

/// <summary>Where the window's user picker found its users (task 112).</summary>
public enum UserSource
{
    /// <summary>The directory, live: Microsoft Graph's <c>/users</c> search with the window's sign-in.</summary>
    Graph,

    /// <summary>The newest snapshot on this computer: no sign-in, or Graph did not answer.</summary>
    Snapshot,
}

/// <summary>The users whose name or address starts with a text (task 112), for the window's user picker.</summary>
/// <param name="Source">Which source answered: the directory, or the snapshot when Graph could not.</param>
/// <param name="Snapshot">The snapshot's name when it answered, else null.</param>
/// <param name="Taken">When that snapshot was taken, else null.</param>
/// <param name="Users">The matching users, by name, the limit at most.</param>
/// <param name="More">Whether more users match than the list holds: a longer text narrows it.</param>
public sealed record UserMatches(UserSource Source, string? Snapshot, DateTimeOffset? Taken, IReadOnlyList<AccessUser> Users, bool More);

/// <summary>
/// <c>access &lt;user&gt;</c> (task 084): which groups, sites, shared items, mailboxes and calendars one user reaches,
/// from the newest snapshot of the profile (D-011's query path), opened read-only; one statement per section, no
/// request to any service. The user is named by user principal name, address or object id.
/// </summary>
public static class SnapshotAccess
{
    /// <summary>A snapshot older than this many hours is answered with a warning.</summary>
    public const double StaleHours = 36;

    private const string GroupsOfUser = "select groupKey from userGroups where userKey = $user";

    /// <summary>The access view of one user.</summary>
    /// <exception cref="AegisException">A <see cref="FailureKind.NotFound"/> failure when the profile has no snapshot, or the snapshot no such user.</exception>
    public static AccessReport ForUser(string home, string profile, string user, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user);
        string name = user.Trim();
        if (name.Length == 0)
        {
            throw new AegisException(FailureKind.Usage, "no user was named", "give the user's user principal name, address or object id");
        }

        (string file, SnapshotDetail detail) = SnapshotStore.Newest(home, profile);
        using SqliteConnection connection = SnapshotStore.OpenRead(file)!;
        long key;
        AccessUser found;
        using (SqliteCommand command = Command(connection, """
            select key, objectId, name, userPrincipalName, mail from objects
            where kind in ('user', 'external') and (objectId = $id or userPrincipalName = $name collate nocase or mail = $name collate nocase)
            order by kind = 'user' desc, key limit 1
            """, -1))
        {
            command.Parameters.AddWithValue("$id", Guid.TryParse(name, out Guid id) ? id.ToString("D") : name);
            command.Parameters.AddWithValue("$name", name);
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                throw new AegisException(
                    FailureKind.NotFound,
                    $"the snapshot {detail.Snapshot.Name} knows no user '{name}'",
                    "give the user principal name, the address or the object id; a user created since needs a new `m365-aegis snapshot take`");
            }

            key = reader.GetInt64(0);
            found = new AccessUser(Text(reader, 1), Text(reader, 2), Text(reader, 3), Text(reader, 4));
        }

        var groups = new List<AccessGroup>();
        foreach (IGrouping<string, (string Key, string? Id, string? Name, string? Kind, string? Via)> group in Rows(connection, key, """
            select g.key, g.objectId, g.name, g.groupKind, v.name
            from userGroups ug join objects g on g.key = ug.groupKey left join objects v on v.key = ug.viaKey
            where ug.userKey = $user
            order by g.name collate nocase, g.key, v.name collate nocase
            """, reader => (Key: reader.GetInt64(0).ToString(CultureInfo.InvariantCulture), Id: Text(reader, 1), Name: Text(reader, 2), Kind: Text(reader, 3), Via: Text(reader, 4)))
            .GroupBy(row => row.Key))
        {
            var first = group.First();
            groups.Add(new AccessGroup(first.Id, first.Name, first.Kind, group.Select(row => row.Via).OfType<string>().ToList()));
        }

        List<AccessSite> sites = Rows(connection, key, $"""
            select s.name, s.webUrl, g.name, 'member' from sites s join objects g on g.key = s.groupKey where s.groupKey in ({GroupsOfUser})
            union
            select s.name, s.webUrl, g.name, 'owner' from sites s join objects g on g.key = s.groupKey where s.groupKey in (select groupKey from owners where ownerKey = $user)
            order by 1 collate nocase, 4
            """, reader => new AccessSite(reader.GetString(0), Text(reader, 1), Text(reader, 2), reader.GetString(3)));

        List<AccessSharedItem> shared = Rows(connection, key, $"""
            select s.name, l.itemName, l.itemPath, l.kind, l.scope, l.roles, l.expires, case when p.key = $user then null else p.name end
            from linkGrantees lg join links l on l.key = lg.linkKey join sites s on s.key = l.siteKey join objects p on p.key = lg.principalKey
            where lg.principalKey = $user or lg.principalKey in ({GroupsOfUser})
            order by s.name collate nocase, l.itemPath, l.itemName, l.key
            """, reader => new AccessSharedItem(reader.GetString(0), Text(reader, 1), Text(reader, 2), reader.GetString(3), Text(reader, 4), Text(reader, 5), Text(reader, 6), Text(reader, 7)));

        AccessEveryone everyone = Rows(connection, key, """
            select
                (select count(*) from links where scope = 'organization'),
                (select count(*) from links where scope = 'anonymous'),
                (select count(*) from folderPermissions where principalName = 'Default' and rights not in ('None', 'AvailabilityOnly')),
                (select count(*) from folderPermissions where principalName = 'Anonymous' and rights <> 'None')
            where $user is not null
            """, reader => new AccessEveryone(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3))).Single();

        List<AccessMailbox> mailboxes = Rows(connection, key, $"""
            select m.name, m.mail, g.right, case when h.key = $user then null else h.name end
            from grants g join objects m on m.key = g.mailboxKey join objects h on h.key = g.holderKey
            where g.holderKey = $user or g.holderKey in ({GroupsOfUser})
            order by m.name collate nocase, g.right, 4
            """, reader => new AccessMailbox(Text(reader, 0), Text(reader, 1), reader.GetString(2), Text(reader, 3)));

        List<AccessCalendar> calendars = Rows(connection, key, $"""
            select m.name, m.mail, f.folder, f.rights, case when p.key = $user then null else p.name end
            from folderPermissions f join objects m on m.key = f.mailboxKey join objects p on p.key = f.principalKey
            where f.principalKey = $user or f.principalKey in ({GroupsOfUser})
            order by m.name collate nocase, 5
            """, reader => new AccessCalendar(Text(reader, 0), Text(reader, 1), reader.GetString(2), reader.GetString(3), Text(reader, 4)));

        double age = Math.Round((now - detail.Snapshot.Taken).TotalHours, 1);
        var warnings = new List<string>();
        if (age > StaleHours)
        {
            warnings.Add(string.Format(CultureInfo.InvariantCulture, "the snapshot is {0:0.#} hours old, older than {1}: run `m365-aegis snapshot take` for a fresh one", age, StaleHours));
        }

        // A warning names the domain and counts, never an item: a note's examples (a site's or a file's name) stay in
        // `snapshot show`.
        warnings.AddRange(detail.Domains
            .Where(domain => domain.Status != SnapshotDomainStatus.Ok)
            .Select(domain => domain.Status switch
            {
                SnapshotDomainStatus.Partial => $"{domain.Name} is partial in this snapshot: {domain.ItemsFailed} item(s) could not be read ({domain.ItemFailures}); `m365-aegis snapshot show` names them",
                SnapshotDomainStatus.Failed => $"{domain.Name} could not be read in this snapshot; `m365-aegis snapshot show` names the failure",
                _ => $"{domain.Name} was not read: {domain.Note}",
            }));
        return new AccessReport(detail.Snapshot.Name, detail.Snapshot.Taken, age, found, groups, sites, shared, mailboxes, calendars, everyone, warnings);
    }

    /// <summary>How many users <see cref="FindUsers"/> lists at most.</summary>
    public const int MatchLimit = 8;

    /// <summary>
    /// The users of the newest snapshot whose display name, a word of it, user principal name or address starts with
    /// the text, in any case (task 112): the window's user picker suggests them when the directory cannot be searched
    /// live (no sign-in, or Graph did not answer, <c>UserSearch</c>). A prefix search
    /// over the snapshot's <c>objects</c>, the local file only, opened read-only as <see cref="ForUser"/> opens it;
    /// <c>%</c>, <c>_</c> and <c>\</c> in the text match themselves.
    /// </summary>
    /// <exception cref="AegisException">A <see cref="FailureKind.NotFound"/> failure when the profile has no snapshot, a <see cref="FailureKind.Usage"/> one for an empty text.</exception>
    public static UserMatches FindUsers(string home, string profile, string text, int limit = MatchLimit)
    {
        ArgumentNullException.ThrowIfNull(text);
        string prefix = text.Trim();
        if (prefix.Length == 0)
        {
            throw new AegisException(FailureKind.Usage, "no text to search for", "type the start of a name or an address");
        }

        (string file, SnapshotDetail detail) = SnapshotStore.Newest(home, profile);
        using SqliteConnection connection = SnapshotStore.OpenRead(file)!;
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            select objectId, name, userPrincipalName, mail from objects
            where kind = 'user' and (
                name like $prefix escape '\' or name like '% ' || $prefix escape '\'
                or userPrincipalName like $prefix escape '\' or mail like $prefix escape '\')
            order by name collate nocase, userPrincipalName collate nocase, key
            limit $limit
            """;
        string escaped = prefix
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
        command.Parameters.AddWithValue("$prefix", escaped + "%");
        command.Parameters.AddWithValue("$limit", limit + 1);
        using SqliteDataReader reader = command.ExecuteReader();
        var users = new List<AccessUser>();
        while (reader.Read())
        {
            users.Add(new AccessUser(Text(reader, 0), Text(reader, 1), Text(reader, 2), Text(reader, 3)));
        }

        return new UserMatches(UserSource.Snapshot, detail.Snapshot.Name, detail.Snapshot.Taken, users.Take(limit).ToList(), users.Count > limit);
    }

    private static List<T> Rows<T>(SqliteConnection connection, long user, string sql, Func<SqliteDataReader, T> read)
    {
        using SqliteCommand command = Command(connection, sql, user);
        using SqliteDataReader reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read())
        {
            rows.Add(read(reader));
        }

        return rows;
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql, long user)
    {
        SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$user", user);
        return command;
    }

    private static string? Text(SqliteDataReader reader, int column) => reader.IsDBNull(column) ? null : reader.GetString(column);
}
