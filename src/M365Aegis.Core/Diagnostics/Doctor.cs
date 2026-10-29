using System.Diagnostics;
using M365Aegis.Core.Config;
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

    /// <summary>Not checked in this run (offline, or the check is not built yet).</summary>
    Skipped,
}

/// <summary>One line of the doctor report.</summary>
/// <param name="Name">A short, stable name of the check.</param>
/// <param name="Status">The outcome.</param>
/// <param name="Detail">What was found, one line.</param>
/// <param name="Required">Whether a failure of this check fails the report (exit code 10).</param>
public sealed record DoctorCheck(string Name, CheckStatus Status, string Detail, bool Required);

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
            new AegisFailure(FailureKind.DoctorFailed, "a required check failed", "the checks marked FAIL name what is missing"),
            this);
}

/// <summary>
/// The prerequisites of the tool, checked one by one: the home folder, the .NET runtime, PowerShell 7.6 for the
/// Exchange Online sidecar, the ExchangeOnlineManagement module, the WebView2 runtime of the window, the tenant
/// profile and the sign-in. The scaffold runs the first three; the rest arrive with task 008.
/// </summary>
public static class Doctor
{
    /// <summary>The PowerShell version the ExchangeOnlineManagement module (3.10 and later) requires.</summary>
    public static readonly Version MinimumPwsh = new(7, 6, 0);

    /// <summary>Runs the checks that need no network.</summary>
    public static DoctorReport RunOffline() => RunOffline(AegisHome.Resolve(), FindPwsh());

    /// <summary>Runs the offline checks against a given home folder and pwsh path (the tests inject both).</summary>
    public static DoctorReport RunOffline(string home, string? pwshPath)
    {
        var checks = new List<DoctorCheck>
        {
            CheckHome(home),
            new("dotnet-runtime", CheckStatus.Pass, Environment.Version.ToString(), Required: false),
            CheckPwsh(pwshPath),
            new("exchange-online-module", CheckStatus.Skipped, "not checked yet (task 008)", Required: false),
            new("webview2-runtime", CheckStatus.Skipped, "not checked yet (task 008)", Required: false),
            CheckConfig(home),
            new("sign-in", CheckStatus.Skipped, "offline run", Required: false),
        };
        return new DoctorReport(home, checks);
    }

    /// <summary>The pwsh executable on PATH, or null.</summary>
    public static string? FindPwsh()
    {
        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
        {
            return null;
        }

        string fileName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
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

    private static DoctorCheck CheckHome(string home)
    {
        if (Directory.Exists(home))
        {
            return new("home", CheckStatus.Pass, home, Required: true);
        }

        return new("home", CheckStatus.Warn, $"{home} does not exist yet; the first sign-in creates it", Required: true);
    }

    private static DoctorCheck CheckConfig(string home)
    {
        string path = AegisHome.ConfigPath(home);
        return File.Exists(path)
            ? new("config", CheckStatus.Pass, path, Required: false)
            : new("config", CheckStatus.Warn, $"{path} not found; `m365-aegis login` writes it (task 002)", Required: false);
    }

    private static DoctorCheck CheckPwsh(string? pwshPath)
    {
        if (pwshPath is null)
        {
            return new("pwsh", CheckStatus.Fail, "pwsh not found on PATH; PowerShell 7.6 or later is needed for Exchange Online", Required: true);
        }

        Version? version = ReadPwshVersion(pwshPath);
        if (version is null)
        {
            return new("pwsh", CheckStatus.Warn, $"{pwshPath}: version unreadable", Required: true);
        }

        return version >= MinimumPwsh
            ? new("pwsh", CheckStatus.Pass, $"{version} at {pwshPath}", Required: true)
            : new("pwsh", CheckStatus.Fail, $"{version} at {pwshPath}; {MinimumPwsh} or later is needed", Required: true);
    }

    private static Version? ReadPwshVersion(string pwshPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo(pwshPath)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add("$PSVersionTable.PSVersion.ToString()");
            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(10_000);
            // A preview version reads "7.6.0-preview.4"; the numeric part is what the comparison needs.
            int dash = output.IndexOf('-', StringComparison.Ordinal);
            return Version.TryParse(dash < 0 ? output : output[..dash], out Version? version) ? version : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
