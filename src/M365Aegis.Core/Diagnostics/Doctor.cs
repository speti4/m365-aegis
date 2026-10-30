using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Config;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Diagnostics;

/// <summary>The outcome of one doctor check.</summary>
public enum CheckStatus
{
    /// <summary>The prerequisite is there.</summary>
    Pass,

    /// <summary>Something is off but the tool can run.</summary>
    Warn,

    /// <summary>The prerequisite is missing.</summary>
    Fail,

    /// <summary>Not checked in this run (offline, another platform, or a check it depends on failed).</summary>
    Skipped,
}

/// <summary>One line of the doctor report.</summary>
/// <param name="Name">A short, stable name of the check.</param>
/// <param name="Status">The outcome.</param>
/// <param name="Detail">What was found, one line.</param>
/// <param name="Required">Whether a failure of this check fails the report (exit code 10).</param>
/// <param name="Hint">What to install or do about a warning or a failure, or null.</param>
public sealed record DoctorCheck(string Name, CheckStatus Status, string Detail, bool Required, string? Hint = null);

/// <summary>The doctor report: the checks and the verdict.</summary>
/// <param name="Home">The home folder the checks looked at.</param>
/// <param name="Checks">The checks, in the order they ran.</param>
public sealed record DoctorReport(string Home, IReadOnlyList<DoctorCheck> Checks)
{
    /// <summary>False when a required check failed.</summary>
    public bool Healthy => Checks.All(check => check.Status != CheckStatus.Fail || !check.Required);

    /// <summary>
    /// The report as a command result: a success when healthy, else a <see cref="FailureKind.DoctorFailed"/>
    /// failure (exit code 10) with the report as its partial data, so the caller still sees every check.
    /// </summary>
    public CommandResult<DoctorReport> ToResult() => Healthy
        ? CommandResult.Success(this)
        : CommandResult.Failure(
            new AegisFailure(FailureKind.DoctorFailed, "a required check failed", "the checks marked FAIL name what is missing, the fix lines what to do"),
            this);
}

/// <summary>
/// The prerequisites of the tool, checked one by one (task 008): the home folder and its access list, the .NET runtime, PowerShell 7.6
/// and the ExchangeOnlineManagement module for the Exchange Online sidecar, the sidecar script against the copy
/// compiled into the Core, the WebView2 runtime of the window, the tenant profiles, the device's join state, the
/// sign-in, and the three endpoints. Everything the checks read comes through <see cref="DoctorEnvironment"/>;
/// <c>--offline</c> skips the two checks that need the network (the sign-in and the endpoints).
/// </summary>
public static class Doctor
{
    /// <summary>The PowerShell version the ExchangeOnlineManagement module (3.10 and later) requires.</summary>
    public static readonly Version MinimumPwsh = new(7, 6, 0);

    /// <summary>The ExchangeOnlineManagement version the sidecar is written against.</summary>
    public static readonly Version MinimumExchangeModule = new(3, 10, 0);

    /// <summary>A WebView2 runtime older than this one means Edge Update is not keeping the Evergreen runtime current.</summary>
    public static readonly Version MinimumWebView2 = new(120, 0);

    /// <summary>An endpoint that takes longer than this to answer is a warning.</summary>
    public static readonly TimeSpan SlowEndpoint = TimeSpan.FromSeconds(3);

    /// <summary>The endpoints the tool talks to: the sign-in, Microsoft Graph, Exchange Online.</summary>
    public static IReadOnlyList<(string Name, Uri Uri)> Endpoints { get; } =
    [
        ("endpoint-login", new Uri("https://login.microsoftonline.com/")),
        ("endpoint-graph", new Uri("https://graph.microsoft.com/v1.0/")),
        ("endpoint-exchange", new Uri("https://outlook.office365.com/")),
    ];

    /// <summary>
    /// The pwsh script of the pwsh and module checks: one call answers both (a pwsh start costs a second). With
    /// <c>M365AEGIS_EXO_MODULE_ROOT</c> set it looks for the module where the sidecar would, in that folder only.
    /// </summary>
    internal const string PwshScript =
        "'pwsh=' + $PSVersionTable.PSVersion.ToString(); " +
        "$root = $env:M365AEGIS_EXO_MODULE_ROOT; " +
        "if ($root) { $m = Get-Module -ListAvailable (Join-Path $root 'ExchangeOnlineManagement' 'ExchangeOnlineManagement.psd1') -ErrorAction SilentlyContinue | Select-Object -First 1 } " +
        "else { $m = Get-Module -ListAvailable -Name ExchangeOnlineManagement | Sort-Object Version -Descending | Select-Object -First 1 }; " +
        "'module=' + $(if ($m) { $m.Version.ToString() } else { '' })";

