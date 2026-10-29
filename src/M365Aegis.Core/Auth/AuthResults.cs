namespace M365Aegis.Core.Auth;

/// <summary>One resource's token as <c>login</c> and <c>whoami</c> report it: the expiry and the scopes, never the token.</summary>
/// <param name="Resource">Which resource.</param>
/// <param name="ExpiresOn">When the token expires, or null when none was acquired.</param>
/// <param name="Scopes">The permissions the token carries, without the resource prefix.</param>
/// <param name="Origin">Where the token came from, or null when none was acquired.</param>
/// <param name="Error">Why there is no token for this resource, or null.</param>
public sealed record ResourceToken(
    Resource Resource,
    DateTimeOffset? ExpiresOn,
    IReadOnlyList<string> Scopes,
    TokenOrigin? Origin,
    string? Error)
{
    /// <summary>A token that was acquired.</summary>
    public static ResourceToken From(Resource resource, TokenResult token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return new(resource, token.ExpiresOn, Auth.Scopes.Permissions(token.Scopes), token.Origin, null);
    }

    /// <summary>A resource without a token.</summary>
    public static ResourceToken Failed(Resource resource, string error) => new(resource, null, [], null, error);
}

/// <summary>The result of <c>login</c> and <c>whoami</c>: who is signed in, and a token's state per resource.</summary>
/// <param name="Profile">The profile that was used.</param>
/// <param name="Account">The signed-in user principal name.</param>
/// <param name="DisplayName">The user's display name, or null.</param>
/// <param name="TenantId">The tenant id the token names, or null.</param>
/// <param name="ObjectId">The user's object id, or null.</param>
/// <param name="Tokens">One entry per resource, Graph first.</param>
public sealed record SignInInfo(
    string Profile,
    string Account,
    string? DisplayName,
    string? TenantId,
    string? ObjectId,
    IReadOnlyList<ResourceToken> Tokens);

/// <summary>The result of <c>logout</c>.</summary>
/// <param name="Profile">The profile that was used; it stays in the file.</param>
/// <param name="Account">The user principal name whose accounts were removed.</param>
/// <param name="RemovedAccounts">How many account records went: 0 when nothing was signed in.</param>
public sealed record SignOutInfo(string Profile, string Account, int RemovedAccounts);

/// <summary>What <c>login</c> was asked for.</summary>
/// <param name="ProfileName">The profile to use or to write; null for the default.</param>
/// <param name="Tenant">With <paramref name="ClientId"/> and <paramref name="Account"/>: write the profile first.</param>
/// <param name="ClientId">See <paramref name="Tenant"/>.</param>
/// <param name="Account">See <paramref name="Tenant"/>.</param>
/// <param name="DeviceCode">Skip the broker and sign in through a browser with a device code.</param>
public sealed record LoginOptions(string? ProfileName, string? Tenant, string? ClientId, string? Account, bool DeviceCode);
