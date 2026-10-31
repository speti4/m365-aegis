using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using M365Aegis.Core.Config;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Snapshots;

/// <summary>What a take needs besides the services: the home folder, the profile, the clock.</summary>
/// <param name="Home">The home folder (<see cref="AegisHome.Resolve"/>).</param>
/// <param name="Profile">The tenant profile: its name names the folder, its tenant goes into the manifest, its retention prunes.</param>
public sealed record SnapshotTakeOptions(string Home, TenantProfile Profile)
{
    /// <summary>The clock of the snapshot's name and of the retention.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>How many drives, permission lists or mailboxes are read at once.</summary>
    internal int Parallel { get; init; } = 4;

    /// <summary>How the prune removes a file (a test's refuses).</summary>
    internal Action<string> Delete { get; init; } = File.Delete;
}

/// <summary>
/// <c>snapshot take</c> (D-011, task 087): every domain the access views read, over the Core's reads, into one SQLite
/// file under the profile's snapshot folder. Each domain is read on its own: one that fails is recorded in the manifest
/// with the service's failure and the snapshot stays usable (exit code 6, and no prune); a sign-in that fails stops the
/// take (exit code 3), as every lookup's does. Only reads: nothing is written to the tenant, so the take is no plan
/// (D-006); its file is the administrator's to keep, which the retention governs.
/// </summary>
public static class SnapshotTake
{
    /// <summary>The <c>Prefer</c> value of the drives' <c>delta</c>: only the items whose sharing differs from their parent's are marked (task 092).</summary>
    internal const string HierarchicalSharing = "hierarchicalsharing";

    private const string UserPermission = "User.ReadBasic.All";

    /// <summary>
    /// The retries of one request after a 429 or 503 within the take (a command's are 4): SharePoint answers a busy
    /// tenant's permission reads with <c>activityLimitReached</c> and a <c>Retry-After</c>, item by item inside a batch.
    /// </summary>
    internal const int TakeRetries = 8;

    /// <summary>The longest the take waits on throttling in all (a command's is a minute): a nightly run may wait.</summary>
    internal static readonly TimeSpan TakeBudget = TimeSpan.FromHours(1);

    /// <summary>The examples of failed items a domain's note names; the rest are counted by their failure.</summary>
    internal const int NoteExamples = 3;

    /// <summary>Takes a snapshot, writes it, and prunes the older ones by the profile's retention when every domain was read.</summary>
    /// <exception cref="AegisException">The sign-in failed, every domain failed, the profile's retention or name is wrong, or the file system refused.</exception>
    public static async Task<CommandResult<SnapshotTakeResult>> RunAsync(ExchangeBackends backends, SnapshotTakeOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backends);
        ArgumentNullException.ThrowIfNull(options);
        SnapshotRetention retention = RetentionOf(options.Profile);
        string folder = SnapshotStore.Folder(options.Home, options.Profile.Name);
        AegisHome.Create(options.Home);
        Directory.CreateDirectory(folder);
        IReadOnlyList<string> removed = SnapshotStore.RemovePartials(folder);
        DateTimeOffset started = options.Clock.GetUtcNow();
        var clock = Stopwatch.StartNew();

        var take = new Reads(backends, options, cancellationToken);
        await take.ReadAllAsync().ConfigureAwait(false);
        if (!take.Domains.Any(domain => domain.Status is SnapshotDomainStatus.Ok or SnapshotDomainStatus.Partial))
        {
            throw new AegisException(take.FirstFailure!);
        }

        long elapsed = clock.ElapsedMilliseconds;
        var manifest = new Dictionary<string, string>(take.Extra, StringComparer.Ordinal)
        {
            ["schemaVersion"] = SnapshotSchema.Version.ToString(CultureInfo.InvariantCulture),
            ["taken"] = started.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["profile"] = options.Profile.Name,
            ["tenant"] = options.Profile.Tenant,
            ["toolVersion"] = ProductInfo.Version,
            ["elapsedMs"] = elapsed.ToString(CultureInfo.InvariantCulture),
        };
        for (int index = 0; index < take.Domains.Count; index++)
        {
            SnapshotDomain domain = take.Domains[index];
            manifest[SnapshotStore.DomainPrefix + domain.Name] = JsonSerializer.Serialize(domain, EnvelopeJson.Options);
            manifest[SnapshotStore.OrderPrefix + domain.Name] = index.ToString(CultureInfo.InvariantCulture);
        }