    /// <summary>Runs every check on this machine.</summary>
    public static Task<DoctorReport> RunAsync(string home, string? profile, bool offline, CancellationToken cancellationToken) =>
        RunAsync(DoctorEnvironment.ForThisMachine(home, profile), offline, cancellationToken);

    /// <summary>Runs every check against the given environment (the tests fake it).</summary>
    public static async Task<DoctorReport> RunAsync(DoctorEnvironment environment, bool offline, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);

        string? pwshPath = environment.FindPwsh();
        PwshAnswer? pwsh = pwshPath is null ? null : PwshAnswer.Parse(environment.RunPwsh(pwshPath, PwshScript));
        var checks = new List<DoctorCheck>
        {
            CheckHome(environment.Home),
            CheckHomeAccess(environment),
            new("dotnet-runtime", CheckStatus.Pass, environment.RuntimeVersion, Required: false),
            CheckDesktopRuntime(environment),
            CheckPwsh(pwshPath, pwsh),
            CheckExchangeModule(pwshPath, pwsh),
            CheckSidecarScript(environment.ReadInstalledSidecar(), environment.ReadBuildSidecar()),
            CheckWebView2(environment),
            CheckConfig(environment.Home),
            CheckDevice(environment),
        };

        if (offline)
        {
            checks.Add(new("sign-in", CheckStatus.Skipped, "offline run", Required: false));
            checks.AddRange(Endpoints.Select(endpoint => new DoctorCheck(endpoint.Name, CheckStatus.Skipped, "offline run", Required: true)));
        }
        else
        {
            checks.Add(await CheckSignInAsync(environment, cancellationToken).ConfigureAwait(false));
            DoctorCheck[] endpoints = await Task.WhenAll(Endpoints.Select(async endpoint =>
                CheckEndpoint(endpoint.Name, endpoint.Uri, await environment.Probe(endpoint.Uri, cancellationToken).ConfigureAwait(false)))).ConfigureAwait(false);
            checks.AddRange(endpoints);
        }

