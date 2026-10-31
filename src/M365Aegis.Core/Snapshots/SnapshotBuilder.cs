using System.Globalization;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Groups;
using Microsoft.Data.Sqlite;

namespace M365Aegis.Core.Snapshots;

/// <summary>
/// A snapshot while the take reads it (task 087): every object once with its integer key, the edges between them by
/// key, and the writer of the file in the shape of <see cref="SnapshotSchema"/>. An object is the same object when its
/// object id or its user principal name is (a mailbox and its user, a member and an owner); a principal with neither
/// (a role group, a name no recipient answers to, an external address) is one object per kind and name or address.
/// </summary>
internal sealed class SnapshotBuilder
{
    private readonly List<SnapshotObject> _objects = [];
    private readonly Dictionary<string, SnapshotObject> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SnapshotObject> _byUserPrincipalName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SnapshotObject> _byLabel = new(StringComparer.OrdinalIgnoreCase);

    internal IReadOnlyList<SnapshotObject> Objects => _objects;

    internal List<(int Group, int Member)> Members { get; } = [];

    internal List<(int User, int Group, int? Via)> UserGroups { get; } = [];

    internal List<(int Group, int Owner, string Source)> Owners { get; } = [];

    internal List<SnapshotSite> Sites { get; } = [];

    internal List<(int Group, string? Visibility)> Teams { get; } = [];

    internal List<SnapshotLink> Links { get; } = [];

    internal List<(int Link, int Principal)> LinkGrantees { get; } = [];

    internal List<(int Mailbox, string Right, int Holder)> Grants { get; } = [];

    internal List<(int Mailbox, string Folder, int? Principal, string PrincipalName, string Rights)> FolderPermissions { get; } = [];

    internal List<(int Mailbox, string Kind, string Target, string? TargetAddress, int? TargetKey, bool? External, bool KeepsCopy)> Forwarding { get; } = [];

    /// <summary>
    /// The object these names describe: the one with the object id, else the one with the user principal name, else a
    /// new one; what it lacked of the names is filled in. An id that is not a GUID (a role group's name) is no id.
    /// </summary>
    internal SnapshotObject Object(string kind, string? objectId, string? userPrincipalName, string? name, string? mail)
    {
        string? id = Guid.TryParse(objectId, out Guid guid) ? guid.ToString("D") : null;
        string? upn = string.IsNullOrWhiteSpace(userPrincipalName) ? null : userPrincipalName;
        SnapshotObject? found = Find(id, upn);
        if (found is null && id is null && upn is null)
        {
            // Nothing identifies it but its kind and its address or name: one object per label.
            string label = string.Concat(kind, "\n", mail, "\n", mail is null ? name : null);
            if (!_byLabel.TryGetValue(label, out found))
            {
                found = Add(kind);
                _byLabel[label] = found;
            }
        }

        found ??= Add(kind);
        if (id is not null)
        {
            found.ObjectId ??= id;
            _byId.TryAdd(id, found);
        }

        if (upn is not null && found.UserPrincipalName is null && _byUserPrincipalName.TryAdd(upn, found))
        {
            found.UserPrincipalName = upn;
        }

        found.Name ??= name;
        found.Mail ??= mail;
        return found;
    }

    /// <summary>A group of the directory, with its kind and whether it is synced.</summary>
    internal SnapshotObject Group(GroupInfo group)
    {
        SnapshotObject found = Object("group", group.Id, null, group.DisplayName, group.Mail);
        found.GroupKind = group.Kind;
        found.Synced = group.Synced;
        return found;
    }

    /// <summary>A mailbox: the user (or the shared mailbox's account) it belongs to, with its type.</summary>
    internal SnapshotObject Mailbox(MailboxInfo mailbox)
    {
        SnapshotObject found = Object("user", mailbox.ObjectId, mailbox.UserPrincipalName, mailbox.DisplayName, mailbox.PrimarySmtpAddress);
        found.MailboxType = mailbox.Type;
        return found;
    }

