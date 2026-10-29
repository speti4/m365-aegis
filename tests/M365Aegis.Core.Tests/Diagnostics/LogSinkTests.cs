using M365Aegis.Core.Auth;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Tests.Config;
using Microsoft.Identity.Client;
using TenantProfile = M365Aegis.Core.Config.TenantProfile;

namespace M365Aegis.Core.Tests.Diagnostics;

/// <summary>
/// The log under the home folder (tasks 021 and 025): what reaches the file, what never does, and that a run with
/// nothing to log leaves nothing behind. The scripted lines are shaped like MSAL's and the broker's own.
/// </summary>
public sealed class LogSinkTests
{
    private static readonly TenantProfile Profile = new("work", "fabrikam.com", "00000000-0000-0000-0000-000000000010", "alice@fabrikam.com");

    /// <summary>Lines of the shapes a sign-in log can carry: a token, an authorization code, a user principal name, tenant ids.</summary>
    private static readonly string[] ScriptedLines =
    [
        "False MSAL 4.91.1.0 MSAL.NetCore .NET 10.0.4 [2026-10-29 10:00:00Z - 00000000-0000-0000-0000-000000000001] === Token Acquisition (SilentRequest) started: Scopes: https://graph.microsoft.com/.default",
        "Authority: https://login.microsoftonline.com/00000000-0000-0000-0000-000000000002/ for client 00000000-0000-0000-0000-000000000010",
        "Authorization: Bearer eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.eyJhdWQiOiJodHRwczovL2dyYXBo.c2lnbmF0dXJl",
        "Redirect received: http://localhost:8400/?code=0.AXoA1b2c3d4e5f6g7h8i9j&state=abc123&session_state=xyz",
        """Token response: {"token_type":"Bearer","access_token":"opaque-access-token-value","refresh_token":"1.AXoA-refresh-value","id_token":"opaque-id-token"}""",
        "[WAM] Account alice@fabrikam.com of tenant contoso.onmicrosoft.com found for the login hint",
        "[WAM] Broker error: failed_to_acquire_token_silently_from_broker, status 3399614476, tag 0x2039c1cb for fabrikam.com",
        "Device code flow: user_code=ABCD1234 device_code=GAQABAAEAAAD--device-code-value",
    ];

    private static readonly string[] NeverInTheFile =
    [
        "eyJ", "0.AXoA1b2c3d4e5f6g7h8i9j", "opaque-access-token-value", "1.AXoA-refresh-value", "opaque-id-token",
        "alice@fabrikam.com", "contoso.onmicrosoft.com", "fabrikam.com", "00000000-0000-0000-0000-0000000000",
        "ABCD1234", "GAQABAAEAAAD",
    ];