        return new DoctorReport(environment.Home, checks);
    }

    /// <summary>The pwsh executable on PATH, or null.</summary>
    public static string? FindPwsh() => FindPwsh(Environment.GetEnvironmentVariable("PATH"), PwshFileName());

    /// <summary>The file name of pwsh on this operating system.</summary>
    [ExcludeFromCodeCoverage(Justification = "one side per operating system (docs/development.md, Coverage)")]
    internal static string PwshFileName() => OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";

    /// <summary>The first folder of a PATH value that holds <paramref name="fileName"/>, as a full file name, or null.</summary>
    internal static string? FindPwsh(string? pathVariable, string fileName)
    {
        if (string.IsNullOrEmpty(pathVariable))
        {
            return null;
        }

        foreach (string folder in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(folder.Trim(), fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    internal static DoctorCheck CheckHome(string home)
    {
        if (Directory.Exists(home))
        {
            return new("home", CheckStatus.Pass, home, Required: true);
        }

        if (File.Exists(home))
        {
            return new("home", CheckStatus.Fail, $"{home} is a file, not a folder", Required: true,
                $"move the file away, or point {AegisHome.EnvironmentVariable} at a folder");
        }

        return new("home", CheckStatus.Warn, $"{home} does not exist yet; the first sign-in creates it", Required: true);
    }

    /// <summary>
    /// The groups whose read right on the home folder lets every other account of the machine read it, by their
    /// well-known SIDs: Everyone, Users and Authenticated Users (task 078).
    /// </summary>
    internal static IReadOnlyList<(string Sid, string Name)> BroadReaders { get; } =
    [
        ("S-1-1-0", "Everyone"),
        ("S-1-5-32-545", "Users"),
        ("S-1-5-11", "Authenticated Users"),
    ];

    /// <summary>
    /// The access rights that read what is in a folder: list the folder (a file's read data, the same bit), and the
    /// generic read and generic all rights an inheritable entry may carry instead of the specific ones.
    /// </summary>
    internal const int ReadRights = 0x1 | 0x10000000 | unchecked((int)0x80000000);

    /// <summary>
    /// The home folder's access list on Windows (task 078): a warning when Everyone, Users or Authenticated Users has
    /// a read right on it (an allow entry, explicit or inherited; a deny entry is not weighed against it), with the
    /// <c>icacls</c> line that gives it the list a home the product creates has (task 075, <see cref="AegisHome.Create"/>):
    /// nothing inherited, full control for the user, SYSTEM and Administrators, the three groups removed. The doctor
    /// does not change the list itself. Not required: the tool runs either way.
    /// </summary>
    internal static DoctorCheck CheckHomeAccess(DoctorEnvironment environment)
    {
        const string name = "home-access";
        if (!environment.IsWindows)
        {
            return new(name, CheckStatus.Skipped, "not Windows; the home folder's access list is a Windows rule", Required: false);
        }

        string home = environment.Home;
        if (!Directory.Exists(home))
        {
            return new(name, CheckStatus.Skipped, "no home folder yet; the product creates it readable by the user only", Required: false);
        }

        if (environment.ReadHomeAccess(home) is not { } access)
        {
            return new(name, CheckStatus.Warn, "the home folder's access list could not be read", Required: false, $"icacls \"{home}\" shows it");
        }

        List<string> readers = [.. BroadReaders
            .Select(reader => (reader.Name, Entries: access.Entries.Where(entry => entry.Allow && entry.Sid == reader.Sid && (entry.Rights & ReadRights) != 0).ToList()))
            .Where(found => found.Entries.Count > 0)
            .Select(found => found.Entries.All(entry => entry.Inherited) ? $"{found.Name} (inherited)" : found.Name)];
        if (readers.Count == 0)
        {
            return new(name, CheckStatus.Pass, "neither Everyone, Users nor Authenticated Users can read it", Required: false);
        }

        return new(name, CheckStatus.Warn,
            $"{string.Join(", ", readers)} can read it: every account on this machine can read the profiles, the plan records and the logs",
            Required: false, HomeAccessFix(home, access.User));
    }

    /// <summary>The <c>icacls</c> line that gives a home the product's own list; it runs the same in pwsh and in cmd.</summary>
    internal static string HomeAccessFix(string home, string user)
    {
        string grants = string.Join(" ", new[] { user, "*S-1-5-18", "*S-1-5-32-544" }.Select(sid => $"\"{sid}:(OI)(CI)F\""));
        string removes = string.Join(" ", BroadReaders.Select(reader => $"*{reader.Sid}"));
        return $"icacls \"{home}\" /inheritance:r /grant:r {grants} /remove:g {removes}";
    }

    internal static DoctorCheck CheckPwsh(string? pwshPath, PwshAnswer? answer)
    {
        const string install = "winget install Microsoft.PowerShell";
        if (pwshPath is null)
        {
            return new("pwsh", CheckStatus.Fail, "pwsh not found on PATH; PowerShell 7.6 or later is needed for Exchange Online", Required: true, install);
        }

        if (answer?.Pwsh is not { } version)
        {
            return new("pwsh", CheckStatus.Warn, $"{pwshPath}: version unreadable", Required: true, "run `pwsh -NoProfile` once to see why it does not start");
        }

        return version >= MinimumPwsh
            ? new("pwsh", CheckStatus.Pass, $"{version} at {pwshPath}", Required: true)
            : new("pwsh", CheckStatus.Fail, $"{version} at {pwshPath}; {MinimumPwsh} or later is needed", Required: true, "winget upgrade Microsoft.PowerShell");
    }

    internal static DoctorCheck CheckExchangeModule(string? pwshPath, PwshAnswer? answer)
    {
        // Not required: the Graph domains work without it; the Exchange Online cmdlets of the sidecar do not.
        const string name = "exchange-online-module";
        if (pwshPath is null || answer is null)
        {
            return new(name, CheckStatus.Skipped, "pwsh did not answer", Required: false);
        }

        if (answer.Module is not { } version)
        {
            return new(name, CheckStatus.Fail, "ExchangeOnlineManagement is not installed; the Exchange Online cmdlets cannot run", Required: false,
                "Install-Module ExchangeOnlineManagement -Scope CurrentUser");
        }

        return version >= MinimumExchangeModule
            ? new(name, CheckStatus.Pass, version.ToString(), Required: false)
            : new(name, CheckStatus.Warn, $"{version} is older than {MinimumExchangeModule.ToString(2)}, which the sidecar is written against", Required: false,
                "Update-Module ExchangeOnlineManagement");
    }

    internal static DoctorCheck CheckSidecarScript(byte[]? installed, byte[]? build)
    {
        const string name = "sidecar-script";
        const string reinstall = "reinstall the release zip (SHA256SUMS.txt checks it), or rebuild";
        if (build is null)
        {
            return new(name, CheckStatus.Fail, "this build carries no copy of the script to compare with", Required: true, reinstall);
        }

        string expected = SidecarScript.Sha256(build);
        if (installed is null)
        {
            return new(name, CheckStatus.Fail, $"sidecar\\{SidecarScript.FileName} is missing next to the exe", Required: true, reinstall);
        }

        string actual = SidecarScript.Sha256(installed);
        if (actual == expected)
        {
            return new(name, CheckStatus.Pass, $"sha256 {Short(actual)} matches the build", Required: true);
        }

        // Only the line endings differ (an editor or an unzip tool rewrote them): the script does the same, but it is
        // not the file the build shipped.
        return SidecarScript.Sha256(WithLf(installed)) == SidecarScript.Sha256(WithLf(build))
            ? new(name, CheckStatus.Warn, $"sha256 {Short(actual)} differs from the build ({Short(expected)}) in the line endings only", Required: true, reinstall)
            : new(name, CheckStatus.Fail, $"sha256 {Short(actual)} differs from the build ({Short(expected)}): the script was changed after the build", Required: true, reinstall);
    }

    /// <summary>
    /// The .NET Desktop Runtime the window needs (a WinForms form, D-009), in the major version of the .NET Runtime:
    /// missing fails (the window's apphost would only say that a framework is missing), a patch behind the .NET
    /// Runtime warns (an update reached one and not the other). Not required: the command line runs without it.
    /// </summary>
    internal static DoctorCheck CheckDesktopRuntime(DoctorEnvironment environment)
    {
        const string name = "dotnet-desktop-runtime";
        const string winget = "Microsoft.DotNet.DesktopRuntime.10";
        if (!environment.IsWindows)
        {
            return new(name, CheckStatus.Skipped, "not Windows; the window runs on Windows only", Required: false);
        }

        Version? runtime = Version.TryParse(environment.RuntimeVersion, out Version? parsed) ? parsed : null;
        int major = runtime?.Major ?? 10;
        List<Version> installed = [.. environment.ReadDesktopRuntimeVersions()
            .Select(value => Version.TryParse(value, out Version? version) ? version : null)
            .OfType<Version>()
            .Order()];
        Version? newest = installed.LastOrDefault(version => version.Major == major);
        if (newest is null)
        {
            string others = installed.Count == 0 ? "" : $" (installed: {string.Join(", ", installed)})";
            return new(name, CheckStatus.Fail,
                $"the .NET Desktop Runtime {major} is not installed: the window cannot start (the command line can){others}",
                Required: false, $"winget install {winget}");
        }

        return runtime is not null && newest < runtime
            ? new(name, CheckStatus.Warn, $"{newest} is older than the .NET Runtime {runtime}: an update reached one of the two only", Required: false,
                $"winget upgrade {winget}")
            : new(name, CheckStatus.Pass, newest.ToString(), Required: false);
    }

    internal static DoctorCheck CheckWebView2(DoctorEnvironment environment)
    {
        const string name = "webview2-runtime";
        if (!environment.IsWindows)
        {
            return new(name, CheckStatus.Skipped, "not Windows; the window runs on Windows only", Required: false);
        }

        // A key whose pv is empty or 0.0.0.0 is a runtime that is not installed (Microsoft's distribution guide).
        Version? newest = environment.ReadWebView2Versions()
            .Select(value => Version.TryParse(value, out Version? version) ? version : null)
            .Where(version => version is not null && version > new Version(0, 0, 0, 0))
            .Max();
        if (newest is null)
        {
            return new(name, CheckStatus.Fail, "the WebView2 runtime is not installed: the window cannot start (the command line can)", Required: false,
                "winget install Microsoft.EdgeWebView2Runtime");
        }

        return newest >= MinimumWebView2
            ? new(name, CheckStatus.Pass, newest.ToString(), Required: false)
            : new(name, CheckStatus.Warn, $"{newest} is old: Edge Update keeps the Evergreen runtime current, so it is blocked here", Required: false,
                "winget upgrade Microsoft.EdgeWebView2Runtime");
    }

    internal static DoctorCheck CheckConfig(string home)
    {
        const string name = "config";
        var store = new ProfileStore(home);
        if (!File.Exists(store.Path))
        {
            return new(name, CheckStatus.Warn, $"{store.Path} not found", Required: false, ProfileStore.LoginHint);
        }

        ProfileList profiles;
        try
        {
            profiles = store.Describe();
        }
        catch (AegisException exception)
        {
            return new(name, CheckStatus.Fail, exception.Failure.Message, Required: false, exception.Failure.Hint);
        }

        if (profiles.Profiles.Count == 0)
        {
            return new(name, CheckStatus.Warn, $"{store.Path} holds no profile", Required: false, ProfileStore.LoginHint);
        }

        string count = string.Format(CultureInfo.InvariantCulture, "{0} profile(s)", profiles.Profiles.Count);
        return profiles.Default is { } named
            ? new(name, CheckStatus.Pass, $"{store.Path}: {count}, default '{named}'", Required: false)
            : new(name, CheckStatus.Warn, $"{store.Path}: {count} and no default, so every command needs --profile", Required: false,
                "`m365-aegis profile use <name>` picks one");
    }

    internal static DoctorCheck CheckDevice(DoctorEnvironment environment)
    {
        const string name = "device";
        if (!environment.IsWindows)
        {
            return new(name, CheckStatus.Skipped, "not Windows", Required: false);
        }

        DeviceState? state = DeviceState.Parse(environment.ReadDeviceState());
        if (state is null)
        {
            return new(name, CheckStatus.Warn, "`dsregcmd /status` could not be read", Required: false, "run `dsregcmd /status` to see why");
        }

        // Task 013: the silent sign-in as the Windows account needs a primary refresh token, which only an
        // Entra-joined machine has; a registered machine signs in as the profile's account through the broker.
        if (state.EntraJoined)
        {
            return state.PrimaryRefreshToken
                ? new(name, CheckStatus.Pass, "Entra-joined with a primary refresh token: the Windows account signs in silently", Required: false)
                : new(name, CheckStatus.Warn, "Entra-joined without a primary refresh token: the silent sign-in as the Windows account fails; the profile's account signs in instead", Required: false,
                    "lock and unlock Windows, or sign out and in again, to renew the primary refresh token");
        }

        if (state.WorkplaceRegistered)
        {
            return new(name, CheckStatus.Warn, "registered, not Entra-joined: no primary refresh token, so the silent sign-in as the Windows account fails (normal here); the profile's account signs in through the broker", Required: false);
        }

        return new(name, CheckStatus.Fail, "neither Entra-joined nor registered: a Conditional Access policy that wants a known device blocks the sign-in", Required: false,
            "Settings > Accounts > Access work or school > Connect");
    }

    internal static async Task<DoctorCheck> CheckSignInAsync(DoctorEnvironment environment, CancellationToken cancellationToken)
    {
        const string name = "sign-in";
        try
        {
            // The profile first, so a missing one never reaches MSAL.
            _ = new ProfileStore(environment.Home).Resolve(environment.Profile);
        }
        catch (AegisException exception)
        {
            return new(name, CheckStatus.Warn, exception.Failure.Message, Required: false, exception.Failure.Hint);
        }

        SignInInfo info;
        try
        {
            info = await environment.SignIn(environment.Profile, cancellationToken).ConfigureAwait(false);
        }
        catch (AegisException exception)
        {
            return new(name, CheckStatus.Fail, exception.Failure.Message, Required: false, exception.Failure.Hint);
        }
        catch (BrokerException exception)
        {
            return new(name, CheckStatus.Fail, $"the broker did not answer ({exception.Code}: {exception.Message})", Required: false, "run `m365-aegis login`");
        }

        string tokens = string.Join(", ", info.Tokens.Select(token => token.ExpiresOn is { } expiresOn
            ? string.Format(CultureInfo.InvariantCulture, "{0} token until {1:yyyy-MM-dd HH:mm zzz}", ResourceName(token.Resource), expiresOn)
            : $"no {ResourceName(token.Resource)} token ({token.Error ?? "no reason given"})"));
        string detail = $"{info.Account}: {tokens}";
        return info.Tokens.All(token => token.ExpiresOn is not null)
            ? new(name, CheckStatus.Pass, detail, Required: false)
            : new(name, CheckStatus.Warn, detail, Required: false, "an administrator consents to the missing permissions in the portal, then `m365-aegis login`");
    }

    internal static DoctorCheck CheckEndpoint(string name, Uri endpoint, EndpointAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(answer);
        string host = endpoint.Host;
        string elapsed = string.Format(CultureInfo.InvariantCulture, "{0:0} ms", answer.Elapsed.TotalMilliseconds);
        if (answer.StatusCode is not { } status)
        {
            return new(name, CheckStatus.Fail, $"{host}: no answer after {elapsed} ({answer.Error ?? "no reason given"})", Required: true,
                $"check the network, the proxy and the firewall for https://{host}");
        }

        if (status == 407)
        {
            return new(name, CheckStatus.Warn, $"{host}: a proxy asks for authentication (HTTP 407)", Required: true,
                $"let https://{host} through the proxy without authentication");
        }

        string answered = string.Format(CultureInfo.InvariantCulture, "{0}: HTTP {1} in {2}", host, status, elapsed);
        return answer.Elapsed > SlowEndpoint
            ? new(name, CheckStatus.Warn, $"{answered}, slow", Required: true, $"check the network path to https://{host}")
            : new(name, CheckStatus.Pass, answered, Required: true);
    }

    internal static string ResourceName(Resource resource) => resource switch
    {
        Resource.Graph => "graph",
        Resource.Exchange => "exchange",
        _ => "?",
    };

    private static string Short(string sha256) => sha256[..12];

    private static byte[] WithLf(byte[] bytes) =>
        Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).ReplaceLineEndings("\n"));
}

