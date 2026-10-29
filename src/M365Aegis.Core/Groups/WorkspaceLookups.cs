using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Groups;

/// <summary>The Teams and SharePoint lists of the groups domain (task 005): every team, the sites a search finds.</summary>
public static class WorkspaceLookups
{
    /// <summary>The permission <c>team list</c> needs.</summary>
    public const string TeamPermission = "Team.ReadBasic.All";

    /// <summary>The permission <c>site list</c> needs.</summary>
    public const string SitePermission = "Sites.Read.All";

    /// <summary>
    /// <c>team list</c>: every team of the tenant (<c>GET /teams</c>, which fills only the id, the name, the
    /// description and the visibility), by name.
    /// </summary>
    public static async Task<CommandResult<IReadOnlyList<TeamInfo>>> TeamsAsync(GraphClient graph, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        string path = GraphQuery.Path("/teams", ("$select", "id,displayName,description,visibility"));
        IReadOnlyList<GraphTeam> teams = await graph.ListAsync<GraphTeam>(GraphRequest.Get(path) with { Permission = TeamPermission }, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<TeamInfo> result = teams
            .Select(team => new TeamInfo(team.Id, team.DisplayName ?? team.Id, team.Visibility, team.Description))
            .OrderBy(team => team.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return CommandResult.Success(result);
    }

    /// <summary>
    /// <c>site list [--search &lt;words&gt;]</c>: the sites a SharePoint search finds (<c>GET /sites?search=</c>;
    /// <c>*</c> for every site), by name. With a delegated token the search is trimmed to the sites the signed-in
    /// account can open; listing every site of the tenant regardless (<c>getAllSites</c>) is application-only.
    /// </summary>
    public static async Task<CommandResult<IReadOnlyList<SiteInfo>>> SitesAsync(GraphClient graph, string? search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        string words = string.IsNullOrWhiteSpace(search) ? "*" : search.Trim();
        string path = GraphQuery.Path("/sites", ("search", words));
        IReadOnlyList<GraphSite> sites = await graph.ListAsync<GraphSite>(GraphRequest.Get(path) with { Permission = SitePermission }, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SiteInfo> result = sites
            .Select(site => new SiteInfo(site.Id, site.DisplayName ?? site.Name ?? site.Id, site.WebUrl, site.LastModifiedDateTime))
            .OrderBy(site => site.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(site => site.WebUrl, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return CommandResult.Success(result);
    }
}
