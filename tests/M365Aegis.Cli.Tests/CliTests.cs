using System.Diagnostics;
using System.Text.Json;

namespace M365Aegis.Cli.Tests;

/// <summary>The command line as a child process, the way a shell or an agent runs it.</summary>
public sealed class CliTests
{
    private static readonly string Assembly = Path.Combine(AppContext.BaseDirectory, "m365-aegis.dll");

    [Fact]
    public void VersionPrintsTheProductVersion()
    {
        (int exitCode, string stdout, _) = Run("--version");

        Assert.Equal(0, exitCode);
        Assert.Equal(Core.ProductInfo.Version, stdout.Trim());
    }

    [Fact]
    public void AnUnknownCommandIsAUsageError()
    {
        (int exitCode, _, string stderr) = Run("frobnicate");

        Assert.Equal(2, exitCode);
        Assert.Contains("frobnicate", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void DoctorJsonIsAnEnvelopeWithTheChecks()
    {
        (int exitCode, string stdout, _) = Run("--json", "doctor", "--offline");

        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement root = document.RootElement;
        bool ok = root.GetProperty("ok").GetBoolean();
        Assert.Equal(ok ? 0 : 10, exitCode);
        Assert.Equal("local", root.GetProperty("meta").GetProperty("backend").GetString());
        JsonElement checks = root.GetProperty("data").GetProperty("checks");
        Assert.Contains(checks.EnumerateArray(), check => check.GetProperty("name").GetString() == "pwsh");
    }

    [Fact]
    public void DoctorWithoutOptionsInAPipeIsJson()
    {
        // The tests redirect stdout, so the default is the envelope (D-006).
        (_, string stdout, _) = Run("doctor", "--offline");

        Assert.StartsWith("{\"ok\":", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void DoctorTableIsATable()
    {
        (_, string stdout, _) = Run("--table", "doctor", "--offline");

        string[] lines = stdout.Split('\n');
        Assert.StartsWith("check", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("-----", lines[1], StringComparison.Ordinal);
        Assert.Contains(lines, line => line.StartsWith("pwsh", StringComparison.Ordinal));
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(Assembly);
        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // The doctor's home check must not touch the real %LOCALAPPDATA%.
        startInfo.Environment["M365AEGIS_HOME"] = Path.Combine(Path.GetTempPath(), "m365-aegis-tests", "cli");

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("dotnet did not start");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }
}
