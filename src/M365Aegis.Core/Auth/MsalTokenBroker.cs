using System.Security.Claims;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using Microsoft.Identity.Client.Extensions.Msal;
using TenantProfile = M365Aegis.Core.Config.TenantProfile;

namespace M365Aegis.Core.Auth;

/// <summary>
/// The one place MSAL.NET is called (D-003): a public client of the profile's app registration in the profile's
/// tenant, the Windows account manager through the broker package with the console's root owner as the parent
/// window, and the cache in <c>msal-cache.bin</c> under the home folder (DPAPI on Windows, a cross-process lock,
/// shared by the command line and the window). MSAL's exceptions are mapped to <see cref="BrokerException"/>
/// here and nowhere else. Nothing is built until the first call, so a command that fails before a token (no
/// profile) never loads the library.
/// </summary>
public sealed class MsalTokenBroker : ITokenBroker
{
    /// <summary>The cache file under the home folder.</summary>
    public const string CacheFileName = "msal-cache.bin";

    /// <summary>
    /// The broker's error when an interactive call has no window. A string literal in the broker package (there is
    /// no <see cref="MsalError"/> constant for it), so it is written once, here.
    /// </summary>
    public const string WindowHandleRequiredCode = "window_handle_required";

    private readonly TenantProfile _profile;
    private readonly string _home;
    private readonly Func<IntPtr> _parentWindow;
    private readonly Lazy<Task<IPublicClientApplication>> _application;
    private readonly Lazy<Task<IPublicClientApplication>> _cacheOnlyApplication;

    private MsalTokenBroker(TenantProfile profile, string home, Func<IntPtr> parentWindow)
    {
        _profile = profile;
        _home = home;
        _parentWindow = parentWindow;
        _application = new Lazy<Task<IPublicClientApplication>>(() => BuildAsync(withBroker: true));
        _cacheOnlyApplication = new Lazy<Task<IPublicClientApplication>>(() => BuildAsync(withBroker: false));
    }

    /// <summary>A broker for a profile, with the console's root owner as the picker's parent window.</summary>
    public static ITokenBroker Create(TenantProfile profile, string home) => Create(profile, home, ConsoleWindow.RootOwner);

