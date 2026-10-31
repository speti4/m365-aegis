namespace M365Aegis.Core.Snapshots;

/// <summary>
/// The schema of a snapshot file (D-011, task 087): one SQLite file per snapshot with integer keys. <c>objects</c>
/// holds every user, group, mailbox and other principal once; the edge tables refer to it by its integer key and are
/// indexed on the principal the access view asks about. <c>pragma user_version</c> carries <see cref="Version"/>, and
/// so does the <c>manifest</c>; a reader opens only the files of its own version.
/// </summary>
public static class SnapshotSchema
{
    /// <summary>The schema's version: a change of a table or a column is a new number, and an older file is not read.</summary>
    public const int Version = 1;

    /// <summary>The tables, in the order <c>snapshot show</c> counts them.</summary>
    public static IReadOnlyList<string> Tables { get; } =
    [
        "objects", "members", "userGroups", "owners", "sites", "teams", "links", "linkGrantees", "grants", "folderPermissions", "forwarding",
    ];

    /// <summary>The statements that create the tables and their indexes in an empty file.</summary>
    internal const string Create = """
        create table manifest (key text primary key, value text not null) without rowid;
        create table objects (
            key integer primary key,
            objectId text,
            kind text not null,
            userPrincipalName text,
            name text,
            mail text,
            groupKind text,
            mailboxType text,
            synced integer);
        create unique index objects_objectId on objects (objectId);
        create unique index objects_userPrincipalName on objects (userPrincipalName collate nocase);
        create index objects_mail on objects (mail collate nocase);
        create table members (groupKey integer not null, memberKey integer not null);
        create index members_group on members (groupKey);
        create index members_member on members (memberKey);
        create table userGroups (userKey integer not null, groupKey integer not null, viaKey integer);
        create index userGroups_user on userGroups (userKey);
        create index userGroups_group on userGroups (groupKey);
        create table owners (groupKey integer not null, ownerKey integer not null, source text not null);
        create index owners_owner on owners (ownerKey);
        create index owners_group on owners (groupKey);
        create table sites (key integer primary key, siteId text not null, name text, webUrl text, driveId text, groupKey integer);
        create index sites_group on sites (groupKey);
        create table teams (groupKey integer primary key, visibility text);
        create table links (
            key integer primary key,
            siteKey integer not null,
            itemId text not null,
            itemName text,
            itemPath text,
            kind text not null,
            scope text,
            type text,
            roles text,
            expires text,
            hasPassword integer);
        create index links_site on links (siteKey);
        create index links_scope on links (scope);
        create table linkGrantees (linkKey integer not null, principalKey integer not null);
        create index linkGrantees_principal on linkGrantees (principalKey);
        create table grants (mailboxKey integer not null, right text not null, holderKey integer not null);
        create index grants_holder on grants (holderKey);
        create index grants_mailbox on grants (mailboxKey);
        create table folderPermissions (mailboxKey integer not null, folder text not null, principalKey integer, principalName text not null, rights text not null);
        create index folderPermissions_principal on folderPermissions (principalKey);
        create index folderPermissions_mailbox on folderPermissions (mailboxKey);
        create table forwarding (
            mailboxKey integer not null,
            kind text not null,
            target text not null,
            targetAddress text,
            targetKey integer,
            external integer,
            keepsCopy integer not null);
        create index forwarding_mailbox on forwarding (mailboxKey);
        create index forwarding_target on forwarding (targetKey);
        """;
}
