using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Exchange;
using Microsoft.Win32;

namespace M365Aegis.Core.Diagnostics;

/// <summary>An endpoint's answer to the doctor's probe: an HTTP status (any status means reachable), or the error.</summary>
/// <param name="StatusCode">The HTTP status, or null when nothing answered.</param>
/// <param name="Elapsed">How long the answer took.</param>
/// <param name="Error">Why nothing answered, or null.</param>
public sealed record EndpointAnswer(int? StatusCode, TimeSpan Elapsed, string? Error);

/// <summary>The home folder's access list as the doctor reads it on Windows (task 078).</summary>
/// <param name="User">The account the process runs as (<c>MACHINE\name</c> or <c>DOMAIN\name</c>), for the fix line.</param>
/// <param name="Entries">Every entry of the folder's access list, explicit and inherited.</param>
public sealed record HomeAccess(string User, IReadOnlyList<AccessEntry> Entries);

/// <summary>One entry of a folder's access list, without the Windows types, so the check over it runs on every runner.</summary>
/// <param name="Sid">The SID the entry is for, as a string (<c>S-1-5-32-545</c>).</param>
/// <param name="Allow">An allow entry (false: a deny entry).</param>
/// <param name="Rights">The access mask (<c>FileSystemRights</c>, generic rights included).</param>
/// <param name="Inherited">Inherited from a folder above.</param>
public sealed record AccessEntry(string Sid, bool Allow, int Rights, bool Inherited);

/// <summary>
/// What the doctor reads from the machine, each behind a seam the tests replace (task 008): the home folder's access
/// list, the pwsh call, the registry, <c>dsregcmd</c>, the sidecar script's two copies, the sign-in and the endpoint
/// probe. <see cref="ForThisMachine"/> wires the real ones.
/// </summary>
public sealed record DoctorEnvironment
{
    /// <summary>The home folder the checks look at.</summary>
    public required string Home { get; init; }

    /// <summary>The profile the sign-in check uses; null for the default.</summary>
    public string? Profile { get; init; }

    /// <summary>Whether this is Windows; the registry and <c>dsregcmd</c> checks are skipped elsewhere.</summary>
    public bool IsWindows { get; init; } = OperatingSystem.IsWindows();

    /// <summary>The access list of an existing home folder (the path), or null when it could not be read.</summary>
    public Func<string, HomeAccess?> ReadHomeAccess { get; init; } = ReadHomeAccessFromDisk;

    /// <summary>The .NET runtime the process runs on.</summary>
    public string RuntimeVersion { get; init; } = Environment.Version.ToString();

    /// <summary>
    /// The versions of the installed .NET Desktop Runtime (the <c>Microsoft.WindowsDesktop.App</c> shared framework the
    /// window needs, D-009), read from the shared folder of the .NET installation this process runs on.
    /// </summary>
    public Func<IReadOnlyList<string>> ReadDesktopRuntimeVersions { get; init; } = ReadDesktopRuntimeVersionsFromDisk;

    /// <summary>The pwsh executable, or null when there is none on PATH.</summary>
    public Func<string?> FindPwsh { get; init; } = Doctor.FindPwsh;

    /// <summary>Runs a script in pwsh (the path, the script) and returns its standard output, or null when it did not run.</summary>
    public Func<string, string, string?> RunPwsh { get; init; } = RunPwshScript;

    /// <summary>The <c>pv</c> values of the WebView2 runtime's registry keys (per machine, per user), null where a key is missing.</summary>
    public Func<IReadOnlyList<string?>> ReadWebView2Versions { get; init; } = ReadWebView2VersionsFromRegistry;

    /// <summary>The output of <c>dsregcmd /status</c>, or null when it could not be read.</summary>
    public Func<string?> ReadDeviceState { get; init; } = RunDsregcmd;

    /// <summary>The sidecar script next to the exe, or null when the file is missing.</summary>
    public Func<byte[]?> ReadInstalledSidecar { get; init; } = ReadInstalledSidecarScript;

    /// <summary>The sidecar script compiled into the Core, or null when the resource is missing.</summary>
    public Func<byte[]?> ReadBuildSidecar { get; init; } = SidecarScript.ReadBuildCopy;

    /// <summary>The silent sign-in of <c>whoami</c> for a profile (the profile name, or null for the default).</summary>
    public Func<string?, CancellationToken, Task<SignInInfo>> SignIn { get; init; } =
        (profile, cancellationToken) => AuthService.Create().WhoAmIAsync(profile, cancellationToken);

    /// <summary>Asks an endpoint for any HTTP answer.</summary>
    public Func<Uri, CancellationToken, Task<EndpointAnswer>> Probe { get; init; } = Network.ProbeAsync;

    /// <summary>The real machine, over the given home folder and profile.</summary>
    public static DoctorEnvironment ForThisMachine(string home, string? profile) => new() { Home = home, Profile = profile };

