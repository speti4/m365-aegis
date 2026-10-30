using M365Aegis.Core.Auth;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Tests.Auth;

/// <summary>
/// The window's own hints for the failures after the sign-in (task 057): the window's set has a hint for every failure
/// kind, none of them names a command, and a failure the core makes (Graph's, the Admin API's, the sidecar's, an
/// internal error) reaches the window with a hint that names none either, its kind and sentence unchanged; the command
/// line's set leaves every failure as it is.
/// </summary>
public sealed class WindowFailureHintsTests
{
    /// <summary>The failures the core's layers make, as they reach the bridge.</summary>
    public static TheoryData<string> Failures { get; } =
    [
        "graph-401", "graph-403", "graph-404", "graph-429", "graph-500", "graph-400",
        "admin-api-401", "admin-api-403", "admin-api-404", "admin-api-429", "admin-api-500", "admin-api-400",
        "sidecar-module-missing", "sidecar-token-expired", "sidecar-access-denied", "sidecar-throttled", "sidecar-not-found",
        "sidecar-failed", "sidecar-not-allowed", "internal-without-log", "internal-with-log",
    ];

    [Fact]
    public void NoHintNamesNoCommand() => Assert.False(SignInHints.NamesACommand(null));

    [Fact]
    public void TheWindowHasAHintForEveryFailureKindAndNoneNamesACommand()
    {
        Assert.Equal(Enum.GetValues<FailureKind>().Order(), SignInHints.Window.Failures.Keys.Order());
        foreach (string hint in SignInHints.Window.Failures.Values)
        {
            Assert.False(string.IsNullOrWhiteSpace(hint));
            Assert.False(SignInHints.NamesACommand(hint), hint);
            Assert.DoesNotContain("m365-aegis", hint, StringComparison.Ordinal);
        }

        Assert.Equal(SignInHints.Window.SignInAgain, SignInHints.Window.Failures[FailureKind.Auth]);
        Assert.Empty(SignInHints.CommandLine.Failures);
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public void AFailureOfTheCoreReachesTheWindowWithAHintThatNamesNoCommand(string name)
    {
        AegisFailure failure = Make(name);

        AegisFailure shown = SignInHints.Window.ForSurface(failure);

        Assert.Equal((failure.Kind, failure.Message), (shown.Kind, shown.Message));
        Assert.False(string.IsNullOrWhiteSpace(shown.Hint));
        Assert.False(SignInHints.NamesACommand(shown.Hint), shown.Hint);
        Assert.Equal(SignInHints.NamesACommand(failure.Hint) ? SignInHints.Window.Failures[failure.Kind] : failure.Hint, shown.Hint);
        Assert.Same(failure, SignInHints.CommandLine.ForSurface(failure));
    }

    [Theory]
    [InlineData("run `m365-aegis doctor`: it checks pwsh, the module and the sidecar script", true)]
    [InlineData("Install-Module ExchangeOnlineManagement -Scope CurrentUser", true)]
    [InlineData("winget install Microsoft.PowerShell", true)]
    [InlineData("log in with the tenant's .onmicrosoft.com name as --tenant, or set M365AEGIS_EXO_CONNECT=upn", true)]
    [InlineData("wait a few minutes and run the command again", true)]
    [InlineData("give the exact display name, the address, the object id, or a word of the name", false)]
    [InlineData(@"the stack trace is in C:\Temp\m365-aegis\logs\m365-aegis.log; report it with that log", false)]
    [InlineData("choose Sign in with a code to sign in through a browser instead", false)]
    public void AHintNamesACommandWhenAShellWouldRunIt(string hint, bool expected)
    {
        Assert.Equal(expected, SignInHints.NamesACommand(hint));
    }

    [Fact]
    public void AFailureWithoutAHintGetsTheWindowsHintOfItsKind()
    {
        var failure = new AegisFailure(FailureKind.ServiceError, "something went wrong");

        Assert.Equal(SignInHints.Window.Failures[FailureKind.ServiceError], SignInHints.Window.ForSurface(failure).Hint);
        Assert.Null(SignInHints.CommandLine.ForSurface(failure).Hint);
    }

    /// <summary>A failure as the core's own code makes it.</summary>
    private static AegisFailure Make(string name)
    {
        var request = new GraphRequest(HttpMethod.Get, "/groups");
        var command = new ExoCommand(AdminApiCmdlets.GetMailbox);
        var none = new Dictionary<string, string>();
        string body = """{"error":{"code":"Refused","message":"refused on purpose"}}""";
        Exception exception = name switch
        {
            "graph-401" => GraphClient.Failure(request, 401, none, "InvalidAuthenticationToken", "expired"),
            "graph-403" => GraphClient.Failure(request, 403, none, "Authorization_RequestDenied", "denied"),
            "graph-404" => GraphClient.Failure(request, 404, none, "Request_ResourceNotFound", "gone"),
            "graph-429" => GraphClient.Failure(request, 429, none, "TooManyRequests", "slow down"),
            "graph-500" => GraphClient.Failure(request, 500, none, "InternalServerError", "broken"),
            "graph-400" => GraphClient.Failure(request, 400, none, "BadRequest", "bad"),
            "admin-api-401" => AdminApiClient.Failure(command, new GraphResponse(401, [], body)),
            "admin-api-403" => AdminApiClient.Failure(command, new GraphResponse(403, [], body)),
            "admin-api-404" => AdminApiClient.Failure(command, new GraphResponse(404, [], body)),
            "admin-api-429" => AdminApiClient.Failure(command, new GraphResponse(429, [], body)),
            "admin-api-500" => AdminApiClient.Failure(command, new GraphResponse(500, [], body)),
            "admin-api-400" => AdminApiClient.Failure(command, new GraphResponse(400, [], body)),
            "sidecar-module-missing" => SidecarFailures.For("invoke", new SidecarError("module-missing", "no module", null, null), ""),
            "sidecar-token-expired" => SidecarFailures.For("connect", new SidecarError("connect-failed", "AADSTS70043: the token has expired", null, null), ""),
            "sidecar-access-denied" => SidecarFailures.For("invoke", new SidecarError("cmdlet-failed", "Access denied", null, null), ""),
            "sidecar-throttled" => SidecarFailures.For("invoke", new SidecarError("cmdlet-failed", "Server busy: the request was throttled", null, null), ""),
            "sidecar-not-found" => SidecarFailures.For("invoke", new SidecarError("cmdlet-failed", "The object couldn't be found on 'server'", null, null), ""),
            "sidecar-failed" => SidecarFailures.For("invoke", new SidecarError("cmdlet-failed", "Something odd happened", null, null), ""),
            "sidecar-not-allowed" => SidecarFailures.For("invoke", new SidecarError("cmdlet-not-allowed", "'Remove-Mailbox' is not on the sidecar's allow-list", null, null), ""),
            "internal-without-log" => new AegisException(InternalError.ToFailure(new InvalidOperationException("broken"), null)),
            "internal-with-log" => new AegisException(InternalError.ToFailure(new InvalidOperationException("broken"), Path.Combine(Path.GetTempPath(), "aegis.log"))),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
        };
        return Assert.IsType<AegisException>(exception).Failure;
    }
}