    /// <summary>The object with the object id, else the one with the user principal name; null for neither.</summary>
    internal SnapshotObject? Find(string? objectId, string? userPrincipalName)
    {
        if (objectId is not null && _byId.TryGetValue(objectId, out SnapshotObject? byId))
        {
            return byId;
        }

        return userPrincipalName is not null && _byUserPrincipalName.TryGetValue(userPrincipalName, out SnapshotObject? byName) ? byName : null;
    }

    /// <summary>
    /// The objects by the text a permission entry names them with: the user principal name, the address, or the display
    /// name when only one object carries it; a name two objects carry names neither.
    /// </summary>
    internal Dictionary<string, SnapshotObject?> NameIndex()
    {
        var index = new Dictionary<string, SnapshotObject?>(StringComparer.OrdinalIgnoreCase);
        foreach (SnapshotObject item in _objects)
        {
            foreach (string? name in new[] { item.UserPrincipalName, item.Mail, item.Name })
            {
                if (name is null)
                {
                    continue;
                }

                // A second object under the same text makes it ambiguous, unless it is the same object again.
                index[name] = index.TryGetValue(name, out SnapshotObject? other) && other != item ? null : item;
            }
        }

        return index;
    }

    /// <summary>
    /// The transitive closure of the direct members (D-011): every user of a group, directly (no via group) or through
    /// a direct member group whose nesting reaches the user, a row per such group as <c>group members --via</c> names
    /// it; a group met twice below one direct member group is followed once, so a cycle ends.
    /// </summary>
    internal int Closure()
    {
        Dictionary<int, List<SnapshotObject>> membersOf = Members
            .GroupBy(edge => edge.Group)
            .ToDictionary(edges => edges.Key, edges => edges.Select(edge => _objects[edge.Member - 1]).ToList());
        foreach ((int group, List<SnapshotObject> members) in membersOf)
        {
            var direct = members.Where(member => member.Kind == "user").Select(member => member.Key).ToHashSet();
            UserGroups.AddRange(direct.Select(user => (user, group, (int?)null)));
            foreach (SnapshotObject top in members.Where(member => member.Kind == "group"))
            {
                var seen = new HashSet<int> { top.Key };
                var reached = new HashSet<int>();
                var pending = new Queue<int>([top.Key]);
                while (pending.TryDequeue(out int current))
                {
                    foreach (SnapshotObject member in membersOf.GetValueOrDefault(current, []))
                    {
                        if (member.Kind == "group" && seen.Add(member.Key))
                        {
                            pending.Enqueue(member.Key);
                        }
                        else if (member.Kind == "user" && !direct.Contains(member.Key) && reached.Add(member.Key))
                        {
                            UserGroups.Add((member.Key, group, top.Key));
                        }
                    }
                }
            }
        }

        return UserGroups.Count;
    }

    /// <summary>Writes the snapshot into a new file: the journal off, one transaction, the schema's version last.</summary>
    internal void Write(string path, IReadOnlyDictionary<string, string> manifest)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        connection.Open();
        Execute(connection, null, "pragma journal_mode = off; pragma synchronous = off;");
        using (SqliteTransaction transaction = connection.BeginTransaction())
        {
            Execute(connection, transaction, SnapshotSchema.Create);
            Insert(connection, transaction, "manifest", manifest.OrderBy(pair => pair.Key, StringComparer.Ordinal), pair => [pair.Key, pair.Value]);
            Insert(connection, transaction, "objects", _objects, item =>
                [item.Key, item.ObjectId, item.Kind, item.UserPrincipalName, item.Name, item.Mail, item.GroupKind?.ToString(), item.MailboxType, Bit(item.Synced)]);
            Insert(connection, transaction, "members", Members, edge => [edge.Group, edge.Member]);
            Insert(connection, transaction, "userGroups", UserGroups, edge => [edge.User, edge.Group, edge.Via]);
            Insert(connection, transaction, "owners", Owners, edge => [edge.Group, edge.Owner, edge.Source]);
            Insert(connection, transaction, "sites", Sites, site => [site.Key, site.SiteId, site.Name, site.WebUrl, site.DriveId, site.Group]);
            Insert(connection, transaction, "teams", Teams, team => [team.Group, team.Visibility]);
            Insert(connection, transaction, "links", Links, link =>
                [link.Key, link.Site, link.ItemId, link.ItemName, link.ItemPath, link.Kind, link.Scope, link.Type, link.Roles, Moment(link.Expires), Bit(link.HasPassword)]);
            Insert(connection, transaction, "linkGrantees", LinkGrantees, edge => [edge.Link, edge.Principal]);
            Insert(connection, transaction, "grants", Grants, grant => [grant.Mailbox, grant.Right, grant.Holder]);
            Insert(connection, transaction, "folderPermissions", FolderPermissions, entry => [entry.Mailbox, entry.Folder, entry.Principal, entry.PrincipalName, entry.Rights]);
            Insert(connection, transaction, "forwarding", Forwarding, forward =>
                [forward.Mailbox, forward.Kind, forward.Target, forward.TargetAddress, forward.TargetKey, Bit(forward.External), Bit(forward.KeepsCopy)]);
            transaction.Commit();
        }