    /// <summary>
    /// Why the readers of the real machine below are excluded from the coverage (task 070): each one asks this
    /// machine (dsregcmd, the registry, the .NET installation's folders, the network), which a test cannot set up and
    /// which answers differently on Windows and on CI's Linux runner; the checks over their answers are tested
    /// through the seams above.
    /// </summary>
    private const string ThisMachine = "reads the real machine, differently per operating system (docs/development.md, Coverage)";

    /// <summary>The WebView2 runtime's client id under the EdgeUpdate keys (Microsoft's distribution guide).</summary>
    private const string WebView2ClientKey = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    private static string? RunPwshScript(string pwshPath, string script) =>
        RunProcess(pwshPath, ["-NoProfile", "-NonInteractive", "-Command", script], TimeSpan.FromSeconds(30));

    [ExcludeFromCodeCoverage(Justification = ThisMachine)]
    private static string? RunDsregcmd() =>
        OperatingSystem.IsWindows()
            ? RunProcess(Path.Combine(Environment.SystemDirectory, "dsregcmd.exe"), ["/status"], TimeSpan.FromSeconds(15))
            : null;

    /// <summary>The home folder's access list through the Windows API, each entry by its SID; null off Windows or when it cannot be read.</summary>
    [ExcludeFromCodeCoverage(Justification = ThisMachine)]
    private static HomeAccess? ReadHomeAccessFromDisk(string home)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var entries = new List<AccessEntry>();
            foreach (FileSystemAccessRule rule in new DirectoryInfo(home).GetAccessControl()
                .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)).OfType<FileSystemAccessRule>())
            {
                entries.Add(new AccessEntry(rule.IdentityReference.Value, rule.AccessControlType == AccessControlType.Allow, (int)rule.FileSystemRights, rule.IsInherited));
            }

            return new HomeAccess(WindowsIdentity.GetCurrent().Name, entries);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static byte[]? ReadInstalledSidecarScript() => ReadIfExists(SidecarScript.InstalledPath);

    /// <summary>The bytes of a file, or null when it is missing.</summary>
    internal static byte[]? ReadIfExists(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

    /// <summary>
    /// The folder names under <c>shared\Microsoft.WindowsDesktop.App</c> of the .NET installation whose
    /// <c>Microsoft.NETCore.App</c> runs this process: the same installation the window's apphost resolves.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = ThisMachine)]
    private static IReadOnlyList<string> ReadDesktopRuntimeVersionsFromDisk()
    {
        try
        {
            // <root>\shared\Microsoft.NETCore.App\<version>\: three levels up is the installation's root.
            DirectoryInfo? root = new DirectoryInfo(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()).Parent?.Parent?.Parent;
            string? desktop = root is null ? null : Path.Combine(root.FullName, "shared", "Microsoft.WindowsDesktop.App");
            return desktop is not null && Directory.Exists(desktop)
                ? [.. Directory.GetDirectories(desktop).Select(Path.GetFileName).OfType<string>()]
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }
    }

    [ExcludeFromCodeCoverage(Justification = ThisMachine)]
    private static IReadOnlyList<string?> ReadWebView2VersionsFromRegistry()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        // Per machine: the 32-bit view, which is WOW6432Node on 64-bit Windows and the only view on 32-bit Windows.
        return [ReadPv(RegistryHive.LocalMachine, RegistryView.Registry32), ReadPv(RegistryHive.CurrentUser, RegistryView.Default)];
    }

    [SupportedOSPlatform("windows")]
    [ExcludeFromCodeCoverage(Justification = ThisMachine)]
    private static string? ReadPv(RegistryHive hive, RegistryView view)
    {
        try
        {
            using RegistryKey root = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? key = root.OpenSubKey(@"SOFTWARE\" + WebView2ClientKey);
            return key?.GetValue("pv") as string;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>Runs a process and returns its standard output, or null when it did not start, failed or timed out.</summary>
    internal static string? RunProcess(string fileName, IEnumerable<string> arguments, TimeSpan timeout)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            // Null only for a shell-executed start that reused a process, which UseShellExecute = false rules out.
            using Process process = Process.Start(startInfo)!;

            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            _ = error.GetAwaiter().GetResult();
            return output.GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>The endpoint probe and its client: a real HTTP request to Microsoft's endpoints.</summary>
    [ExcludeFromCodeCoverage(Justification = ThisMachine)]
    private static class Network
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

        internal static async Task<EndpointAnswer> ProbeAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, endpoint);
                using HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                return new EndpointAnswer((int)response.StatusCode, stopwatch.Elapsed, null);
            }
            catch (HttpRequestException exception)
            {
                return new EndpointAnswer(null, stopwatch.Elapsed, exception.Message);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new EndpointAnswer(null, stopwatch.Elapsed, $"no answer within {Http.Timeout.TotalSeconds:0} s");
            }
        }
    }
}
