using M365Aegis.Core.Auth;
using M365Aegis.Core.Config;
using M365Aegis.Core.Output;
using M365Aegis.Core.Tests.Config;

namespace M365Aegis.Core.Tests.Auth;

/// <summary>login, whoami, logout and the token hand-out over the scripted broker and a temporary home.</summary>
public sealed class AuthServiceTests
{
    private static readonly LoginOptions WithFlags = new(null, "contoso.onmicrosoft.com", "00000000-0000-0000-0000-000000000010", "alice@contoso.com", DeviceCode: false);

    [Fact]
    public async Task LoginWithTheFlagsWritesTheProfileAndReportsBothTokens()
    {
        var broker = new FakeTokenBroker
        {
            Interactive = (scope, hint) => FakeTokenBroker.Token(scope, FakeTokenBroker.Alice, TokenOrigin.Broker),
            Silent = (scope, account) => FakeTokenBroker.Token(scope, account, TokenOrigin.Broker),
        };
        (AuthService service, string home) = Service(broker);

        SignInInfo info = await service.LoginAsync(WithFlags, _ => Task.CompletedTask, CancellationToken.None);

        Assert.Equal("default", info.Profile);
        Assert.Equal("alice@contoso.com", info.Account);
        Assert.Equal("Alice Example", info.DisplayName);
        Assert.Equal("00000000-0000-0000-0000-000000000002", info.TenantId);
        Assert.Equal([Resource.Graph, Resource.Exchange], info.Tokens.Select(token => token.Resource));
        Assert.Equal(["User.Read", "Group.Read.All"], info.Tokens[0].Scopes);
        Assert.Equal(["Exchange.Manage", "Exchange.ManageV2"], info.Tokens[1].Scopes);
        Assert.True(File.Exists(Path.Combine(home, "config.json")));
        Assert.Equal("alice@contoso.com", service.Profiles.Resolve(null).Account);
        // The picker signed Graph in; Exchange Online is a silent call for that account, not a second picker.
        Assert.Equal(["os-account", "accounts", "interactive:alice@contoso.com", "silent:alice@contoso.com"], broker.Calls);
    }

