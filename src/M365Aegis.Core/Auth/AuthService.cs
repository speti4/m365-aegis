using M365Aegis.Core.Config;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Auth;

/// <summary>
/// The sign-in as the commands and the other components use it: <c>login</c>, <c>whoami</c>, <c>logout</c>, and the
/// access token the Graph client and the sidecar ask for. One profile from <c>config.json</c>, one
/// <see cref="ITokenBroker"/> per profile from the factory (MSAL in the product, a fake in the tests), two tokens
/// per sign-in: Graph, then Exchange Online silently for the same account.
/// </summary>
public sealed class AuthService
{
    private static readonly Resource[] Resources = [Resource.Graph, Resource.Exchange];

    private readonly Func<TenantProfile, ITokenBroker> _brokerFactory;

    /// <summary>A service over the given home folder and broker factory.</summary>
    public AuthService(string home, Func<TenantProfile, ITokenBroker> brokerFactory)
    {
        ArgumentNullException.ThrowIfNull(brokerFactory);
        Profiles = new ProfileStore(home);
        _brokerFactory = brokerFactory;
    }

    /// <summary>The profiles of the home folder.</summary>
    public ProfileStore Profiles { get; }

    /// <summary>The service of the real home folder with MSAL and the Windows account manager behind it.</summary>
    public static AuthService Create()
    {
        string home = AegisHome.Resolve();
        return new AuthService(home, profile => MsalTokenBroker.Create(profile, home));
    }