        string name = SnapshotStore.NameFor(started);
        string file = Path.Combine(folder, name);
        string partial = Path.Combine(folder, Path.GetFileNameWithoutExtension(name) + SnapshotStore.PartialExtension);
        take.Builder.Write(partial, manifest);
        Rename(partial, file, name);

        List<string> failed = take.Domains.Where(domain => domain.Status == SnapshotDomainStatus.Failed).Select(domain => domain.Name).ToList();
        (IReadOnlyList<string> pruned, IReadOnlyList<string> refused) = failed.Count == 0
            ? SnapshotStore.Prune(folder, retention, options.Clock.GetUtcNow(), options.Delete)
            : ([], []);
        var result = new SnapshotTakeResult(new SnapshotInfo(name, started, new FileInfo(file).Length, failed), file, elapsed, take.Domains, retention, pruned)
        {
            RemovedPartials = removed,
            Warnings = refused,
        };
        return failed.Count == 0
            ? CommandResult.Success(result)
            : CommandResult.Failure(
                new AegisFailure(
                    FailureKind.Partial,
                    $"{failed.Count} of {take.Domains.Count} domains could not be read ({string.Join(", ", failed)}); the snapshot is kept without them",
                    "`m365-aegis snapshot show` names each failure; the older snapshots are kept until a take reads every domain"),
                result);
    }

    /// <summary>The profile's retention: its <c>keepDaily</c> and <c>keepWeekly</c> in <c>config.json</c>, else D-011's.</summary>
    /// <exception cref="AegisException">A usage error for a value below 1.</exception>
    internal static SnapshotRetention RetentionOf(TenantProfile profile)
    {
        var retention = new SnapshotRetention(profile.KeepDaily ?? 14, profile.KeepWeekly ?? 8);
        if (retention.KeepDaily < 1 || retention.KeepWeekly < 1)
        {
            throw new AegisException(
                FailureKind.Usage,
                $"the profile '{profile.Name}' keeps {retention.KeepDaily} day(s) and {retention.KeepWeekly} week(s) of snapshots; each is 1 or more",
                "set keepDaily and keepWeekly of the profile in config.json to whole numbers of 1 or more, or remove them for 14 and 8");
        }

        return retention;
    }

    /// <summary>The kind of object a mailbox grant's recipient is: a group, a mailbox's user, or another recipient.</summary>
    internal static string KindOf(string? recipientType) =>
        recipientType switch
        {
            null => "recipient",
            _ when recipientType.Contains("Group", StringComparison.OrdinalIgnoreCase) => "group",
            _ when recipientType.Contains("Mailbox", StringComparison.OrdinalIgnoreCase) => "user",
            _ => "recipient",
        };

    /// <summary>
    /// The folder of a shared item within its library: the parent's path after <c>root:</c> (<c>/drives/{id}/root:/Reports</c>
    /// is <c>/Reports</c>, the root itself <c>/</c>); a path without it as it is; null for none.
    /// </summary>
    internal static string? FolderOf(string? path)
    {
        int root = path is null ? -1 : path.IndexOf("root:", StringComparison.Ordinal);
        return root < 0 ? path : "/" + path![(root + "root:".Length)..].TrimStart('/');
    }

    /// <summary>Renames the complete file into place; an existing snapshot of the same second is never replaced.</summary>
    private static void Rename(string partial, string file, string name)
    {
        try
        {
            File.Move(partial, file);
        }
        catch (IOException exception)
        {
            File.Delete(partial);
            throw new AegisException(
                FailureKind.Forbidden,
                $"the snapshot {name} could not be written: {exception.Message}",
                "a take of the same second wrote it already, or the file system refused; run the take again",
                exception);
        }
    }

    /// <summary>What one domain's read gave: its rows, the items it could not read, and a remark for the manifest.</summary>
    private sealed record DomainRead(int Rows, IReadOnlyList<Unread> Failed, string? Remark = null);

    /// <summary>An item a domain could not read: what it is, with the service's message, and the failure's code.</summary>
    private sealed record Unread(string What, string Code);

    /// <summary>The reads of one take, domain by domain, into one <see cref="SnapshotBuilder"/>.</summary>
    private sealed class Reads(ExchangeBackends backends, SnapshotTakeOptions options, CancellationToken cancellationToken)
    {
        // The take's own budget over the command's client: the same transport, the same request log.
        private readonly GraphClient _graph = backends.Graph.WithBudget(TakeRetries, TakeBudget);
        private List<GroupInfo> _groups = [];
        private IReadOnlyList<MailboxInfo> _mailboxes = [];

        internal SnapshotBuilder Builder { get; } = new();

        internal List<SnapshotDomain> Domains { get; } = [];

        /// <summary>What the manifest keeps beside the domains: each drive's delta link, for a later incremental take.</summary>
        internal Dictionary<string, string> Extra { get; } = new(StringComparer.Ordinal);

        internal AegisFailure? FirstFailure { get; private set; }

        internal async Task ReadAllAsync()
        {
            await DomainAsync("users", "graph /users", UsersAsync).ConfigureAwait(false);
            SnapshotDomain groups = await DomainAsync("groups", "graph /groups", GroupsAsync).ConfigureAwait(false);

            // The mailboxes come third: each names its user's principal name next to Exchange's object id, so a recipient
            // read later (an owner from ManagedBy, a grant's holder) is the user Graph read, whatever id Exchange gives it.
            SnapshotDomain mailboxes = await DomainAsync("mailboxes", "admin-api Get-Mailbox (pwsh Get-EXOMailbox when it cannot answer)", MailboxesAsync).ConfigureAwait(false);
            SnapshotDomain members = await DomainAsync("members", "graph /groups/{id}/members ($batch)", () => After(groups, MembersAsync)).ConfigureAwait(false);
            await DomainAsync("userGroups", "computed from the direct members at take time", () => After(members, () => Task.FromResult(new DomainRead(Builder.Closure(), [])))).ConfigureAwait(false);
            await DomainAsync("owners", "graph /groups/{id}/owners ($batch); pwsh Get-DistributionGroup ManagedBy for the groups Exchange owns", () => After(groups, OwnersAsync)).ConfigureAwait(false);
            await DomainAsync("teams", "graph /teams", TeamsAsync).ConfigureAwait(false);
            SnapshotDomain sites = await DomainAsync("sites", "graph /sites?search=*; /sites/{id}/drive ($batch): the drive and the group behind the site", SitesAsync).ConfigureAwait(false);
            await DomainAsync(
                "sharing",
                "graph /drives/{id}/root/delta (Prefer: hierarchicalsharing); /drives/{id}/items/{id}/permissions ($batch)",
                () => After(sites, SharingAsync)).ConfigureAwait(false);
            Domains.Add(new SnapshotDomain(
                "oneDrive",
                SnapshotDomainStatus.Skipped,
                "none",
                0,
                "other users' OneDrives need a consent the tool does not ask for (Sites.Read.All reads the signed-in account's only; task 092)"));
            await DomainAsync("grants", "pwsh Get-EXOMailboxPermission (split across sidecars), Get-EXORecipientPermission, GrantSendOnBehalfTo", GrantsAsync).ConfigureAwait(false);
            await DomainAsync("folderPermissions", "admin-api Get-MailboxFolderPermission of each mailbox's calendar", () => After(mailboxes, FolderPermissionsAsync)).ConfigureAwait(false);
            await DomainAsync("forwarding", "pwsh Get-EXOMailbox ForwardingSmtpAddress, ForwardingAddress", ForwardingAsync).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads one domain and records it with its time and the requests it sent (task 097: the domains are read one
        /// after the other, so the counters' difference is the domain's own); any failure but the sign-in's is the
        /// domain's own.
        /// </summary>
        private async Task<SnapshotDomain> DomainAsync(string name, string read, Func<Task<DomainRead>> work)
        {
            var clock = Stopwatch.StartNew();
            (long graph, TimeSpan waited, long adminApi, long sidecar) = (_graph.RequestsSent, _graph.Waited, backends.AdminApiCalls, backends.SidecarCalls);
            SnapshotDomain domain;
            try
            {
                DomainRead result = await work().ConfigureAwait(false);
                // The note stays short whatever the count: the failures by code, and the first few items as examples.
                string codes = string.Join(", ", result.Failed.GroupBy(item => item.Code).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Key} {group.Count()}"));
                domain = result.Failed.Count == 0
                    ? new SnapshotDomain(name, SnapshotDomainStatus.Ok, read, result.Rows, result.Remark)
                    : new SnapshotDomain(
                        name,
                        SnapshotDomainStatus.Partial,
                        read,
                        result.Rows,
                        string.Join("; ", new[] { $"{result.Failed.Count} could not be read ({codes}), for example {string.Join("; ", result.Failed.Take(NoteExamples).Select(item => item.What))}", result.Remark }.OfType<string>()))
                    {
                        ItemsFailed = result.Failed.Count,
                        ItemFailures = codes,
                    };
            }
            catch (AegisException exception) when (exception.Failure.Kind != FailureKind.Auth)
            {
                FirstFailure ??= exception.Failure;
                domain = new SnapshotDomain(name, SnapshotDomainStatus.Failed, read, 0, $"{exception.Failure.Code}: {exception.Message}");
            }

            domain = domain with
            {
                ElapsedMs = clock.ElapsedMilliseconds,
                GraphRequests = _graph.RequestsSent - graph,
                ThrottledMs = (long)(_graph.Waited - waited).TotalMilliseconds,
                AdminApiCalls = backends.AdminApiCalls - adminApi,
                SidecarCalls = backends.SidecarCalls - sidecar,
            };
            Domains.Add(domain);
            return domain;
        }

        /// <summary>A domain that needs another one: not read when that one failed.</summary>
        private static Task<DomainRead> After(SnapshotDomain needed, Func<Task<DomainRead>> work) =>
            needed.Status == SnapshotDomainStatus.Failed
                ? throw new AegisException(FailureKind.ServiceError, $"it needs the {needed.Name}, which could not be read")
                : work();

        private async Task<DomainRead> UsersAsync()
        {
            string path = GraphQuery.Path("/users", ("$select", "id,displayName,userPrincipalName,mail"), ("$top", "999"));
            IReadOnlyList<SnapshotUser> users = await _graph.ListAsync<SnapshotUser>(GraphRequest.Get(path) with { Permission = UserPermission }, cancellationToken).ConfigureAwait(false);
            foreach (SnapshotUser user in users)
            {
                Builder.Object("user", user.Id, user.UserPrincipalName, user.DisplayName, user.Mail);
            }

            return new DomainRead(users.Count, []);
        }

        private async Task<DomainRead> GroupsAsync()
        {
            string path = GraphQuery.Path("/groups", ("$select", GroupLookups.GroupFields), ("$top", "999"));
            IReadOnlyList<GraphGroup> groups = await _graph.ListAsync<GraphGroup>(GraphRequest.Get(path) with { Permission = GroupLookups.Permission }, cancellationToken).ConfigureAwait(false);
            _groups = groups.Select(group => group.ToInfo()).ToList();
            foreach (GroupInfo group in _groups)
            {
                Builder.Group(group);
            }

            return new DomainRead(_groups.Count, []);
        }

        private async Task<DomainRead> MembersAsync()
        {
            IReadOnlyDictionary<string, IReadOnlyList<GroupMember>> membersOf =
                await GroupLookups.MembersOfAsync(_graph, _groups.Select(group => group.Id), cancellationToken).ConfigureAwait(false);
            foreach ((string groupId, IReadOnlyList<GroupMember> members) in membersOf)
            {
                int group = Builder.Find(groupId, null)!.Key;
                Builder.Members.AddRange(members.Select(member => (group, Builder.Object(member.Type, member.Id, member.UserPrincipalName, member.DisplayName, member.Mail).Key)));
            }

            return new DomainRead(Builder.Members.Count, []);
        }

        private async Task<DomainRead> OwnersAsync()
        {
            IReadOnlyDictionary<string, IReadOnlyList<GroupMember>> ownersOf = await GroupLookups.RelationOfAsync(
                _graph, _groups.Where(group => !GroupWrites.ThroughExchange(group)).Select(group => group.Id), "owners", cancellationToken).ConfigureAwait(false);
            foreach ((string groupId, IReadOnlyList<GroupMember> owners) in ownersOf)
            {
                AddOwners(groupId, owners, "graph");
            }

            var failed = new List<Unread>();
            foreach (GroupInfo group in _groups.Where(GroupWrites.ThroughExchange))
            {
                try
                {
                    AddOwners(group.Id, await GroupWrites.ExchangeOwnerListAsync(backends, group, cancellationToken).ConfigureAwait(false), "managedBy");
                }
                catch (AegisException exception) when (exception.Failure.Kind != FailureKind.Auth)
                {
                    failed.Add(new Unread($"{group.DisplayName}: {exception.Message}", exception.Failure.Code));
                }
            }

            return new DomainRead(Builder.Owners.Count, failed);
        }

        private void AddOwners(string groupId, IReadOnlyList<GroupMember> owners, string source)
        {
            int group = Builder.Find(groupId, null)!.Key;
            Builder.Owners.AddRange(owners.Select(owner => (group, Builder.Object(owner.Type, owner.Id, owner.UserPrincipalName, owner.DisplayName, owner.Mail).Key, source)));
        }

        private async Task<DomainRead> TeamsAsync()
        {
            IReadOnlyList<TeamInfo> teams = (await WorkspaceLookups.TeamsAsync(_graph, cancellationToken).ConfigureAwait(false)).Data!;
            Builder.Teams.AddRange(teams.Select(team => (Builder.Object("group", team.Id, null, team.DisplayName, null).Key, team.Visibility)));
            return new DomainRead(teams.Count, []);
        }

        /// <summary>
        /// The sites the search finds, and each one's document library (task 092): <c>owner.group</c> of the drive is the
        /// Microsoft 365 group behind a group-connected site. A site without a library (404) is named in the remark, one
        /// whose library the account may not read (403) is an item that failed.
        /// </summary>
        private async Task<DomainRead> SitesAsync()
        {
            IReadOnlyList<SiteInfo> found = (await WorkspaceLookups.SitesAsync(_graph, null, cancellationToken).ConfigureAwait(false)).Data!;
            List<SnapshotSite> sites = found.Select((site, index) => new SnapshotSite(index + 1, site.Id, site.DisplayName, site.WebUrl)).ToList();
            Builder.Sites.AddRange(sites);
            IReadOnlyList<GraphBatchResponse> answers = await _graph.BatchAsync(
                sites.Select(site => GraphRequest.Get(GraphQuery.Path($"/sites/{site.SiteId}/drive", ("$select", "id,owner"))) with { Permission = WorkspaceLookups.SitePermission }).ToList(),
                cancellationToken).ConfigureAwait(false);
            var failed = new List<Unread>();
            var noLibrary = new List<string>();
            foreach ((SnapshotSite site, GraphBatchResponse answer) in sites.Zip(answers))
            {
                if (answer.StatusCode == 404)
                {
                    noLibrary.Add(site.Name);
                    continue;
                }

                try
                {
                    answer.EnsureSuccess();
                }
                catch (AegisException exception)
                {
                    failed.Add(new Unread($"{site.Name}: {exception.Message}", exception.Failure.Code));
                    continue;
                }

                SnapshotDrive drive = answer.Deserialize<SnapshotDrive>()!;
                site.DriveId = drive.Id;
                if (drive.Owner?.Group is { } group)
                {
                    site.Group = Builder.Object("group", group.Id, null, group.DisplayName, null).Key;
                }
            }

            return new DomainRead(sites.Count, failed, noLibrary.Count == 0 ? null : $"no document library: {string.Join(", ", noLibrary)}");
        }

        /// <summary>
        /// The shared items of every site's library (task 092): the drive's <c>delta</c> with <c>Prefer:
        /// hierarchicalsharing</c> marks the items whose sharing differs from their parent's, and their permissions are
        /// read twenty to a <c>$batch</c> call. Several drives' deltas are read at once, but their permissions one batch at
        /// a time, which SharePoint throttles first (live: 1,036 of 2,218 items answered 429 with four drives at once); a
        /// throttled item is sent again after its <c>Retry-After</c> within the take's budget. A drive that fails is an
        /// item that failed.
        /// </summary>
        private async Task<DomainRead> SharingAsync()
        {
            List<SnapshotSite> sites = Builder.Sites.Where(site => site.DriveId is not null).ToList();
            using var permissionGate = new SemaphoreSlim(1, 1);
            DriveRead[] reads = await EachAsync(sites, site => ReadDriveAsync(site, permissionGate)).ConfigureAwait(false);
            var failed = new List<Unread>();
            foreach (DriveRead read in reads)
            {
                failed.AddRange(read.Failed);
                if (read.DeltaLink is not null)
                {
                    Extra[$"deltaLink.{read.Site.DriveId}"] = read.DeltaLink;
                }

                foreach ((DriveItem item, IReadOnlyList<DrivePermission> permissions) in read.Items)
                {
                    AddPermissions(read.Site, item, permissions);
                }
            }

            return new DomainRead(Builder.Links.Count, failed);
        }

        private sealed record DriveRead(SnapshotSite Site, List<(DriveItem Item, IReadOnlyList<DrivePermission> Permissions)> Items, List<Unread> Failed, string? DeltaLink);

        private async Task<DriveRead> ReadDriveAsync(SnapshotSite site, SemaphoreSlim permissionGate)
        {
            var items = new List<(DriveItem, IReadOnlyList<DrivePermission>)>();
            var failed = new List<Unread>();
            var shared = new List<DriveItem>();
            string? deltaLink;
            try
            {
                var request = GraphRequest.Get(GraphQuery.Path($"/drives/{site.DriveId}/root/delta", ("$select", "id,name,shared,parentReference"), ("$top", "1000"))) with
                {
                    Permission = WorkspaceLookups.SitePermission,
                    Prefer = HierarchicalSharing,
                };
                while (true)
                {
                    DeltaPage page = (await _graph.GetAsync<DeltaPage>(request, cancellationToken).ConfigureAwait(false))!;
                    shared.AddRange(page.Value.Where(item => item.Shared is not null));
                    if (page.NextLink is null)
                    {
                        deltaLink = page.DeltaLink;
                        break;
                    }

                    request = new GraphRequest(HttpMethod.Get, page.NextLink) { Permission = WorkspaceLookups.SitePermission, Prefer = HierarchicalSharing };
                }
            }
            catch (AegisException exception) when (exception.Failure.Kind != FailureKind.Auth)
            {
                failed.Add(new Unread($"{site.Name}: {exception.Message}", exception.Failure.Code));
                return new DriveRead(site, items, failed, null);
            }

            IReadOnlyList<GraphBatchResponse> answers;
            await permissionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                answers = await _graph.BatchAsync(
                    shared.Select(item => GraphRequest.Get($"/drives/{site.DriveId}/items/{item.Id}/permissions") with { Permission = WorkspaceLookups.SitePermission }).ToList(),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                permissionGate.Release();
            }

            foreach ((DriveItem item, GraphBatchResponse answer) in shared.Zip(answers))
            {
                try
                {
                    answer.EnsureSuccess();
                    GraphPage<DrivePermission> page = answer.Deserialize<GraphPage<DrivePermission>>()!;
                    List<DrivePermission> permissions = [.. page.Value];
                    if (page.NextLink is { } next)
                    {
                        permissions.AddRange(await _graph.ListAsync<DrivePermission>(new GraphRequest(HttpMethod.Get, next) { Permission = WorkspaceLookups.SitePermission }, cancellationToken).ConfigureAwait(false));
                    }

                    items.Add((item, permissions));
                }
                catch (AegisException exception) when (exception.Failure.Kind != FailureKind.Auth)
                {
                    failed.Add(new Unread($"{site.Name}: {item.Name}: {exception.Message}", exception.Failure.Code));
                }
            }

            return new DriveRead(site, items, failed, deltaLink);
        }

        /// <summary>
        /// One shared item's permissions as rows (task 092): a sharing link with the users and groups it names, or a grant
        /// to a user or a group of the directory. An inherited permission is its parent's, already read; a grant to one
        /// of SharePoint's own groups (the site's Owners, Members, Visitors) is the group behind the site's.
        /// </summary>
        private void AddPermissions(SnapshotSite site, DriveItem item, IReadOnlyList<DrivePermission> permissions)
        {
            foreach (DrivePermission permission in permissions.Where(permission => permission.InheritedFrom is not { ValueKind: JsonValueKind.Object }))
            {
                string roles = string.Join(",", permission.Roles);
                List<(string Kind, SharingIdentity Identity)> grantees;
                string kind;
                if (permission.Link is { } link)
                {
                    kind = "link";
                    grantees = permission.GrantedToIdentitiesV2.Select(Grantee).OfType<(string, SharingIdentity)>().ToList();
                }
                else if (Grantee(permission.GrantedToV2) is { } grantee)
                {
                    kind = "direct";
                    grantees = [grantee];
                }
                else
                {
                    continue;
                }

                var row = new SnapshotLink(
                    Builder.Links.Count + 1,
                    site.Key,
                    item.Id,
                    item.Name,
                    FolderOf(item.ParentReference.Path),
                    kind,
                    permission.Link?.Scope,
                    permission.Link?.Type,
                    roles,
                    permission.ExpirationDateTime,
                    permission.HasPassword);
                Builder.Links.Add(row);
                foreach ((string granteeKind, SharingIdentity identity) in grantees)
                {
                    string objectKind = Guid.TryParse(identity.Id, out _) ? granteeKind : "external";
                    Builder.LinkGrantees.Add((row.Key, Builder.Object(objectKind, identity.Id, null, identity.DisplayName, identity.Email).Key));
                }
            }
        }

        /// <summary>The directory user or group a permission names; null for SharePoint's own identities.</summary>
        private static (string Kind, SharingIdentity Identity)? Grantee(SharingIdentitySet? set) => set switch
        {
            { User: { } user } => ("user", user),
            { Group: { } group } => ("group", group),
            _ => null,
        };

        private async Task<DomainRead> MailboxesAsync()
        {
            _mailboxes = (await MailboxLookups.ListAsync(backends, sharedOnly: false, cancellationToken).ConfigureAwait(false)).Data!.Mailboxes;
            foreach (MailboxInfo mailbox in _mailboxes)
            {
                Builder.Mailbox(mailbox);
            }

            return new DomainRead(_mailboxes.Count, []);
        }

        private async Task<DomainRead> GrantsAsync()
        {
            MailboxGrants grants = await MailboxLookups.AllGrantsAsync(backends, cancellationToken).ConfigureAwait(false);
            foreach (MailboxGrant grant in grants.Grants)
            {
                int mailbox = Builder.Mailbox(grant.Mailbox).Key;
                SnapshotObject holder = grant.Recipient is { } recipient
                    ? Builder.Object(KindOf(recipient.Type), recipient.ObjectId, recipient.UserPrincipalName, recipient.DisplayName, recipient.Address)
                    : Builder.Object("recipient", null, null, grant.Holder, null);
                Builder.Grants.Add((mailbox, JsonNamingPolicy.KebabCaseLower.ConvertName(grant.Right.ToString()), holder.Key));
            }

            return new DomainRead(Builder.Grants.Count, grants.Failed.Select(message => new Unread(message, FailureKind.CmdletFailed.ToCode())).ToList());
        }

        /// <summary>
        /// Each mailbox's calendar permissions over the Admin API (task 042), several mailboxes at once; a mailbox that
        /// fails is an item that failed. An entry's holder is named as the service names it, and matched to an object by
        /// its user principal name, its address or a display name only one object carries.
        /// </summary>
        private async Task<DomainRead> FolderPermissionsAsync()
        {
            if (!backends.HasAdminApi)
            {
                throw new AegisException(FailureKind.ServiceError, "the calendars' permissions need the Exchange Online Admin API, which this run does not have");
            }

            var failed = new List<Unread>();
            (MailboxInfo Mailbox, MailboxFolderPermissions? Folder)[] reads = await EachAsync(_mailboxes, async mailbox =>
            {
                try
                {
                    return (mailbox, (await MailboxLookups.FolderPermissionsAsync(backends, mailbox.PrimarySmtpAddress, null, cancellationToken).ConfigureAwait(false)).Data);
                }
                catch (AegisException exception) when (exception.Failure.Kind != FailureKind.Auth)
                {
                    lock (failed)
                    {
                        failed.Add(new Unread($"{mailbox.PrimarySmtpAddress}: {exception.Message}", exception.Failure.Code));
                    }

                    return (mailbox, null);
                }
            }).ConfigureAwait(false);
            Dictionary<string, SnapshotObject?> names = Builder.NameIndex();
            foreach ((MailboxInfo mailbox, MailboxFolderPermissions? folder) in reads.Where(read => read.Folder is not null))
            {
                int key = Builder.Mailbox(mailbox).Key;
                foreach (FolderPermissionEntry entry in folder!.Permissions)
                {
                    Builder.FolderPermissions.Add((key, folder.Folder, Named(names, entry.User)?.Key, entry.User, string.Join(",", entry.AccessRights)));
                }
            }

            failed.Sort((left, right) => string.CompareOrdinal(left.What, right.What));
            return new DomainRead(Builder.FolderPermissions.Count, failed);
        }

        private async Task<DomainRead> ForwardingAsync()
        {
            MailboxForwarders forwarders = (await MailboxLookups.ForwardersAsync(backends.Sidecar, cancellationToken).ConfigureAwait(false)).Data!;
            Dictionary<string, SnapshotObject?> names = Builder.NameIndex();
            foreach (Forwarder forwarder in forwarders.Forwarders)
            {
                int? target = forwarder.TargetAddress is { } address ? Named(names, address)?.Key : null;
                Builder.Forwarding.Add((
                    Builder.Mailbox(forwarder.Mailbox).Key,
                    JsonNamingPolicy.KebabCaseLower.ConvertName(forwarder.Kind.ToString()),
                    forwarder.Target,
                    forwarder.TargetAddress,
                    target,
                    forwarder.External,
                    forwarder.KeepsCopy));
            }

            return new DomainRead(Builder.Forwarding.Count, []);
        }

        /// <summary>
        /// The object a permission entry or a forwarding names: the user whose user principal name it is (a mailbox's
        /// address), else the one object of <see cref="SnapshotBuilder.NameIndex"/> that carries it; null for none.
        /// </summary>
        private SnapshotObject? Named(Dictionary<string, SnapshotObject?> names, string text) =>
            Builder.Find(null, text) ?? names.GetValueOrDefault(text);

        /// <summary>Reads each item, <see cref="SnapshotTakeOptions.Parallel"/> at once, the answers in the items' order.</summary>
        private async Task<TResult[]> EachAsync<TItem, TResult>(IEnumerable<TItem> items, Func<TItem, Task<TResult>> read)
        {
            using var gate = new SemaphoreSlim(options.Parallel);
            return await Task.WhenAll(items.Select(async item =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    return await read(item).ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            })).ConfigureAwait(false);
        }
    }
}