    [Fact]
    public void NoTokenAuthorizationCodeOrUserPrincipalNameReachesTheFile()
    {
        var sink = new LogSink(Folder());
        sink.Redact(Profile.Tenant);
        sink.Redact(Profile.ClientId);
        sink.Redact(Profile.Account);

        foreach (string line in ScriptedLines)
        {
            Assert.True(sink.Write("msal", line));
        }

        string text = File.ReadAllText(sink.CurrentFile);
        Assert.All(NeverInTheFile, secret => Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase));
        // What makes the log worth having stays: the step, the error code, the scope.
        Assert.Contains("Token Acquisition (SilentRequest) started", text, StringComparison.Ordinal);
        Assert.Contains("failed_to_acquire_token_silently_from_broker", text, StringComparison.Ordinal);
        Assert.Contains("https://graph.microsoft.com/.default", text, StringComparison.Ordinal);
        Assert.Contains("Bearer <redacted>", text, StringComparison.Ordinal);
        Assert.Contains("code=<redacted>", text, StringComparison.Ordinal);
        Assert.Contains("\"access_token\":\"<redacted>\"", text, StringComparison.Ordinal);
        Assert.Equal(ScriptedLines.Length, File.ReadAllLines(sink.CurrentFile).Length);
    }

    /// <summary>
    /// Lines of the window's run under <c>m365-aegis-gui --verbose</c> (task 027): the broker's calls with the window
    /// as the account picker's parent, the silent sign-in of the page's <c>whoami</c>, and the bridge's internal error.
    /// </summary>
    private static readonly string[] WindowLines =
    [
        "False MSAL 4.91.1.0 MSAL.NetCore .NET 10.0.4 [2026-10-29 10:00:00Z - 00000000-0000-0000-0000-000000000005] [WAM] Parent window handle: 0x5b036c, the account picker is owned by m365-aegis-gui",
        "[RuntimeBroker] Silent request for account alice@fabrikam.com (home account id 00000000-0000-0000-0000-000000000001.00000000-0000-0000-0000-000000000002) in tenant fabrikam.com",
        "[RuntimeBroker] AcquireTokenInteractive: login_hint=alice@fabrikam.com&prompt=select_account; WebView2 data folder webview2",
        "[RuntimeBroker] Token response: {\"access_token\":\"opaque-window-token\",\"client_info\":\"eyJ1aWQiOiIwMDAwMDAwMCJ9\"}",
        "System.InvalidOperationException: the client broke for alice@fabrikam.com\n   at M365Aegis.Core.Bridge.BridgeDispatcher.GroupMembersAsync(BridgeMessage request, CancellationToken cancellationToken)",
    ];

    [Fact]
    public void TheWindowsLinesReachTheFileWithoutATokenOrAnAccount()
    {
        var sink = new LogSink(Folder());
        sink.Redact(Profile.Tenant);
        sink.Redact(Profile.ClientId);
        sink.Redact(Profile.Account);

        LogCallback callback = MsalTokenBroker.ToSink(sink);
        foreach (string line in WindowLines.SkipLast(1))
        {
            callback(LogLevel.Verbose, line, containsPii: false);
        }

        Assert.True(sink.Write("internal-error", WindowLines[^1]));

        string text = File.ReadAllText(sink.CurrentFile);
        string[] never = ["opaque-window-token", "eyJ1aWQi", "alice@fabrikam.com", "fabrikam.com", "00000000-0000-0000-0000-0000000000"];
        Assert.All(never, secret => Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase));
        // The step, the window handle and the failing frame stay: that is what a support case needs.
        Assert.Contains("Parent window handle: 0x5b036c", text, StringComparison.Ordinal);
        Assert.Contains("login_hint=<redacted>", text, StringComparison.Ordinal);
        Assert.Contains("BridgeDispatcher.GroupMembersAsync", text, StringComparison.Ordinal);
        Assert.Equal(WindowLines.Length + 1, File.ReadAllLines(sink.CurrentFile).Length);
    }

    [Fact]
    public void ThePatternsAloneCatchAnAccountThatWasNotRegistered()
    {
        string scrubbed = LogSink.ScrubPatterns("signed in bob@contoso.com (00000000-0000-0000-0000-000000000003) with eyJabcdefghijklmnop.qrs");

        Assert.Equal("signed in <upn> (<guid>) with <token>", scrubbed);
    }

    [Fact]
    public void ASinkThatWritesNothingCreatesNothing()
    {
        string folder = Folder();

        _ = new LogSink(folder);

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void AStackTraceKeepsItsLinesIndentedUnderOneEntry()
    {
        var sink = new LogSink(Folder());

        sink.Write("internal-error", "System.InvalidOperationException: boom\r\n   at A.B()\r\n   at C.D()");

        string[] lines = File.ReadAllLines(sink.CurrentFile);
        Assert.Equal(3, lines.Length);
        Assert.Contains($"[{Environment.ProcessId}] internal-error: System.InvalidOperationException: boom", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("       at A.B()", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnwritableFolderIsAFalseNotAnException()
    {
        string file = Path.Combine(Folder(), "a-file");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "");

        // The "folder" is a file: the directory cannot be created.
        Assert.False(new LogSink(file).Write("msal", "a line"));
    }

    [Fact]
    public void TheFileIsNamedByTheDayUnderTheHomesLogsFolder()
    {
        string home = ProfileStoreTests.TempHome();

        LogSink sink = LogSink.ForHome(home);

        Assert.Equal(Path.Combine(home, "logs"), sink.Folder);
        Assert.Matches(@"m365-aegis-\d{4}-\d{2}-\d{2}\.log$", sink.CurrentFile);
    }

    [Fact]
    public void WithoutASinkTheMsalClientHasNoLoggingCallback()
    {
        IPublicClientApplication application = MsalTokenBroker.WithLog(Builder(), null, Profile).Build();

        Assert.Null(application.AppConfig.LoggingCallback);
    }

    [Fact]
    public void WithASinkTheMsalClientLogsWithoutPii()
    {
        var sink = new LogSink(Folder());

        IPublicClientApplication application = MsalTokenBroker.WithLog(Builder(), sink, Profile).Build();

        Assert.NotNull(application.AppConfig.LoggingCallback);
        Assert.False(application.AppConfig.EnablePiiLogging);
        Assert.False(application.AppConfig.IsDefaultPlatformLoggingEnabled);
        // The profile's values are registered for redaction.
        Assert.Equal("for <redacted> as <redacted>", sink.Scrub("for fabrikam.com as alice@fabrikam.com"));
    }

    [Fact]
    public void TheCallbackDropsALineMarkedAsCarryingPii()
    {
        var sink = new LogSink(Folder());
        LogCallback callback = MsalTokenBroker.ToSink(sink);

        callback(LogLevel.Verbose, "the account alice@fabrikam.com, with PII", containsPii: true);
        Assert.False(Directory.Exists(sink.Folder));

        callback(LogLevel.Error, "the broker failed", containsPii: false);
        Assert.Contains("msal: Error: the broker failed", File.ReadAllText(sink.CurrentFile), StringComparison.Ordinal);
    }

    private static PublicClientApplicationBuilder Builder() =>
        PublicClientApplicationBuilder.Create(Profile.ClientId).WithTenantId("contoso.onmicrosoft.com");

    private static string Folder() => Path.Combine(ProfileStoreTests.TempHome(), "logs");
}
