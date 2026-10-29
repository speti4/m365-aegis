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
    public void WhoAmIWithoutAProfileIsAnAuthEnvelopeWithTheLoginCommand()
    {
        // The tests' home has no config.json; the failure comes before MSAL is touched.
        (int exitCode, string stdout, string stderr) = Run("--json", "whoami");

        Assert.Equal(3, exitCode);
        Assert.Equal("", stderr);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement error = document.RootElement.GetProperty("error");
        Assert.Equal("auth", error.GetProperty("code").GetString());
        Assert.Contains("login --tenant", error.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void LoginWithOnlyATenantIsAUsageError()
    {
        (int exitCode, string stdout, _) = Run("--json", "login", "--tenant", "contoso.onmicrosoft.com");

        Assert.Equal(2, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("usage", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void LogoutWithoutAProfileIsAnErrorLineAtATerminal()
    {
        (int exitCode, string stdout, string stderr) = Run("--table", "logout");

        Assert.Equal(3, exitCode);
        Assert.Equal("", stdout);
        Assert.StartsWith("error: no tenant profile", stderr, StringComparison.Ordinal);
        Assert.Contains("(auth)", stderr, StringComparison.Ordinal);
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

    [Fact]
    public void AnInternalErrorInAPipeIsAnEnvelopeWithSeventyAndTheStackTraceGoesToTheLog()
    {
        string home = TempHome();

        (int exitCode, string stdout, string stderr) = RunWith(Fault(home), "profile", "list");

        Assert.Equal(70, exitCode);
        Assert.Equal("", stderr);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement error = document.RootElement.GetProperty("error");
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("internal", error.GetProperty("code").GetString());
        Assert.DoesNotContain(" at ", stdout, StringComparison.Ordinal);
        string log = Assert.Single(Directory.GetFiles(Path.Combine(home, "logs"), "m365-aegis-*.log"));
        Assert.Contains(log, error.GetProperty("hint").GetString(), StringComparison.Ordinal);
        string text = File.ReadAllText(log);
        Assert.Contains("internal-error: System.InvalidOperationException", text, StringComparison.Ordinal);
        Assert.Contains("CommandRunner", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInternalErrorAtATerminalIsTheErrorLine()
    {
        (int exitCode, string stdout, string stderr) = RunWith(Fault(TempHome()), "--table", "doctor", "--offline");

        Assert.Equal(70, exitCode);
        Assert.Equal("", stdout);
        Assert.StartsWith("error: an unexpected internal error stopped the command", stderr, StringComparison.Ordinal);
        Assert.Contains("(internal)", stderr, StringComparison.Ordinal);
        Assert.Contains("hint: the stack trace is in ", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunWithoutAnErrorWritesNoLog()
    {
        string home = TempHome();

        RunWith(Home(home), "--json", "doctor", "--offline");
        RunWith(Home(home), "--json", "profile", "list");

        Assert.False(Directory.Exists(Path.Combine(home, "logs")));
    }

    [Fact]
    public void ProfileListInAnEmptyHomeIsAnEmptyList()
    {
        (int exitCode, string stdout, _) = RunWith(Home(TempHome()), "--json", "profile", "list");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(0, data.GetProperty("profiles").GetArrayLength());
        Assert.False(data.TryGetProperty("default", out _));
    }

    [Fact]
    public void ProfileUseMovesTheDefaultAndListMarksIt()
    {
        string home = WithTwoProfiles();

        (int useExit, string useOut, _) = RunWith(Home(home), "--json", "profile", "use", "lab");
        (int listExit, string listOut, _) = RunWith(Home(home), "--table", "profile", "list");

        Assert.Equal(0, useExit);
        using JsonDocument document = JsonDocument.Parse(useOut);
        Assert.Equal("lab", document.RootElement.GetProperty("data").GetProperty("default").GetString());
        Assert.Equal(0, listExit);
        string[] lines = listOut.Split('\n');
        Assert.StartsWith("default  name", lines[0], StringComparison.Ordinal);
        Assert.Contains(lines, line => line.StartsWith("*        lab ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("         work ", StringComparison.Ordinal));
        Assert.Contains("\"default\": \"lab\"", File.ReadAllText(Path.Combine(home, "config.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void ProfileUseOfAnUnknownNameIsNotFound()
    {
        string home = WithTwoProfiles();

        (int exitCode, string stdout, _) = RunWith(Home(home), "--json", "profile", "use", "nope");

        Assert.Equal(4, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("not-found", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void ProfileRemoveOfAnUnknownNameIsNotFoundAtATerminal()
    {
        (int exitCode, string stdout, string stderr) = RunWith(Home(WithTwoProfiles()), "--table", "profile", "remove", "nope");

        Assert.Equal(4, exitCode);
        Assert.Equal("", stdout);
        Assert.StartsWith("error: no profile named 'nope'", stderr, StringComparison.Ordinal);
        Assert.Contains("hint: `m365-aegis profile list` shows the names", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void ProfileRemoveOfAProfileThatSharesItsAccountKeepsTheSignIn()
    {
        // The two profiles share the app registration and the account, so the removal never reaches the broker.
        string home = TempHome();
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.json"), """
            {"default":"work","profiles":{
              "work":{"tenant":"contoso.onmicrosoft.com","clientId":"00000000-0000-0000-0000-000000000010","account":"alice@contoso.com"},
              "alias":{"tenant":"contoso.com","clientId":"00000000-0000-0000-0000-000000000010","account":"alice@contoso.com"}}}
            """);

        (int exitCode, string stdout, _) = RunWith(Home(home), "--json", "profile", "remove", "alias");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.False(data.GetProperty("signedOut").GetBoolean());
        Assert.Equal("work", data.GetProperty("default").GetString());
        Assert.DoesNotContain("alias", File.ReadAllText(Path.Combine(home, "config.json")), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(home, "msal-cache.bin")));
    }

    [Fact]
    public void ProfileWithoutASubcommandIsAUsageError()
    {
        (int exitCode, string stdout, _) = Run("--json", "profile");

        Assert.Equal(2, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("usage", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void VerboseIsAnOptionOfEveryCommand()
    {
        (int exitCode, string stdout, _) = RunWith(Home(TempHome()), "profile", "list", "--verbose");

        Assert.Equal(0, exitCode);
        Assert.StartsWith("{\"ok\":true", stdout, StringComparison.Ordinal);
    }

    private static string TempHome() => Path.Combine(Path.GetTempPath(), "m365-aegis-tests", Guid.NewGuid().ToString("N"));

    private static Dictionary<string, string> Home(string home) => new() { ["M365AEGIS_HOME"] = home };

    private static Dictionary<string, string> Fault(string home) => new() { ["M365AEGIS_HOME"] = home, ["M365AEGIS_TEST_FAULT"] = "internal-error" };

    private static string WithTwoProfiles()
    {
        string home = TempHome();
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.json"), """
            {"default":"work","profiles":{
              "work":{"tenant":"contoso.onmicrosoft.com","clientId":"00000000-0000-0000-0000-000000000010","account":"alice@contoso.com"},
              "lab":{"tenant":"fabrikam.onmicrosoft.com","clientId":"00000000-0000-0000-0000-000000000011","account":"alice@fabrikam.com"}}}
            """);
        return home;
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
