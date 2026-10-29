using M365Aegis.Core.Auth;

namespace M365Aegis.Core.Tests.Auth;

/// <summary>
/// A scripted token source: each path answers with the delegate the test set, or fails the way the real broker
/// would on a registered-only machine with nothing signed in. Every call is recorded, so a test asserts the
/// order the flow took.
/// </summary>
internal sealed class FakeTokenBroker : ITokenBroker
{
    /// <summary>The fake's token: looks like a JWT on purpose, so the fixture check's own regex catches a leak.</summary>
    internal const string FakeAccessToken = "eyJfake-token-of-the-tests";

    internal static readonly AccountInfo Alice = new("alice@contoso.com", "00000000-0000-0000-0000-000000000001.00000000-0000-0000-0000-000000000002", "00000000-0000-0000-0000-000000000001", "00000000-0000-0000-0000-000000000002");
    internal static readonly AccountInfo Bob = new("bob@contoso.com", "00000000-0000-0000-0000-000000000003.00000000-0000-0000-0000-000000000002", "00000000-0000-0000-0000-000000000003", "00000000-0000-0000-0000-000000000002");

    internal List<string> Calls { get; } = [];

    internal List<AccountInfo> Accounts { get; } = [];

    internal List<AccountInfo> Removed { get; } = [];

    /// <summary>The operating system account path; null fails as a machine without a primary refresh token.</summary>
    internal Func<string, TokenResult>? OperatingSystemAccount { get; set; }

    /// <summary>The silent path for a known account; null fails as an expired session.</summary>
    internal Func<string, AccountInfo, TokenResult>? Silent { get; set; }

    /// <summary>The account picker; null fails with the exception in <see cref="InteractiveFailure"/>.</summary>
    internal Func<string, string, TokenResult>? Interactive { get; set; }

    internal BrokerException InteractiveFailure { get; set; } =
        new(BrokerErrorKind.Other, "wam_provider_error_0", "the broker did not answer");

    /// <summary>The device code path; null fails as an expired code.</summary>
    internal Func<string, TokenResult>? DeviceCode { get; set; }

    /// <summary>A token for a scope and an account, with the scopes a consented registration returns.</summary>
    internal static TokenResult Token(string scope, AccountInfo account, TokenOrigin origin)
    {
        // The broker lists the scopes without the resource prefix and with the sign-in's own ones; the identity
        // provider lists them with the prefix. The result types show the permissions either way.
        string host = new Uri(scope).Host;
        IReadOnlyList<string> scopes = host == "graph.microsoft.com"
            ? ["email", ".default", "User.Read", "Group.Read.All", "openid", "profile", "User.Read"]
            : [$"https://{host}/Exchange.Manage", $"https://{host}/Exchange.ManageV2"];
        return new TokenResult(
            FakeAccessToken,
            new DateTimeOffset(2026, 10, 29, 15, 0, 0, TimeSpan.FromHours(1)),
            scopes,
            account.TenantId,
            account,
            origin,
            "Alice Example");
    }

    public Task<TokenResult> AcquireSilentWithOperatingSystemAccountAsync(string scope, CancellationToken cancellationToken)
    {
        Calls.Add("os-account");
        return OperatingSystemAccount is null
            ? throw new BrokerException(BrokerErrorKind.NoPrimaryRefreshToken, "failed_to_acquire_token_silently_from_broker", "no primary refresh token")
            : Task.FromResult(OperatingSystemAccount(scope));
    }

    public Task<IReadOnlyList<AccountInfo>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        Calls.Add("accounts");
        return Task.FromResult<IReadOnlyList<AccountInfo>>(Accounts.ToList());
    }

    public Task<TokenResult> AcquireSilentAsync(string scope, AccountInfo account, CancellationToken cancellationToken)
    {
        Calls.Add($"silent:{account.Username}");
        return Silent is null
            ? throw new BrokerException(BrokerErrorKind.UiRequired, "interaction_required", "the session expired")
            : Task.FromResult(Silent(scope, account));
    }

    public Task<TokenResult> AcquireInteractiveAsync(string scope, string loginHint, CancellationToken cancellationToken)
    {
        Calls.Add($"interactive:{loginHint}");
        return Interactive is null ? throw InteractiveFailure : Task.FromResult(Interactive(scope, loginHint));
    }

    public async Task<TokenResult> AcquireWithDeviceCodeAsync(string scope, Func<DeviceCodePrompt, Task> prompt, CancellationToken cancellationToken)
    {
        Calls.Add("device-code");
        await prompt(new DeviceCodePrompt(
            new Uri("https://login.microsoftonline.com/common/oauth2/deviceauth"),
            "ABCD1234",
            "To sign in, open the page and enter the code ABCD1234.",
            DateTimeOffset.UtcNow.AddMinutes(15)));
        return DeviceCode is null
            ? throw new BrokerException(BrokerErrorKind.Other, "code_expired", "the device code expired")
            : DeviceCode(scope);
    }

    public Task RemoveAsync(AccountInfo account, CancellationToken cancellationToken)
    {
        Calls.Add($"remove:{account.Username}");
        Removed.Add(account);
        Accounts.Remove(account);
        return Task.CompletedTask;
    }
}
