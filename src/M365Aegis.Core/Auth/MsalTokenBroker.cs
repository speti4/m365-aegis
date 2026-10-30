using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using M365Aegis.Core.Diagnostics;
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
/// profile) never loads the library. Under <c>--verbose</c> (<see cref="LogSink.Verbose"/>) the library's own log,
/// without PII, goes into the home folder's <c>logs</c> folder through the scrubbing sink (task 025); without the
/// flag no logging callback is registered at all. The window passes its own handle and a way onto its UI thread,
/// where MSAL wants the account picker's call to start (task 040; <c>MsalError.WamUiThread</c>).
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

    /// <summary>
    /// Why the members that call MSAL itself are excluded from the coverage (task 070): each one builds the real
    /// public client (the Windows account manager, the DPAPI cache file) and talks to the real sign-in service, which
    /// no test may reach. What they do around the library is tested: the exception mapping (<see cref="Guard"/>), the
    /// result (<see cref="ToResult"/>), the window thread, the log; the flows over them run on a fake
    /// <see cref="ITokenBroker"/>.
    /// </summary>
    private const string Untestable = "calls MSAL.NET against the real broker and sign-in service (docs/development.md, Coverage)";

    private readonly TenantProfile _profile;
    private readonly string _home;
    private readonly Func<IntPtr> _parentWindow;
    private readonly Func<Func<Task>, Task>? _onWindowThread;
    private readonly LogSink? _log;
    private readonly Lazy<Task<IPublicClientApplication>> _application;
    private readonly Lazy<Task<IPublicClientApplication>> _cacheOnlyApplication;

    private MsalTokenBroker(TenantProfile profile, string home, Func<IntPtr> parentWindow, Func<Func<Task>, Task>? onWindowThread, LogSink? log)
    {
        _profile = profile;
        _home = home;
        _parentWindow = parentWindow;
        _onWindowThread = onWindowThread;
        _log = log;
        _application = new Lazy<Task<IPublicClientApplication>>(() => BuildAsync(withBroker: true));
        _cacheOnlyApplication = new Lazy<Task<IPublicClientApplication>>(() => BuildAsync(withBroker: false));
    }

    /// <summary>A broker for a profile, with the console's root owner as the picker's parent window.</summary>
    public static ITokenBroker Create(TenantProfile profile, string home) => Create(profile, home, ConsoleWindow.RootOwner);

    /// <summary>A broker for a profile with a parent window of the caller's (the window of the GUI).</summary>
    public static ITokenBroker Create(TenantProfile profile, string home, Func<IntPtr> parentWindow) =>
        Create(profile, home, parentWindow, LogSink.Verbose);

    /// <summary>A broker that logs MSAL into <paramref name="log"/>, or not at all when it is null.</summary>
    public static ITokenBroker Create(TenantProfile profile, string home, Func<IntPtr> parentWindow, LogSink? log)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        ArgumentNullException.ThrowIfNull(parentWindow);
        return new MsalTokenBroker(profile, home, parentWindow, null, log);
    }

    /// <summary>
    /// A broker for the window: <paramref name="parentWindow"/> is the window's handle, and
    /// <paramref name="onWindowThread"/> starts a call on the thread that owns it and completes when the call does,
    /// so the account picker is started from the window's UI thread, as MSAL asks of a UI application (task 040).
    /// </summary>
    public static ITokenBroker Create(TenantProfile profile, string home, Func<IntPtr> parentWindow, Func<Func<Task>, Task> onWindowThread)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        ArgumentNullException.ThrowIfNull(parentWindow);
        ArgumentNullException.ThrowIfNull(onWindowThread);
        return new MsalTokenBroker(profile, home, parentWindow, onWindowThread, LogSink.Verbose);
    }

    /// <summary>
    /// Registers the library's log on a builder when there is a sink: PII logging off, no platform logging, every
    /// level (the broker's error codes and the step that failed are at the verbose level), and the profile's tenant,
    /// client id and account registered with the sink for redaction. Without a sink the builder is returned as it is.
    /// </summary>
    internal static PublicClientApplicationBuilder WithLog(PublicClientApplicationBuilder builder, LogSink? log, TenantProfile profile)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(profile);
        if (log is null)
        {
            return builder;
        }

        log.Redact(profile.Tenant);
        log.Redact(profile.ClientId);
        log.Redact(profile.Account);
        return builder.WithLogging(ToSink(log), LogLevel.Verbose, enablePiiLogging: false, enableDefaultPlatformLogging: false);
    }

    /// <summary>The callback MSAL calls: a line marked as carrying PII is dropped even though PII logging is off.</summary>
    internal static LogCallback ToSink(LogSink log)
    {
        ArgumentNullException.ThrowIfNull(log);
        return (level, message, containsPii) =>
        {
            if (!containsPii)
            {
                log.Write("msal", $"{level}: {message}");
            }
        };
    }

    /// <inheritdoc/>
    [ExcludeFromCodeCoverage(Justification = Untestable)]
    public Task<TokenResult> AcquireSilentWithOperatingSystemAccountAsync(string scope, CancellationToken cancellationToken) =>
        GuardAsync(async application =>
            await application.AcquireTokenSilent([scope], PublicClientApplication.OperatingSystemAccount)
                .ExecuteAsync(cancellationToken).ConfigureAwait(false));

    /// <inheritdoc/>
    [ExcludeFromCodeCoverage(Justification = Untestable)]
    public async Task<IReadOnlyList<AccountInfo>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        IPublicClientApplication application = await _application.Value.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        IEnumerable<IAccount> accounts = await Guard(application.GetAccountsAsync).ConfigureAwait(false);
        return accounts.Select(ToAccountInfo).ToList();
    }

    /// <inheritdoc/>
    [ExcludeFromCodeCoverage(Justification = Untestable)]
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
    [ExcludeFromCodeCoverage(Justification = Untestable)]
    public async Task<TokenResult> AcquireInteractiveAsync(string scope, string loginHint, CancellationToken cancellationToken)
    {
        // The client is built first, off the window's thread; only the picker's call starts on it.
        IPublicClientApplication application = await _application.Value.ConfigureAwait(false);
        AuthenticationResult result = await Guard(() => OnWindowThreadAsync(() =>
            application.AcquireTokenInteractive([scope])
                .WithLoginHint(loginHint)
                .ExecuteAsync(cancellationToken))).ConfigureAwait(false);
        return ToResult(result);
    }

    /// <inheritdoc/>
    [ExcludeFromCodeCoverage(Justification = Untestable)]
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
    [ExcludeFromCodeCoverage(Justification = Untestable)]
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

    [ExcludeFromCodeCoverage(Justification = Untestable)]
    private static async Task<IAccount> FindAsync(IPublicClientApplication application, AccountInfo account, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IAccount? known = await Guard(() => application.GetAccountAsync(account.HomeAccountId)).ConfigureAwait(false);
        return known ?? throw new BrokerException(BrokerErrorKind.UiRequired, "account_not_found", $"{account.Username} is no longer a known account");
    }

    internal static AccountInfo ToAccountInfo(IAccount account) =>
        new(account.Username, account.HomeAccountId.Identifier, account.HomeAccountId.ObjectId, account.HomeAccountId.TenantId);

    internal static TokenResult ToResult(AuthenticationResult result)
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
    internal static async Task<T> Guard<T>(Func<Task<T>> call)
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

    /// <summary>Starts a call on the window's thread when there is one (the window), here otherwise (a console).</summary>
    internal async Task<T> OnWindowThreadAsync<T>(Func<Task<T>> call)
    {
        if (_onWindowThread is null)
        {
            return await call().ConfigureAwait(false);
        }

        Task<T>? started = null;
        await _onWindowThread(() => started = call()).ConfigureAwait(false);
        return await started!.ConfigureAwait(false);
    }

    [ExcludeFromCodeCoverage(Justification = Untestable)]
    private async Task<TokenResult> GuardAsync(Func<IPublicClientApplication, Task<AuthenticationResult>> call)
    {
        IPublicClientApplication application = await _application.Value.ConfigureAwait(false);
        AuthenticationResult result = await Guard(() => call(application)).ConfigureAwait(false);
        return ToResult(result);
    }

    [ExcludeFromCodeCoverage(Justification = Untestable)]
    private async Task<IPublicClientApplication> BuildAsync(bool withBroker)
    {
        // The redirect URI the broker needs (ms-appx-web://microsoft.aad.brokerplugin/<client id>) is a property of
        // the app registration; MSAL derives it itself. The default one serves the browser fallback.
        PublicClientApplicationBuilder builder = PublicClientApplicationBuilder.Create(_profile.ClientId)
            .WithTenantId(_profile.Tenant)
            .WithDefaultRedirectUri()
            .WithParentActivityOrWindow(_parentWindow);
        builder = WithLog(builder, _log, _profile);
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
