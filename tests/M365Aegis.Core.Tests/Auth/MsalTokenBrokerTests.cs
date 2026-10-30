using System.Security.Claims;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Tests.Config;
using Microsoft.Identity.Client;
using TenantProfile = M365Aegis.Core.Config.TenantProfile;

namespace M365Aegis.Core.Tests.Auth;

/// <summary>
/// What the MSAL broker does around the library, without calling it (task 070): the exception mapping, the result,
/// the window thread and the factories. The members that call MSAL itself are excluded from the coverage.
/// </summary>
public sealed class MsalTokenBrokerTests
{
    private static readonly TenantProfile Profile = new("default", "contoso.onmicrosoft.com", "00000000-0000-0000-0000-000000000010", "alice@contoso.com");

    public static TheoryData<Exception, BrokerErrorKind> MsalFailures => new()
    {
        { new MsalUiRequiredException(MsalError.FailedToAcquireTokenSilentlyFromBroker, "no primary refresh token"), BrokerErrorKind.NoPrimaryRefreshToken },
        { new MsalUiRequiredException(MsalError.InvalidGrantError, "the session expired"), BrokerErrorKind.UiRequired },
        { new MsalClientException(MsalTokenBroker.WindowHandleRequiredCode, "no window"), BrokerErrorKind.WindowHandleRequired },
        { new MsalClientException(MsalError.AuthenticationCanceledError, "the user closed the picker"), BrokerErrorKind.Cancelled },
        { new MsalClientException("some_client_error", "something else"), BrokerErrorKind.Other },
        { new MsalServiceException("invalid_client", "the service refused"), BrokerErrorKind.Other },
    };

    [Theory]
    [MemberData(nameof(MsalFailures))]
    public async Task EveryMsalExceptionBecomesABrokerExceptionOfItsKind(Exception thrown, BrokerErrorKind kind)
    {
        BrokerException exception = await Assert.ThrowsAsync<BrokerException>(() => MsalTokenBroker.Guard<int>(() => throw thrown));

        Assert.Equal(kind, exception.Kind);
        Assert.Equal(((MsalException)thrown).ErrorCode, exception.Code);
        Assert.Same(thrown, exception.InnerException);
    }

    [Fact]
    public async Task ACallThatSucceedsIsReturnedAsItIs() =>
        Assert.Equal(7, await MsalTokenBroker.Guard(() => Task.FromResult(7)));

    [Theory]
    [InlineData(TokenSource.Broker, TokenOrigin.Broker)]
    [InlineData(TokenSource.Cache, TokenOrigin.Cache)]
    [InlineData(TokenSource.IdentityProvider, TokenOrigin.IdentityProvider)]
    public void TheResultCarriesTheOriginTheTenantAndTheAccount(TokenSource source, TokenOrigin origin)
    {
        TokenResult result = MsalTokenBroker.ToResult(Result("00000000-0000-0000-0000-000000000002", new AuthenticationResultMetadata(source), claims: null));

        Assert.Equal(origin, result.Origin);
        Assert.Equal("00000000-0000-0000-0000-000000000002", result.TenantId);
        Assert.Equal("alice@contoso.com", result.Account.Username);
        Assert.Equal("00000000-0000-0000-0000-000000000001", result.Account.ObjectId);
        Assert.Equal(["User.Read"], result.Scopes);
        Assert.Null(result.DisplayName);
    }

    [Fact]
    public void WithoutATenantOrMetadataTheClaimsAnswer()
    {
        var claims = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tid", "00000000-0000-0000-0000-000000000003"), new Claim("name", "Alice Example")]));

        TokenResult result = MsalTokenBroker.ToResult(Result(null, metadata: null, claims));

        Assert.Equal(TokenOrigin.IdentityProvider, result.Origin);
        Assert.Equal("00000000-0000-0000-0000-000000000003", result.TenantId);
        Assert.Equal("Alice Example", result.DisplayName);
    }