    /// <summary>A broker for a profile with a parent window of the caller's (the window of the GUI).</summary>
    public static ITokenBroker Create(TenantProfile profile, string home, Func<IntPtr> parentWindow)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        ArgumentNullException.ThrowIfNull(parentWindow);
        return new MsalTokenBroker(profile, home, parentWindow);
    }

    /// <inheritdoc/>
    public Task<TokenResult> AcquireSilentWithOperatingSystemAccountAsync(string scope, CancellationToken cancellationToken) =>
        GuardAsync(async application =>
            await application.AcquireTokenSilent([scope], PublicClientApplication.OperatingSystemAccount)
                .ExecuteAsync(cancellationToken).ConfigureAwait(false));

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AccountInfo>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        IPublicClientApplication application = await _application.Value.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        IEnumerable<IAccount> accounts = await Guard(application.GetAccountsAsync).ConfigureAwait(false);
        return accounts.Select(ToAccountInfo).ToList();
    }

    /// <inheritdoc/>
    public async Task<TokenResult> AcquireSilentAsync(string scope, AccountInfo account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        try
        {
            return await GuardAsync(async application =>
            {
                IAccount known = await FindAsync(application, account, cancellationToken).ConfigureAwait(false);
                return await application.AcquireTokenSilent([scope], known).ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (BrokerException exception) when (exception.Kind is BrokerErrorKind.NoPrimaryRefreshToken or BrokerErrorKind.UiRequired)
        {
            // With the broker enabled, a silent call for an account the broker lists is answered by the broker alone,
            // and a refresh token the file cache holds from a device-code sign-in is never tried (measured 2026-10-29).
            // The same cache through a client without the broker reaches it, so a sign-in from a redirected shell
            // serves the later runs too.
            IPublicClientApplication cacheOnly = await _cacheOnlyApplication.Value.ConfigureAwait(false);
            IAccount? cached = await Guard(() => cacheOnly.GetAccountAsync(account.HomeAccountId)).ConfigureAwait(false);
            if (cached is null)
            {
                throw;
            }

            AuthenticationResult result = await Guard(() => cacheOnly.AcquireTokenSilent([scope], cached).ExecuteAsync(cancellationToken)).ConfigureAwait(false);
            return ToResult(result);
        }
    }

    /// <inheritdoc/>
    public Task<TokenResult> AcquireInteractiveAsync(string scope, string loginHint, CancellationToken cancellationToken) =>
        GuardAsync(async application =>
            await application.AcquireTokenInteractive([scope])
                .WithLoginHint(loginHint)
                .ExecuteAsync(cancellationToken).ConfigureAwait(false));

    /// <inheritdoc/>
    public Task<TokenResult> AcquireWithDeviceCodeAsync(string scope, Func<DeviceCodePrompt, Task> prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        return GuardAsync(async application =>
            await application.AcquireTokenWithDeviceCode(
                    [scope],
                    code => prompt(new DeviceCodePrompt(new Uri(code.VerificationUrl), code.UserCode, code.Message, code.ExpiresOn)))
                .ExecuteAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(AccountInfo account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        IPublicClientApplication application = await _application.Value.ConfigureAwait(false);
        IAccount known = await FindAsync(application, account, cancellationToken).ConfigureAwait(false);
        await Guard(async () =>
        {
            await application.RemoveAsync(known).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    private static async Task<IAccount> FindAsync(IPublicClientApplication application, AccountInfo account, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IAccount? known = await Guard(() => application.GetAccountAsync(account.HomeAccountId)).ConfigureAwait(false);
        return known ?? throw new BrokerException(BrokerErrorKind.UiRequired, "account_not_found", $"{account.Username} is no longer a known account");
    }

    private static AccountInfo ToAccountInfo(IAccount account) =>
        new(account.Username, account.HomeAccountId.Identifier, account.HomeAccountId.ObjectId, account.HomeAccountId.TenantId);

    private static TokenResult ToResult(AuthenticationResult result)
    {
        TokenOrigin origin = result.AuthenticationResultMetadata?.TokenSource switch
        {
            TokenSource.Broker => TokenOrigin.Broker,
            TokenSource.Cache => TokenOrigin.Cache,
            _ => TokenOrigin.IdentityProvider,
        };
        ClaimsPrincipal? claims = result.ClaimsPrincipal;
        return new TokenResult(
            result.AccessToken,
            result.ExpiresOn,
            result.Scopes.ToList(),
            result.TenantId ?? claims?.FindFirst("tid")?.Value,
            ToAccountInfo(result.Account),
            origin,
            claims?.FindFirst("name")?.Value);
    }

    /// <summary>Runs an MSAL call and maps its exceptions.</summary>
    private static async Task<T> Guard<T>(Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (MsalUiRequiredException exception)
        {
            BrokerErrorKind kind = exception.ErrorCode == MsalError.FailedToAcquireTokenSilentlyFromBroker
                ? BrokerErrorKind.NoPrimaryRefreshToken
                : BrokerErrorKind.UiRequired;
            throw new BrokerException(kind, exception.ErrorCode, exception.Message, exception);
        }
        catch (MsalClientException exception) when (exception.ErrorCode == WindowHandleRequiredCode)
        {
            throw new BrokerException(BrokerErrorKind.WindowHandleRequired, exception.ErrorCode, exception.Message, exception);
        }
        catch (MsalClientException exception) when (exception.ErrorCode == MsalError.AuthenticationCanceledError)
        {
            throw new BrokerException(BrokerErrorKind.Cancelled, exception.ErrorCode, exception.Message, exception);
        }
        catch (MsalException exception)
        {
            throw new BrokerException(BrokerErrorKind.Other, exception.ErrorCode, exception.Message, exception);
        }
    }

    private async Task<TokenResult> GuardAsync(Func<IPublicClientApplication, Task<AuthenticationResult>> call)
    {
        IPublicClientApplication application = await _application.Value.ConfigureAwait(false);
        AuthenticationResult result = await Guard(() => call(application)).ConfigureAwait(false);
        return ToResult(result);
    }

    private async Task<IPublicClientApplication> BuildAsync(bool withBroker)
    {
        // The redirect URI the broker needs (ms-appx-web://microsoft.aad.brokerplugin/<client id>) is a property of
        // the app registration; MSAL derives it itself. The default one serves the browser fallback.
        PublicClientApplicationBuilder builder = PublicClientApplicationBuilder.Create(_profile.ClientId)
            .WithTenantId(_profile.Tenant)
            .WithDefaultRedirectUri()
            .WithParentActivityOrWindow(_parentWindow);
        if (withBroker)
        {
            builder = builder.WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows)
            {
                ListOperatingSystemAccounts = true,
                Title = ProductInfo.Name,
            });
        }

        IPublicClientApplication application = builder.Build();

        // The broker holds the refresh token; the file holds the accounts and the id tokens, and the tokens of a
        // device-code sign-in. DPAPI on Windows; the plain file elsewhere, where the tool does not ship anyway.
        Directory.CreateDirectory(_home);
        var storage = new StorageCreationPropertiesBuilder(CacheFileName, _home);
        if (!OperatingSystem.IsWindows())
        {
            storage = storage.WithLinuxUnprotectedFile();
        }

        MsalCacheHelper cache = await MsalCacheHelper.CreateAsync(storage.Build()).ConfigureAwait(false);
        cache.RegisterCache(application.UserTokenCache);
        return application;
    }
}
