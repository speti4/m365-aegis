using M365Aegis.Core.Auth;
using M365Aegis.Core.Config;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Tests.Auth;

/// <summary>The fallback order of D-003 over the scripted broker, step by step.</summary>
public sealed class SignInFlowTests
{
    private static readonly TenantProfile Profile = new("default", "contoso.onmicrosoft.com", "00000000-0000-0000-0000-000000000010", "alice@contoso.com");

    [Fact]
    public async Task TheOperatingSystemAccountAnswersFirst()
    {
        var broker = new FakeTokenBroker { OperatingSystemAccount = scope => FakeTokenBroker.Token(scope, FakeTokenBroker.Alice, TokenOrigin.Broker) };

        TokenResult token = await Run(broker, SignInMode.Interactive);

        Assert.Equal(TokenOrigin.Broker, token.Origin);
        Assert.Equal(["os-account"], broker.Calls);
    }

    [Fact]
    public async Task ARegisteredMachinePicksTheProfileAccountByUserPrincipalName()
    {
        // The broker lists the everyday account first; the profile names the other one (task 013).
        var broker = new FakeTokenBroker { Silent = (scope, account) => FakeTokenBroker.Token(scope, account, TokenOrigin.Broker) };
        broker.Accounts.AddRange([FakeTokenBroker.Bob, FakeTokenBroker.Alice]);

        TokenResult token = await Run(broker, SignInMode.SilentOnly);

        Assert.Equal("alice@contoso.com", token.Account.Username);
        Assert.Equal(["os-account", "accounts", "silent:alice@contoso.com"], broker.Calls);
    }

    [Fact]
    public async Task TheMatchIgnoresTheCaseOfTheUserPrincipalName()
    {
        var broker = new FakeTokenBroker { Silent = (scope, account) => FakeTokenBroker.Token(scope, account, TokenOrigin.Cache) };
        broker.Accounts.Add(FakeTokenBroker.Alice with { Username = "Alice@Contoso.com" });

        TokenResult token = await Run(broker, SignInMode.SilentOnly);

        Assert.Equal(TokenOrigin.Cache, token.Origin);
    }

    [Fact]
    public async Task SilentOnlyNeverPromptsAndSaysToLogIn()
    {
        var broker = new FakeTokenBroker();
        broker.Accounts.Add(FakeTokenBroker.Bob);

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => Run(broker, SignInMode.SilentOnly));

        Assert.Equal(FailureKind.Auth, exception.Failure.Kind);
        Assert.Contains("alice@contoso.com", exception.Failure.Message, StringComparison.Ordinal);
        Assert.Contains("login", exception.Failure.Hint, StringComparison.Ordinal);
        Assert.DoesNotContain(broker.Calls, call => call.StartsWith("interactive", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnExpiredSessionFallsThroughToThePicker()
    {
        var broker = new FakeTokenBroker { Interactive = (scope, hint) => FakeTokenBroker.Token(scope, FakeTokenBroker.Alice, TokenOrigin.Broker) };
        broker.Accounts.Add(FakeTokenBroker.Alice);

        TokenResult token = await Run(broker, SignInMode.Interactive);

        Assert.Equal(TokenOrigin.Broker, token.Origin);
        Assert.Equal(["os-account", "accounts", "silent:alice@contoso.com", "interactive:alice@contoso.com"], broker.Calls);
    }

    [Fact]
    public async Task NoWindowFallsBackToDeviceCode()
    {
        var broker = new FakeTokenBroker
        {
            InteractiveFailure = new BrokerException(BrokerErrorKind.WindowHandleRequired, "window_handle_required", "A window handle must be configured."),
            DeviceCode = scope => FakeTokenBroker.Token(scope, FakeTokenBroker.Alice, TokenOrigin.IdentityProvider),
        };
        var prompts = new List<DeviceCodePrompt>();

        TokenResult token = await SignInFlow.RunAsync(broker, Profile, Resource.Graph, SignInMode.Interactive, prompt => { prompts.Add(prompt); return Task.CompletedTask; }, CancellationToken.None);

        Assert.Equal(TokenOrigin.IdentityProvider, token.Origin);
        Assert.Equal("ABCD1234", Assert.Single(prompts).UserCode);
        Assert.Equal(["os-account", "accounts", "interactive:alice@contoso.com", "device-code"], broker.Calls);
    }

    [Fact]
    public async Task AClosedPickerIsAnAuthFailure()
    {
        var broker = new FakeTokenBroker
        {
            InteractiveFailure = new BrokerException(BrokerErrorKind.Cancelled, "authentication_canceled", "User canceled authentication."),
        };

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => Run(broker, SignInMode.Interactive));

        Assert.Equal(ExitCode.Auth, exception.Failure.ExitCode);
        Assert.Contains("cancelled", exception.Failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("device-code", broker.Calls);
    }

    [Fact]
    public async Task AnotherPickerFailureSuggestsDeviceCode()
    {
        var broker = new FakeTokenBroker();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => Run(broker, SignInMode.Interactive));

        Assert.Contains("wam_provider_error_0", exception.Failure.Message, StringComparison.Ordinal);
        Assert.Contains("--device-code", exception.Failure.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeviceCodeModeSkipsTheBroker()
    {
        var broker = new FakeTokenBroker { DeviceCode = scope => FakeTokenBroker.Token(scope, FakeTokenBroker.Alice, TokenOrigin.IdentityProvider) };

        await Run(broker, SignInMode.DeviceCode);

        Assert.Equal(["device-code"], broker.Calls);
    }

    [Fact]
    public async Task AnExpiredDeviceCodeIsAnAuthFailure()
    {
        var broker = new FakeTokenBroker();

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => Run(broker, SignInMode.DeviceCode));

        Assert.Equal(FailureKind.Auth, exception.Failure.Kind);
        Assert.Contains("code_expired", exception.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheExchangeResourceAsksTheExchangeScope()
    {
        string? asked = null;
        var broker = new FakeTokenBroker
        {
            OperatingSystemAccount = scope =>
            {
                asked = scope;
                return FakeTokenBroker.Token(scope, FakeTokenBroker.Alice, TokenOrigin.Broker);
            },
        };

        await SignInFlow.RunAsync(broker, Profile, Resource.Exchange, SignInMode.SilentOnly, _ => Task.CompletedTask, CancellationToken.None);

        Assert.Equal(Scopes.Exchange, asked);
    }

    [Fact]
    public void TheTokenNeverPrintsItself()
    {
        TokenResult token = FakeTokenBroker.Token(Scopes.Graph, FakeTokenBroker.Alice, TokenOrigin.Broker);

        Assert.DoesNotContain(FakeTokenBroker.FakeAccessToken, token.ToString(), StringComparison.Ordinal);
    }

    private static Task<TokenResult> Run(FakeTokenBroker broker, SignInMode mode) =>
        SignInFlow.RunAsync(broker, Profile, Resource.Graph, mode, _ => Task.CompletedTask, CancellationToken.None);
}