    /// <summary>
    /// <c>login</c>: writes the profile when the three flags are given (all or none), then signs in as far as the
    /// options allow, and reports the account and both tokens.
    /// </summary>
    public async Task<SignInInfo> LoginAsync(LoginOptions options, Func<DeviceCodePrompt, Task> prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(prompt);

        TenantProfile profile = ResolveForLogin(options);
        ITokenBroker broker = _brokerFactory(profile);
        SignInMode mode = options.DeviceCode ? SignInMode.DeviceCode : SignInMode.Interactive;
        return await SignInAsync(broker, profile, mode, prompt, cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>whoami</c>: the silent paths only; a failure says to run <c>login</c>.</summary>
    public Task<SignInInfo> WhoAmIAsync(string? profileName, CancellationToken cancellationToken)
    {
        TenantProfile profile = Profiles.Resolve(profileName);
        return SignInAsync(_brokerFactory(profile), profile, SignInMode.SilentOnly, NoPrompt, cancellationToken);
    }

    /// <summary><c>logout</c>: forgets every account record of the profile's user; the profile stays.</summary>
    public async Task<SignOutInfo> LogoutAsync(string? profileName, CancellationToken cancellationToken)
    {
        TenantProfile profile = Profiles.Resolve(profileName);
        int removed = await SignOutAsync(profile, cancellationToken).ConfigureAwait(false);
        return new SignOutInfo(profile.Name, profile.Account, removed);
    }

    /// <summary>
    /// <c>profile remove</c>: signs the profile's account out, then deletes the profile from the file. The sign-out
    /// is skipped when another profile uses the same app registration and account, which would be signed out with it;
    /// when the sign-out fails, the profile stays.
    /// </summary>
    public async Task<ProfileRemoval> RemoveProfileAsync(string profileName, CancellationToken cancellationToken)
    {
        TenantProfile profile = Profiles.Find(profileName);
        bool shared = Profiles.List().Any(other =>
            other.Name != profile.Name
            && string.Equals(other.ClientId, profile.ClientId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(other.Account, profile.Account, StringComparison.OrdinalIgnoreCase));
        int removed = shared ? 0 : await SignOutAsync(profile, cancellationToken).ConfigureAwait(false);
        string? defaultAfter = Profiles.Remove(profile.Name);
        return new ProfileRemoval(profile.Name, profile.Account, !shared, removed, defaultAfter);
    }

    /// <summary>
    /// A bearer token for a resource, silently (the Graph client and the sidecar call this); an
    /// <see cref="FailureKind.Auth"/> failure when there is none, so a lookup never prompts.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(Resource resource, string? profileName, CancellationToken cancellationToken)
    {
        TokenResult token = await GetTokenAsync(resource, profileName, cancellationToken).ConfigureAwait(false);
        return token.AccessToken;
    }

    /// <summary>
    /// The token for a resource with its expiry, scopes and account, silently: the Exchange Online sidecar renews
    /// its connection before <see cref="TokenResult.ExpiresOn"/> (task 015). The token stays in the core.
    /// </summary>
    public Task<TokenResult> GetTokenAsync(Resource resource, string? profileName, CancellationToken cancellationToken)
    {
        TenantProfile profile = Profiles.Resolve(profileName);
        return SignInFlow.RunAsync(_brokerFactory(profile), profile, resource, SignInMode.SilentOnly, NoPrompt, cancellationToken);
    }

    private static Task NoPrompt(DeviceCodePrompt prompt) => Task.CompletedTask;

    /// <summary>Removes every account record of the profile's user; a broker failure is an auth failure of the contract.</summary>
    private async Task<int> SignOutAsync(TenantProfile profile, CancellationToken cancellationToken)
    {
        ITokenBroker broker = _brokerFactory(profile);
        try
        {
            IReadOnlyList<AccountInfo> accounts = await broker.GetAccountsAsync(cancellationToken).ConfigureAwait(false);
            int removed = 0;
            foreach (AccountInfo account in accounts.Where(account => string.Equals(account.Username, profile.Account, StringComparison.OrdinalIgnoreCase)))
            {
                await broker.RemoveAsync(account, cancellationToken).ConfigureAwait(false);
                removed++;
            }

            return removed;
        }
        catch (BrokerException exception)
        {
            throw new AegisException(
                FailureKind.Auth,
                $"the sign-out of {profile.Account} failed ({exception.Code}: {exception.Message})",
                "try again; the profile is unchanged",
                exception);
        }
    }

    private static async Task<SignInInfo> SignInAsync(
        ITokenBroker broker, TenantProfile profile, SignInMode mode, Func<DeviceCodePrompt, Task> prompt, CancellationToken cancellationToken)
    {
        // Graph goes through the full flow; the second resource is a silent call for the account that just signed in.
        TokenResult first = await SignInFlow.RunAsync(broker, profile, Resources[0], mode, prompt, cancellationToken).ConfigureAwait(false);
        var tokens = new List<ResourceToken> { ResourceToken.From(Resources[0], first) };
        foreach (Resource resource in Resources.Skip(1))
        {
            try
            {
                TokenResult token = await broker.AcquireSilentAsync(Scopes.For(resource), first.Account, cancellationToken).ConfigureAwait(false);
                tokens.Add(ResourceToken.From(resource, token));
            }
            catch (BrokerException exception)
            {
                tokens.Add(ResourceToken.Failed(resource, $"{exception.Code}: {exception.Message}"));
            }
        }

        return new SignInInfo(
            profile.Name,
            first.Account.Username,
            first.DisplayName,
            first.TenantId ?? first.Account.TenantId,
            first.Account.ObjectId,
            tokens);
    }

    private TenantProfile ResolveForLogin(LoginOptions options)
    {
        bool anyFlag = options.Tenant is not null || options.ClientId is not null || options.Account is not null;
        if (!anyFlag)
        {
            return Profiles.Resolve(options.ProfileName);
        }

        if (string.IsNullOrWhiteSpace(options.Tenant) || string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.Account))
        {
            throw new AegisException(
                FailureKind.Usage,
                "--tenant, --client-id and --account go together",
                "give all three to write the profile, or none to use the saved one");
        }

        var profile = new TenantProfile(
            options.ProfileName ?? ProfileStore.DefaultProfileName,
            options.Tenant.Trim(),
            options.ClientId.Trim(),
            options.Account.Trim());
        Profiles.Save(profile);
        return profile;
    }
}
