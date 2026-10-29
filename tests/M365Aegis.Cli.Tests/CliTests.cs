using System.Diagnostics;
using System.Text.Json;

namespace M365Aegis.Cli.Tests;

/// <summary>The command line as a child process, the way a shell or an agent runs it.</summary>
public sealed class CliTests
{
    private static readonly string Assembly = Path.Combine(AppContext.BaseDirectory, "m365-aegis.dll");
    private static readonly string[] Statuses = ["pass", "warn", "fail", "skipped"];

    [Fact]
    public void VersionPrintsTheProductVersion()
    {
        (int exitCode, string stdout, _) = Run("--version");

        Assert.Equal(0, exitCode);
        Assert.Equal(Core.ProductInfo.Version, stdout.Trim());
    }

    [Fact]
    public void AnUnknownCommandInAPipeIsAUsageEnvelope()
    {
        (int exitCode, string stdout, string stderr) = Run("frobnicate");

        Assert.Equal(2, exitCode);
        Assert.Equal("", stderr);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement root = document.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("usage", root.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("frobnicate", root.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownCommandAtATerminalIsAnErrorLineOnStderr()
    {
        (int exitCode, string stdout, string stderr) = Run("--table", "frobnicate");

        Assert.Equal(2, exitCode);
        Assert.Equal("", stdout);
        Assert.StartsWith("error: ", stderr, StringComparison.Ordinal);
        Assert.Contains("frobnicate", stderr, StringComparison.Ordinal);
        Assert.Contains("(usage)", stderr, StringComparison.Ordinal);
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
        JsonElement pwsh = Assert.Single(checks.EnumerateArray(), check => check.GetProperty("name").GetString() == "pwsh");
        // The status is a word of the contract, never a number.
        Assert.Contains(pwsh.GetProperty("status").GetString(), Statuses);
    }

    [Fact]
    public void DoctorWithoutPwshFailsWithTenAndKeepsTheReport()
    {
        // An empty PATH hides pwsh from the doctor; the dotnet host is resolved by this process, not by the child.
        (int exitCode, string stdout, _) = RunWith(new() { ["PATH"] = "" }, "--json", "doctor", "--offline");

        Assert.Equal(10, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement root = document.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("doctor-failed", root.GetProperty("error").GetProperty("code").GetString());
        JsonElement checks = root.GetProperty("data").GetProperty("checks");
        JsonElement pwsh = Assert.Single(checks.EnumerateArray(), check => check.GetProperty("name").GetString() == "pwsh");
        Assert.Equal("fail", pwsh.GetProperty("status").GetString());
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
        Assert.Contains(lines, line => line.StartsWith("home: ", StringComparison.Ordinal));
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args) => RunWith([], args);

    private static (int ExitCode, string Stdout, string Stderr) RunWith(Dictionary<string, string> environment, params string[] args)
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
        foreach ((string name, string value) in environment)
        {
            startInfo.Environment[name] = value;
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("dotnet did not start");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }
}
