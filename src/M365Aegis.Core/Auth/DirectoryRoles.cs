using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Auth;

/// <summary>
/// The signed-in account's active Microsoft Entra roles for the doctor's <c>roles</c> row (task 111): the directory
/// roles of <c>/me/transitiveMemberOf</c>, so a role that comes through a role-assignable group counts too. The call
/// needs only <c>User.Read</c>; the Graph token itself is never read.
/// </summary>
public static class DirectoryRoles
{
    /// <summary>The permission the call needs, already on the setup guide's first row.</summary>
    public const string Permission = "User.Read";

    /// <summary>
    /// The role template ids of the account's active directory roles, each once. A role that comes back without its
    /// template id fails the read rather than look like a missing role.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> ReadAsync(GraphClient graph, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        string path = GraphQuery.Path(
            "/me/transitiveMemberOf/microsoft.graph.directoryRole",
            ("$count", "true"),
            ("$select", "id,displayName,roleTemplateId"),
            ("$top", "999"));
        GraphRequest request = GraphRequest.Get(path) with { EventualConsistency = true, Permission = Permission };
        IReadOnlyList<GraphDirectoryRole> roles = await graph.ListAsync<GraphDirectoryRole>(request, cancellationToken).ConfigureAwait(false);
        var ids = new List<Guid>();
        foreach (GraphDirectoryRole role in roles)
        {
            if (!Guid.TryParse(role.RoleTemplateId, out Guid id))
            {
                throw new AegisException(FailureKind.ServiceError,
                    $"Microsoft Graph returned a directory role without its template id ({role.Id})",
                    "the roles are not read with this token; the commands still show a missing role when they run");
            }

            if (!ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}

/// <summary>A directory role as <c>transitiveMemberOf</c> returns it to the read's <c>$select</c>.</summary>
internal sealed record GraphDirectoryRole : GraphObject
{
    public string Id { get; init; } = "";

    public string? DisplayName { get; init; }

    public string? RoleTemplateId { get; init; }
}
