using System.Diagnostics;
using System.Runtime.Versioning;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Exchange;
using Microsoft.Win32;

namespace M365Aegis.Core.Diagnostics;

/// <summary>An endpoint's answer to the doctor's probe: an HTTP status (any status means reachable), or the error.</summary>
/// <param name="StatusCode">The HTTP status, or null when nothing answered.</param>
/// <param name="Elapsed">How long the answer took.</param>
/// <param name="Error">Why nothing answered, or null.</param>
public sealed record EndpointAnswer(int? StatusCode, TimeSpan Elapsed, string? Error);

/// <summary>
/// What the doctor reads from the machine, each behind a seam the tests replace (task 008): the pwsh call, the
/// registry, <c>dsregcmd</c>, the sidecar script's two copies, the sign-in and the endpoint probe.
/// <see cref="ForThisMachine"/> wires the real ones.
/// </summary>
public sealed record DoctorEnvironment
{
    /// <summary>The home folder the checks look at.</summary>
    public required string Home { get; init; }

    /// <summary>The profile the sign-in check uses; null for the default.</summary>
    public string? Profile { get; init; }

    /// <summary>Whether this is Windows; the registry and <c>dsregcmd</c> checks are skipped elsewhere.</summary>
    public bool IsWindows { get; init; } = OperatingSystem.IsWindows();

    /// <summary>The .NET runtime the process runs on.</summary>
    public string RuntimeVersion { get; init; } = Environment.Version.ToString();

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
    public Func<Uri, CancellationToken, Task<EndpointAnswer>> Probe { get; init; } = ProbeAsync;

    /// <summary>The real machine, over the given home folder and profile.</summary>
    public static DoctorEnvironment ForThisMachine(string home, string? profile) => new() { Home = home, Profile = profile };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>The WebView2 runtime's client id under the EdgeUpdate keys (Microsoft's distribution guide).</summary>
    private const string WebView2ClientKey = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    private static string? RunPwshScript(string pwshPath, string script) =>
        RunProcess(pwshPath, ["-NoProfile", "-NonInteractive", "-Command", script], TimeSpan.FromSeconds(30));

    private static string? RunDsregcmd() =>
        OperatingSystem.IsWindows()
            ? RunProcess(Path.Combine(Environment.SystemDirectory, "dsregcmd.exe"), ["/status"], TimeSpan.FromSeconds(15))
            : null;

    private static byte[]? ReadInstalledSidecarScript()
    {
        string path = SidecarScript.InstalledPath;
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

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

    private static async Task<EndpointAnswer> ProbeAsync(Uri endpoint, CancellationToken cancellationToken)
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

    /// <summary>Runs a process and returns its standard output, or null when it did not start, failed or timed out.</summary>
    private static string? RunProcess(string fileName, IEnumerable<string> arguments, TimeSpan timeout)
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

            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

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
}