    [Fact]
    public void ClaimsWithoutTheTenantLeaveItEmpty()
    {
        TokenResult result = MsalTokenBroker.ToResult(Result(null, metadata: null, new ClaimsPrincipal(new ClaimsIdentity())));

        Assert.Null(result.TenantId);
        Assert.Null(result.DisplayName);
        Assert.Null(MsalTokenBroker.ToResult(Result(null, metadata: null, claims: null)).TenantId);
    }

    [Fact]
    public async Task AConsoleBrokerStartsTheCallHere()
    {
        var broker = (MsalTokenBroker)MsalTokenBroker.Create(Profile, ProfileStoreTests.TempHome());

        Assert.Equal(3, await broker.OnWindowThreadAsync(() => Task.FromResult(3)));
    }

    [Fact]
    public async Task AWindowBrokerStartsTheCallOnTheWindowsThread()
    {
        int started = 0;
        var broker = (MsalTokenBroker)MsalTokenBroker.Create(Profile, ProfileStoreTests.TempHome(), () => IntPtr.Zero, async start =>
        {
            started++;
            await start();
        });

        Assert.Equal(5, await broker.OnWindowThreadAsync(() => Task.FromResult(5)));
        Assert.Equal(1, started);
    }

    [Fact]
    public void TheFactoriesCheckTheirArguments()
    {
        string home = ProfileStoreTests.TempHome();

        Assert.Throws<ArgumentNullException>(() => MsalTokenBroker.Create(null!, home, () => IntPtr.Zero, log: null));
        Assert.Throws<ArgumentException>(() => MsalTokenBroker.Create(Profile, " ", () => IntPtr.Zero, log: null));
        Assert.Throws<ArgumentNullException>(() => MsalTokenBroker.Create(Profile, home, null!, log: null));
        Assert.Throws<ArgumentNullException>(() => MsalTokenBroker.Create(null!, home, () => IntPtr.Zero, _ => Task.CompletedTask));
        Assert.Throws<ArgumentException>(() => MsalTokenBroker.Create(Profile, "", () => IntPtr.Zero, _ => Task.CompletedTask));
        Assert.Throws<ArgumentNullException>(() => MsalTokenBroker.Create(Profile, home, null!, _ => Task.CompletedTask));
        Assert.Throws<ArgumentNullException>(() => MsalTokenBroker.Create(Profile, home, () => IntPtr.Zero, (Func<Func<Task>, Task>)null!));
        // Building a broker loads nothing: the client is built on the first call, which no test makes.
        Assert.IsType<MsalTokenBroker>(MsalTokenBroker.Create(Profile, home, () => IntPtr.Zero));
        Assert.False(Directory.Exists(home));
    }

    private static AuthenticationResult Result(string? tenantId, AuthenticationResultMetadata? metadata, ClaimsPrincipal? claims) =>
        new(
            "eyJfake-token-of-the-tests",
            isExtendedLifeTimeToken: false,
            uniqueId: "00000000-0000-0000-0000-000000000001",
            expiresOn: new DateTimeOffset(2026, 10, 30, 15, 0, 0, TimeSpan.Zero),
            extendedExpiresOn: new DateTimeOffset(2026, 10, 30, 15, 0, 0, TimeSpan.Zero),
            tenantId: tenantId!,
            account: new FakeAccount(),
            idToken: null!,
            scopes: ["User.Read"],
            correlationId: Guid.Empty,
            tokenType: "Bearer",
            authenticationResultMetadata: metadata!,
            claimsPrincipal: claims!);

    private sealed class FakeAccount : IAccount
    {
        public string Username => "alice@contoso.com";

        public string Environment => "login.microsoftonline.com";

        public AccountId HomeAccountId { get; } = new(
            "00000000-0000-0000-0000-000000000001.00000000-0000-0000-0000-000000000002",
            "00000000-0000-0000-0000-000000000001",
            "00000000-0000-0000-0000-000000000002");
    }
}
