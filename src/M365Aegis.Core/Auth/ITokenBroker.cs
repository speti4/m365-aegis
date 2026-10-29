namespace M365Aegis.Core.Auth;

/// <summary>An account the broker or the cache knows, without any secret.</summary>
/// <param name="Username">The user principal name.</param>
/// <param name="HomeAccountId">MSAL's identifier of the account (<c>oid.tid</c>), the key for a silent call.</param>
/// <param name="ObjectId">The user's object id, or null when the broker did not say.</param>
/// <param name="TenantId">The home tenant id, or null when the broker did not say.</param>
public sealed record AccountInfo(string Username, string HomeAccountId, string? ObjectId, string? TenantId);

/// <summary>Where a token came from.</summary>
public enum TokenOrigin
{
    /// <summary>The Windows account manager answered.</summary>
    Broker,

    /// <summary>The cache under the home folder answered.</summary>
    Cache,

    /// <summary>The identity provider answered (an interactive or device-code round trip).</summary>
    IdentityProvider,
}

/// <summary>
/// A token as the broker hands it over. <see cref="AccessToken"/> stays inside the Core: a result type of a command
/// never carries it, and the record's <c>ToString</c> leaves it out so it cannot reach a log by accident.
/// </summary>
/// <param name="AccessToken">The bearer token for the resource.</param>
/// <param name="ExpiresOn">When the token expires.</param>
/// <param name="Scopes">The scopes the token carries, in the form the service returned them.</param>
/// <param name="TenantId">The tenant that issued the token, or null.</param>
/// <param name="Account">The account the token belongs to.</param>
/// <param name="Origin">Where the token came from.</param>
/// <param name="DisplayName">The user's display name from the id token, or null.</param>
public sealed record TokenResult(
    string AccessToken,
    DateTimeOffset ExpiresOn,
    IReadOnlyList<string> Scopes,
    string? TenantId,
    AccountInfo Account,
    TokenOrigin Origin,
    string? DisplayName)
{
    /// <inheritdoc/>
    public override string ToString() =>
        $"{nameof(TokenResult)} {{ Account = {Account.Username}, ExpiresOn = {ExpiresOn:u}, Origin = {Origin} }}";
}

/// <summary>What a device-code sign-in shows the user: open the page, type the code.</summary>
/// <param name="VerificationUri">The page to open.</param>
/// <param name="UserCode">The code to type there.</param>
/// <param name="Message">The service's own sentence, which already contains both.</param>
/// <param name="ExpiresOn">When the code stops working.</param>
public sealed record DeviceCodePrompt(Uri VerificationUri, string UserCode, string Message, DateTimeOffset ExpiresOn);

/// <summary>Why the broker could not answer, in the terms the sign-in flow decides on.</summary>
public enum BrokerErrorKind
{
    /// <summary>The operating system account cannot sign in silently: no primary refresh token on this machine.</summary>
    NoPrimaryRefreshToken,

    /// <summary>A silent call needs the user: no cached account, an expired session, a new consent.</summary>
    UiRequired,

    /// <summary>An interactive call has no window to attach to (no console window); device code takes over.</summary>
    WindowHandleRequired,

    /// <summary>The user closed the account picker.</summary>
    Cancelled,

    /// <summary>Anything else; <see cref="BrokerException.Code"/> carries the library's code.</summary>
    Other,
}

/// <summary>A failure of the token broker, with the library's own error code kept for the message.</summary>
public sealed class BrokerException : Exception
{
    /// <summary>A failure of the given kind.</summary>
    public BrokerException(BrokerErrorKind kind, string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Code = code;
    }

    /// <summary>What the flow decides on.</summary>
    public BrokerErrorKind Kind { get; }

    /// <summary>The library's error code (<c>failed_to_acquire_token_silently_from_broker</c>, …).</summary>
    public string Code { get; }
}

/// <summary>
/// The token source behind the sign-in flow: MSAL.NET with the Windows account manager in the product
/// (<see cref="MsalTokenBroker"/>), a scripted fake in the tests. One scope per call, as the identity platform
/// issues one token per resource.
/// </summary>
public interface ITokenBroker
{
    /// <summary>Silent, as the account Windows is signed in with (needs a primary refresh token).</summary>
    Task<TokenResult> AcquireSilentWithOperatingSystemAccountAsync(string scope, CancellationToken cancellationToken);

    /// <summary>The accounts the broker and the cache know.</summary>
    Task<IReadOnlyList<AccountInfo>> GetAccountsAsync(CancellationToken cancellationToken);

    /// <summary>Silent, for a known account.</summary>
    Task<TokenResult> AcquireSilentAsync(string scope, AccountInfo account, CancellationToken cancellationToken);

    /// <summary>The account picker, with the profile's account as the hint.</summary>
    Task<TokenResult> AcquireInteractiveAsync(string scope, string loginHint, CancellationToken cancellationToken);

    /// <summary>Device code; <paramref name="prompt"/> shows the page and the code to the user.</summary>
    Task<TokenResult> AcquireWithDeviceCodeAsync(string scope, Func<DeviceCodePrompt, Task> prompt, CancellationToken cancellationToken);

    /// <summary>Forgets an account: the cache, and the broker's record for this app.</summary>
    Task RemoveAsync(AccountInfo account, CancellationToken cancellationToken);
}