        Execute(connection, null, string.Create(CultureInfo.InvariantCulture, $"pragma user_version = {SnapshotSchema.Version}"));
    }

    private SnapshotObject Add(string kind)
    {
        var item = new SnapshotObject(_objects.Count + 1, kind);
        _objects.Add(item);
        return item;
    }

    private static object? Bit(bool? flag) => flag switch
    {
        true => 1,
        false => 0,
        null => null,
    };

    private static string? Moment(DateTimeOffset? moment) =>
        moment?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Inserts the rows into a table with one prepared statement, a parameter per column.</summary>
    private static void Insert<T>(SqliteConnection connection, SqliteTransaction transaction, string table, IEnumerable<T> rows, Func<T, object?[]> values)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        foreach (T row in rows)
        {
            object?[] cells = values(row);
            if (command.Parameters.Count == 0)
            {
                IEnumerable<string> names = Enumerable.Range(1, cells.Length).Select(index => string.Create(CultureInfo.InvariantCulture, $"$p{index}"));
                command.CommandText = $"insert into {table} values ({string.Join(", ", names)})";
                foreach (string name in names)
                {
                    command.Parameters.Add(new SqliteParameter(name, DBNull.Value));
                }

                command.Prepare();
            }

            for (int index = 0; index < cells.Length; index++)
            {
                command.Parameters[index].Value = cells[index] ?? DBNull.Value;
            }

            command.ExecuteNonQuery();
        }
    }
}

/// <summary>One object of a snapshot: a user, a group, a contact, a device, a principal no directory object stands for.</summary>
/// <param name="key">The integer key the edges refer to.</param>
/// <param name="kind">The kind: <c>user</c>, <c>group</c>, <c>device</c>, <c>orgContact</c>, <c>recipient</c>, <c>external</c>, …</param>
internal sealed class SnapshotObject(int key, string kind)
{
    public int Key { get; } = key;

    public string Kind { get; } = kind;

    public string? ObjectId { get; set; }

    public string? UserPrincipalName { get; set; }

    public string? Name { get; set; }

    public string? Mail { get; set; }

    public GroupKind? GroupKind { get; set; }

    public string? MailboxType { get; set; }

    public bool? Synced { get; set; }
}

/// <summary>One site of a snapshot, with its drive and the Microsoft 365 group behind it once the drive is read.</summary>
internal sealed class SnapshotSite(int key, string siteId, string name, string? webUrl)
{
    public int Key { get; } = key;

    public string SiteId { get; } = siteId;

    public string Name { get; } = name;

    public string? WebUrl { get; } = webUrl;

    public string? DriveId { get; set; }

    public int? Group { get; set; }
}

/// <summary>
/// One shared item's grant: a sharing link (<c>link</c>, its scope, type, roles, expiry and whether it has a password,
/// never its URL) or a permission granted to someone directly (<c>direct</c>, its roles); who holds it is in
/// <see cref="SnapshotBuilder.LinkGrantees"/>.
/// </summary>
internal sealed record SnapshotLink(int Key, int Site, string ItemId, string? ItemName, string? ItemPath, string Kind, string? Scope, string? Type, string? Roles, DateTimeOffset? Expires, bool? HasPassword);