/// <summary>What one pwsh call told the doctor: its own version and the newest ExchangeOnlineManagement, each null when unreadable.</summary>
/// <param name="Pwsh">The pwsh version (a preview's numeric part).</param>
/// <param name="Module">The module version, or null when it is not installed.</param>
internal sealed record PwshAnswer(Version? Pwsh, Version? Module)
{
    /// <summary>The <c>pwsh=…</c> and <c>module=…</c> lines of <see cref="Doctor.PwshScript"/>; null when pwsh did not answer.</summary>
    internal static PwshAnswer? Parse(string? output)
    {
        if (output is null)
        {
            return null;
        }

        Version? pwsh = null;
        Version? module = null;
        foreach (string line in output.ReplaceLineEndings("\n").Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("pwsh=", StringComparison.Ordinal))
            {
                pwsh = ParseVersion(trimmed["pwsh=".Length..]);
            }
            else if (trimmed.StartsWith("module=", StringComparison.Ordinal))
            {
                module = ParseVersion(trimmed["module=".Length..]);
            }
        }

        return new PwshAnswer(pwsh, module);
    }

    private static Version? ParseVersion(string text)
    {
        // A preview reads "7.6.0-preview.4"; the numeric part is what the comparison needs.
        int dash = text.IndexOf('-', StringComparison.Ordinal);
        return Version.TryParse(dash < 0 ? text : text[..dash], out Version? version) ? version : null;
    }
}