    [Fact]
    public async Task PartialFlagsAreAUsageFailureAndWriteNothing()
    {
        (AuthService service, string home) = Service(new FakeTokenBroker());

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() =>
            service.LoginAsync(WithFlags with { Account = null }, _ => Task.CompletedTask, CancellationToken.None));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
        Assert.False(File.Exists(Path.Combine(home, "config.json")));
    }

    [Fact]
    public async Task LoginWithDeviceCodeGoesStraightToTheCode()
    {
        var broker = new FakeTokenBroker
        {
            DeviceCode = scope => FakeTokenBroker.Token(scope, FakeTokenBroker.Alice, TokenOrigin.IdentityProvider),
            Silent = (scope, account) => FakeTokenBroker.Token(scope, account, TokenOrigin.Cache),
        };
        (AuthService service, _) = Service(broker);
        var prompts = new List<string>();

        SignInInfo info = await service.LoginAsync(WithFlags with { DeviceCode = true }, prompt => { prompts.Add(prompt.Message); return Task.CompletedTask; }, CancellationToken.None);

        Assert.Single(prompts);
        Assert.Equal(TokenOrigin.IdentityProvider, info.Tokens[0].Origin);
        Assert.Equal(TokenOrigin.Cache, info.Tokens[1].Origin);
    }

    [Fact]
    public async Task WhoAmIWithoutAProfileIsAnAuthFailure()
    {
        (AuthService service, _) = Service(new FakeTokenBroker());

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => service.WhoAmIAsync(null, CancellationToken.None));

        Assert.Equal(ExitCode.Auth, exception.Failure.ExitCode);
    }

    [Fact]
    public async Task WhoAmIIsSilentAndAMissingExchangeTokenIsARowNotAFailure()
    {
        var broker = new FakeTokenBroker
        {
            Silent = (scope, account) => scope == Scopes.Graph
                ? FakeTokenBroker.Token(scope, account, TokenOrigin.Broker)
                : throw new BrokerException(BrokerErrorKind.UiRequired, "consent_required", "no consent for Exchange Online"),
        };
        broker.Accounts.Add(FakeTokenBroker.Alice);
        (AuthService service, _) = Service(broker, saveProfile: true);

        SignInInfo info = await service.WhoAmIAsync(null, CancellationToken.None);

        Assert.Null(info.Tokens[0].Error);
        Assert.Null(info.Tokens[1].ExpiresOn);
        Assert.Contains("consent_required", info.Tokens[1].Error, StringComparison.Ordinal);
        Assert.DoesNotContain(broker.Calls, call => call.StartsWith("interactive", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoTokenReachesASerializedResult()
    {
        var broker = new FakeTokenBroker
        {
            Interactive = (scope, hint) => FakeTokenBroker.Token(scope, FakeTokenBroker.Alice, TokenOrigin.Broker),
            Silent = (scope, account) => FakeTokenBroker.Token(scope, account, TokenOrigin.Broker),
        };
        (AuthService service, _) = Service(broker);
        SignInInfo info = await service.LoginAsync(WithFlags, _ => Task.CompletedTask, CancellationToken.None);

        string json = EnvelopeJson.Serialize(CommandResult.Success(info).ToEnvelope(new EnvelopeMeta([], 1, Backend.Local)));

        Assert.DoesNotContain("eyJ", json, StringComparison.Ordinal);
        Assert.DoesNotContain("accessToken", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LogoutRemovesTheProfileAccountsOnlyAndKeepsTheProfile()
    {
        var broker = new FakeTokenBroker();
        broker.Accounts.AddRange([FakeTokenBroker.Bob, FakeTokenBroker.Alice, FakeTokenBroker.Alice with { HomeAccountId = "other" }]);
        (AuthService service, _) = Service(broker, saveProfile: true);

        SignOutInfo info = await service.LogoutAsync(null, CancellationToken.None);

        Assert.Equal(2, info.RemovedAccounts);
        Assert.All(broker.Removed, account => Assert.Equal("alice@contoso.com", account.Username));
        Assert.Equal([FakeTokenBroker.Bob], broker.Accounts);
        Assert.Equal("alice@contoso.com", service.Profiles.Resolve(null).Account);
    }

    [Fact]
    public async Task LogoutWithNothingSignedInIsAZero()
    {
        (AuthService service, _) = Service(new FakeTokenBroker(), saveProfile: true);

        SignOutInfo info = await service.LogoutAsync(null, CancellationToken.None);

        Assert.Equal(0, info.RemovedAccounts);
    }

    [Fact]
    public async Task TheAccessTokenHandOutIsSilentOnly()
    {
        var broker = new FakeTokenBroker { Interactive = (scope, hint) => FakeTokenBroker.Token(scope, FakeTokenBroker.Alice, TokenOrigin.Broker) };
        (AuthService service, _) = Service(broker, saveProfile: true);

        await Assert.ThrowsAsync<AegisException>(() => service.GetAccessTokenAsync(Resource.Exchange, null, CancellationToken.None));
        broker.OperatingSystemAccount = scope => FakeTokenBroker.Token(scope, FakeTokenBroker.Alice, TokenOrigin.Broker);
        string token = await service.GetAccessTokenAsync(Resource.Exchange, null, CancellationToken.None);

        Assert.Equal(FakeTokenBroker.FakeAccessToken, token);
        Assert.DoesNotContain(broker.Calls, call => call.StartsWith("interactive", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ABrokerFailureOfTheSignOutIsAnAuthFailure()
    {
        var broker = new FakeTokenBroker { RemoveFailure = new BrokerException(BrokerErrorKind.Other, "wam_provider_error_0", "the broker did not answer") };
        broker.Accounts.Add(FakeTokenBroker.Alice);
        (AuthService service, _) = Service(broker, saveProfile: true);

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => service.LogoutAsync(null, CancellationToken.None));

        Assert.Equal(FailureKind.Auth, exception.Failure.Kind);
        Assert.Contains("wam_provider_error_0", exception.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemovingAProfileSignsItsAccountOutFirst()
    {
        var broker = new FakeTokenBroker();
        broker.Accounts.AddRange([FakeTokenBroker.Alice, FakeTokenBroker.Bob]);
        (AuthService service, _) = Service(broker, saveProfile: true);
        service.Profiles.Save(new TenantProfile("lab", "fabrikam.onmicrosoft.com", "00000000-0000-0000-0000-000000000011", "bob@contoso.com"));

        ProfileRemoval removal = await service.RemoveProfileAsync("default", CancellationToken.None);

        Assert.True(removal.SignedOut);
        Assert.Equal(1, removal.RemovedAccounts);
        Assert.Equal(["accounts", "remove:alice@contoso.com"], broker.Calls);
        Assert.Equal("lab", removal.Default);
        Assert.Equal(["lab"], service.Profiles.List().Select(profile => profile.Name));
    }

    [Fact]
    public async Task ASharedAccountIsNotSignedOutWhenAnotherProfileStillUsesIt()
    {
        var broker = new FakeTokenBroker();
        broker.Accounts.Add(FakeTokenBroker.Alice);
        (AuthService service, _) = Service(broker, saveProfile: true);
        service.Profiles.Save(new TenantProfile("second", WithFlags.Tenant!, WithFlags.ClientId!.ToUpperInvariant(), "ALICE@contoso.com"));

        ProfileRemoval removal = await service.RemoveProfileAsync("second", CancellationToken.None);

        Assert.False(removal.SignedOut);
        Assert.Empty(broker.Calls);
        Assert.Equal([FakeTokenBroker.Alice], broker.Accounts);
        Assert.Equal("default", removal.Default);
    }

    [Fact]
    public async Task AFailedSignOutKeepsTheProfile()
    {
        var broker = new FakeTokenBroker { RemoveFailure = new BrokerException(BrokerErrorKind.Other, "wam_provider_error_0", "the broker did not answer") };
        broker.Accounts.Add(FakeTokenBroker.Alice);
        (AuthService service, _) = Service(broker, saveProfile: true);

        await Assert.ThrowsAsync<AegisException>(() => service.RemoveProfileAsync("default", CancellationToken.None));

        Assert.Equal("default", Assert.Single(service.Profiles.List()).Name);
    }

    [Fact]
    public async Task RemovingAnUnknownProfileIsNotFoundAndTouchesNothing()
    {
        var broker = new FakeTokenBroker();
        (AuthService service, _) = Service(broker, saveProfile: true);

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => service.RemoveProfileAsync("nope", CancellationToken.None));

        Assert.Equal(FailureKind.NotFound, exception.Failure.Kind);
        Assert.Empty(broker.Calls);
    }

    [Theory]
    [InlineData(" ", "00000000-0000-0000-0000-000000000010", "alice@contoso.com")]
    [InlineData("contoso.onmicrosoft.com", null, "alice@contoso.com")]
    [InlineData("contoso.onmicrosoft.com", "00000000-0000-0000-0000-000000000010", "")]
    public async Task EveryMissingFlagOfTheThreeIsAUsageFailure(string? tenant, string? clientId, string? account)
    {
        (AuthService service, string home) = Service(new FakeTokenBroker());

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() =>
            service.LoginAsync(WithFlags with { Tenant = tenant, ClientId = clientId, Account = account }, _ => Task.CompletedTask, CancellationToken.None));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
        Assert.False(File.Exists(Path.Combine(home, "config.json")));
    }

    [Fact]
    public async Task LoginWithTheFlagsAndANameWritesThatProfileAndTheAccountsTenantStandsInForTheTokens()
    {
        // A token without a tenant (a device-code answer of some clouds) leaves the account's tenant to the result.
        var broker = new FakeTokenBroker
        {
            Interactive = (scope, hint) => FakeTokenBroker.Token(scope, FakeTokenBroker.Alice, TokenOrigin.Broker) with { TenantId = null },
            Silent = (scope, account) => FakeTokenBroker.Token(scope, account, TokenOrigin.Broker),
        };
        (AuthService service, _) = Service(broker);

        SignInInfo info = await service.LoginAsync(WithFlags with { ProfileName = "lab" }, _ => Task.CompletedTask, CancellationToken.None);

        Assert.Equal("lab", info.Profile);
        Assert.Equal(FakeTokenBroker.Alice.TenantId, info.TenantId);
        Assert.Equal("lab", service.Profiles.Resolve("lab").Name);
    }

    [Fact]
    public async Task TheSilentCallsPromptNothing() =>
        await AuthService.NoPrompt(new DeviceCodePrompt(new Uri("https://login.microsoftonline.com/common/oauth2/deviceauth"), "ABCD", "go there", DateTimeOffset.UnixEpoch));

    [Fact]
    public void EveryResourceHasItsScopeAndAnUnknownOneHasNone()
    {
        Assert.Equal(Scopes.Graph, Scopes.For(Resource.Graph));
        Assert.Equal(Scopes.Exchange, Scopes.For(Resource.Exchange));
        Assert.Throws<ArgumentOutOfRangeException>(() => Scopes.For((Resource)99));
    }

    private static (AuthService Service, string Home) Service(FakeTokenBroker broker, bool saveProfile = false)
    {
        string home = ProfileStoreTests.TempHome();
        var service = new AuthService(home, _ => broker);
        if (saveProfile)
        {
            service.Profiles.Save(new TenantProfile("default", WithFlags.Tenant!, WithFlags.ClientId!, WithFlags.Account!));
        }

        return (service, home);
    }
}
