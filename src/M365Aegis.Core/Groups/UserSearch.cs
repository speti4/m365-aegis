using System.Globalization;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;
using M365Aegis.Core.Snapshots;

namespace M365Aegis.Core.Groups;

/// <summary>
/// The window's live user search (task 112): the directory's users whose display name (a word of it), user principal
/// name or address starts with a text, from Microsoft Graph's <c>/users</c> with <c>$search</c>, for the access cards'
/// user picker. The window falls back to the snapshot's search (<see cref="SnapshotAccess.FindUsers"/>) when this one
/// cannot answer.
/// </summary>
public static class UserSearch
{
    /// <summary>The delegated permission the search needs: the one the group writes and the take already ask for.</summary>
    public const string Permission = "User.ReadBasic.All";

    /// <summary>How many users the search lists at most.</summary>
    public const int MatchLimit = SnapshotAccess.MatchLimit;

    private const string Fields = "id,displayName,userPrincipalName,mail";

    /// <summary>The properties the search matches the text against.</summary>
    private static readonly string[] SearchedFields = ["displayName", "userPrincipalName", "mail"];

    /// <summary>
    /// The users whose display name, a word of it, user principal name or address starts with the text, by display
    /// name, the limit at most and <see cref="UserMatches.More"/> when another one matches. <c>$search</c> tokenizes the
    /// display name (a word's start matches) and matches the start of the other two, in any case; it is an advanced
    /// query, so <c>ConsistencyLevel: eventual</c> and <c>$count</c> go with it. A quote in the text is the syntax's
    /// own, so it is dropped.
    /// </summary>
    /// <exception cref="AegisException">A <see cref="FailureKind.Usage"/> failure for a text with nothing to search for; Graph's failures (no sign-in, a permission missing, the network) as the client raises them.</exception>
    public static async Task<UserMatches> FindAsync(GraphClient graph, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(text);
        string prefix = text.Replace("\"", "", StringComparison.Ordinal).Trim();
        if (prefix.Length == 0)
        {
            throw new AegisException(FailureKind.Usage, "no text to search for", "type the start of a name or an address");
        }

        string search = string.Join(" OR ", SearchedFields.Select(field => $"\"{field}:{prefix}\""));
        string path = GraphQuery.Path(
            "/users",
            ("$search", search),
            ("$count", "true"),
            ("$orderby", "displayName"),
            ("$select", Fields),
            ("$top", (MatchLimit + 1).ToString(CultureInfo.InvariantCulture)));
        GraphRequest request = GraphRequest.Get(path) with { EventualConsistency = true, Permission = Permission };
        GraphPage<GraphSearchUser> page = await graph.GetAsync<GraphPage<GraphSearchUser>>(request, cancellationToken).ConfigureAwait(false)
            ?? new GraphPage<GraphSearchUser>();
        List<AccessUser> users = [.. page.Value.Select(user => new AccessUser(user.Id, user.DisplayName, user.UserPrincipalName, user.Mail))];
        return new UserMatches(UserSource.Graph, null, null, users.Take(MatchLimit).ToList(), users.Count > MatchLimit);
    }
}

/// <summary>A user as the search's <c>$select</c> returns it.</summary>
internal sealed record GraphSearchUser : GraphObject
{
    public string? Id { get; init; }

    public string? DisplayName { get; init; }

    public string? UserPrincipalName { get; init; }

    public string? Mail { get; init; }
}
