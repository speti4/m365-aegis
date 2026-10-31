using System.Text.Json;
using System.Text.Json.Serialization;
using M365Aegis.Core.Graph;

namespace M365Aegis.Core.Snapshots;

/// <summary>A user as <c>GET /users</c> returns it to the snapshot's <c>$select</c>.</summary>
internal sealed record SnapshotUser : GraphObject
{
    public string Id { get; init; } = "";

    public string? DisplayName { get; init; }

    public string? UserPrincipalName { get; init; }

    public string? Mail { get; init; }
}

/// <summary>A site's document library as <c>GET /sites/{id}/drive</c> returns it: its id and its owner.</summary>
internal sealed record SnapshotDrive : GraphObject
{
    public string Id { get; init; } = "";

    /// <summary>The owner: <c>owner.group</c> is the Microsoft 365 group behind a group-connected site.</summary>
    public SharingIdentitySet? Owner { get; init; }
}

/// <summary>One page of a drive's <c>delta</c>: the items, and the next page or the delta link on the last one.</summary>
internal sealed record DeltaPage
{
    public IReadOnlyList<DriveItem> Value { get; init; } = [];

    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; init; }

    [JsonPropertyName("@odata.deltaLink")]
    public string? DeltaLink { get; init; }
}

/// <summary>
/// An item of a drive's <c>delta</c>: with <c>Prefer: hierarchicalsharing</c> the <c>shared</c> facet marks an item
/// whose sharing differs from its parent's (task 092); its own <c>scope</c> is not a link's and is not kept.
/// </summary>
internal sealed record DriveItem
{
    public string Id { get; init; } = "";

    public string? Name { get; init; }

    public JsonElement? Shared { get; init; }

    /// <summary>Set on an item a later <c>delta</c> reports as removed (task 099): its rows are dropped.</summary>
    public JsonElement? Deleted { get; init; }

    public DriveItemParent ParentReference { get; init; } = new(null);
}

/// <summary>Where an item sits: the path of its folder (<c>/drives/{id}/root:/Reports</c>).</summary>
internal sealed record DriveItemParent(string? Path);

/// <summary>
/// One permission of a shared item (<c>GET /drives/{id}/items/{id}/permissions</c>): a sharing link with its grantees,
/// or a grant to a user or a group; an inherited one names its parent.
/// </summary>
internal sealed record DrivePermission
{
    public IReadOnlyList<string> Roles { get; init; } = [];

    public SharingLinkFacet? Link { get; init; }

    public DateTimeOffset? ExpirationDateTime { get; init; }

    public bool? HasPassword { get; init; }

    public SharingIdentitySet? GrantedToV2 { get; init; }

    public IReadOnlyList<SharingIdentitySet> GrantedToIdentitiesV2 { get; init; } = [];

    public JsonElement? InheritedFrom { get; init; }
}

/// <summary>A sharing link's scope and type; its URL is never read (D-011).</summary>
internal sealed record SharingLinkFacet(string? Scope, string? Type);

/// <summary>
/// Who a permission is granted to: a user or a group of the directory. SharePoint's own identities (<c>siteUser</c>,
/// <c>siteGroup</c>: the site's Owners, Members and Visitors, which the group behind the site stands for) are not read.
/// </summary>
internal sealed record SharingIdentitySet(SharingIdentity? User, SharingIdentity? Group);

/// <summary>An identity of a permission: the directory's object id when it is one, its name and address.</summary>
internal sealed record SharingIdentity(string? Id, string? DisplayName, string? Email);