/// <summary>The join state <c>dsregcmd /status</c> reports, the three lines the doctor needs and nothing that names the tenant.</summary>
/// <param name="EntraJoined"><c>AzureAdJoined : YES</c>.</param>
/// <param name="WorkplaceRegistered"><c>WorkplaceJoined : YES</c>.</param>
/// <param name="PrimaryRefreshToken"><c>AzureAdPrt : YES</c>.</param>
internal sealed record DeviceState(bool EntraJoined, bool WorkplaceRegistered, bool PrimaryRefreshToken)
{
    /// <summary>The state, or null when the output has none of the lines (dsregcmd did not run).</summary>
    internal static DeviceState? Parse(string? output)
    {
        if (output is null)
        {
            return null;
        }

        var values = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in output.ReplaceLineEndings("\n").Split('\n'))
        {
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            string key = line[..colon].Trim();
            if (key is "AzureAdJoined" or "WorkplaceJoined" or "AzureAdPrt")
            {
                values[key] = string.Equals(line[(colon + 1)..].Trim(), "YES", StringComparison.OrdinalIgnoreCase);
            }
        }

        if (!values.ContainsKey("AzureAdJoined") && !values.ContainsKey("WorkplaceJoined"))
        {
            return null;
        }

        return new DeviceState(
            values.GetValueOrDefault("AzureAdJoined"),
            values.GetValueOrDefault("WorkplaceJoined"),
            values.GetValueOrDefault("AzureAdPrt"));
    }
}
