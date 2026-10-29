namespace M365Aegis.Core.Auth;

/// <summary>The two services one sign-in serves, each with a token of its own (D-003).</summary>
public enum Resource
{
    /// <summary>Microsoft Graph.</summary>
    Graph,

    /// <summary>Exchange Online: the Admin API and <c>Connect-ExchangeOnline -AccessToken</c> in the sidecar.</summary>
    Exchange,
}

/// <summary>
/// The scope each resource is asked for: <c>.default</c>, so the token carries every delegated permission the
/// tenant administrator consented to in the portal (task 002). The code names no Graph permission; the list
/// the domains need is in docs/development.md, and <c>whoami</c> shows what a token actually carries.
/// </summary>
public static class Scopes
{
    /// <summary>The Graph scope.</summary>
    public const string Graph = "https://graph.microsoft.com/.default";

    /// <summary>The Exchange Online scope (<c>Exchange.Manage</c>, <c>Exchange.ManageV2</c> by consent).</summary>
    public const string Exchange = "https://outlook.office365.com/.default";

    /// <summary>The scope of a resource.</summary>
    public static string For(Resource resource) => resource switch
    {
        Resource.Graph => Graph,
        Resource.Exchange => Exchange,
        _ => throw new ArgumentOutOfRangeException(nameof(resource), resource, "a resource without a scope"),
    };

    /// <summary>The scopes of the sign-in itself, which every token lists and no table needs to show.</summary>
    private static readonly HashSet<string> SignInScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".default", "openid", "profile", "email", "offline_access",
    };

    /// <summary>
    /// The permissions a token carries, as the result types show them: the granted scopes without their resource
    /// prefix (<c>https://graph.microsoft.com/User.Read</c> → <c>User.Read</c>), without the scopes of the sign-in
    /// itself (<c>openid</c>, <c>profile</c>, <c>.default</c>, …) and without repeats, in the order returned.
    /// </summary>
    public static IReadOnlyList<string> Permissions(IEnumerable<string> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        return scopes.Select(WithoutResource).Where(scope => !SignInScopes.Contains(scope)).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>A granted scope without its resource prefix.</summary>
    public static string WithoutResource(string scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (Uri.TryCreate(scope, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            return uri.AbsolutePath.TrimStart('/');
        }

        return scope;
    }
}
