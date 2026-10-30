using M365Aegis.Core.Auth;

namespace M365Aegis.Tests;

/// <summary>
/// The token source of the token-hygiene tests (task 072, task 080 through the window): it signs everything in as
/// Alice with tokens that carry the run's marker, the Graph token in a JWT's shape as the service issues it and the
/// Exchange Online token opaque, so a sink passes only by never taking it. A device-code sign-in hands its made-up
/// prompt to the caller first, as the sign-in service's answer does. Linked into both test projects.
/// </summary>
/// <param name="marker">The run's marker, in every token.</param>
/// <param name="tenantId">The tenant the tokens name, Alice's by default (the Admin API's recorded answers want theirs).</param>
internal sealed class MarkedTokenBroker(string marker, string? tenantId = null) : ITokenBroker
{
    /// <summary>The account every sign-in answers with.</summary>
    internal const string Account = "alice@contoso.com";

    private static readonly AccountInfo Alice = new(Account, "00000000-0000-0000-0000-000000000001.00000000-0000-0000-0000-000000000002", "00000000-0000-0000-0000-000000000001", "00000000-0000-0000-0000-000000000002");

    /// <summary>A fresh marker for one run: <c>aegismarker</c> and a new GUID.</summary>
    internal static string NewMarker() => $"aegismarker{Guid.NewGuid():N}";

    /// <summary>The token of a scope: JWT-shaped for Graph, opaque for Exchange Online; the marker in both.</summary>
    internal string Token(string scope) =>
        scope.Contains("graph.microsoft.com", StringComparison.Ordinal) ? $"eyJ{marker}Zw.eyJ{marker}cGF5.c2ln" : $"opaque-{marker}-exchange";

    public Task<TokenResult> AcquireSilentWithOperatingSystemAccountAsync(string scope, CancellationToken cancellationToken) =>
        Task.FromResult(Result(scope, TokenOrigin.Broker));

    public Task<IReadOnlyList<AccountInfo>> GetAccountsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AccountInfo>>([Alice]);

    public Task<TokenResult> AcquireSilentAsync(string scope, AccountInfo account, CancellationToken cancellationToken) =>
        Task.FromResult(Result(scope, TokenOrigin.Cache));

    public Task<TokenResult> AcquireInteractiveAsync(string scope, string loginHint, CancellationToken cancellationToken) =>
        Task.FromResult(Result(scope, TokenOrigin.IdentityProvider));

    public async Task<TokenResult> AcquireWithDeviceCodeAsync(string scope, Func<DeviceCodePrompt, Task> prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        await prompt(new DeviceCodePrompt(
            new Uri("https://microsoft.com/devicelogin"),
            "ABCD1234",
            "To sign in, use a web browser to open the page https://microsoft.com/devicelogin and enter the code ABCD1234 to authenticate.",
            DateTimeOffset.UtcNow.AddMinutes(15))).ConfigureAwait(false);
        return Result(scope, TokenOrigin.IdentityProvider);
    }

    public Task RemoveAsync(AccountInfo account, CancellationToken cancellationToken) => Task.CompletedTask;

    private TokenResult Result(string scope, TokenOrigin origin) => new(
        Token(scope),
        DateTimeOffset.UtcNow.AddHours(1),
        scope.Contains("graph.microsoft.com", StringComparison.Ordinal) ? ["User.Read", "GroupMember.ReadWrite.All"] : ["https://outlook.office365.com/Exchange.Manage"],
        tenantId ?? Alice.TenantId,
        Alice,
        origin,
        "Alice Example");
}
