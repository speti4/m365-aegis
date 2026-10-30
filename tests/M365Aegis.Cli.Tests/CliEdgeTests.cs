using System.Text.Json;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Operations;
using M365Aegis.Tests;

namespace M365Aegis.Cli.Tests;

/// <summary>
/// The command line's edges the end-to-end tests do not reach (task 070): the output mode at a terminal, the device
/// code line, a CSV file that cannot be read for its record, a temporary folder that cannot be removed, and the
/// resumes refused for their profile or for arguments that no longer parse.
/// </summary>
public sealed class CliEdgeTests
{
    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, true, true)]
    [InlineData(false, false, false, false)]
    public void TheOutputModeIsTheOptionElseTheConsole(bool json, bool table, bool redirected, bool envelope) =>
        Assert.Equal(envelope ? OutputMode.Json : OutputMode.Table, Program.PickMode(json, table, redirected));

    [Fact]
    public async Task TheDeviceCodeSentenceIsOneLine()
    {
        var writer = new StringWriter();

        await DeviceCodePrompts.Write(writer, new DeviceCodePrompt(new Uri("https://login.microsoftonline.com/common/oauth2/deviceauth"), "ABCD1234", "To sign in, enter the code ABCD1234.", DateTimeOffset.UnixEpoch));

        Assert.Equal("To sign in, enter the code ABCD1234.\n", writer.ToString());
    }

    [Fact]
    public void ACsvFileIsKeptWithItsFullPathOrNotAtAll()
    {
        string folder = Directory.CreateTempSubdirectory("m365-aegis-tests-").FullName;
        string file = Path.Combine(folder, "rows.csv");
        File.WriteAllText(file, "group,member\n");

        PlanRecordInput? input = WriteRunner.ReadInput(file);

        Assert.Equal(file, input?.File);
        Assert.Equal("group,member\n", input?.Content);
        Assert.Null(WriteRunner.ReadInput(Path.Combine(folder, "missing.csv")));
    }

    [Fact]
    public void ATemporaryFolderIsRemovedAndAMissingOneIsNoError()
    {
        string folder = Directory.CreateTempSubdirectory("m365-aegis-tests-").FullName;
        File.WriteAllText(Path.Combine(folder, "rows.csv"), "");

        ResumeAction.DeleteQuietly(folder);
        ResumeAction.DeleteQuietly(folder);

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void AResumeOfARecordWrittenUnderAProfileWantsThatProfile()
    {
        string home = HomeWithRecord("work", ["group", "members", "add", "Finance", "bob@contoso.com", "--yes"]);

        (int exitCode, string stdout, _) = CliTests.RunWith(Graph(home), "--json", "group", "members", "add", "--resume", Name, "--dry-run");

        Assert.Equal(2, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement error = document.RootElement.GetProperty("error");
        Assert.Contains("with the profile work, not (the default one)", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal("run the resume with --profile work", error.GetProperty("hint").GetString());
    }

    [Fact]
    public void AResumeOfArgumentsThatNoLongerParseIsRefusedAtATerminalToo()
    {
        string home = HomeWithRecord(null, ["group", "owners", "add", "Finance", "bob@contoso.com"]);

        (int exitCode, string stdout, string stderr) = CliTests.RunWith(Graph(home), "--table", "group", "members", "add", "--resume", Name, "--dry-run");

        Assert.Equal(2, exitCode);
        Assert.Equal("", stdout);
        Assert.Contains("do not make the same command again", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void AResumeOfARecordWhoseFileOptionLostItsValueIsRefused()
    {
        // The record keeps a file's text, but its arguments end at --from-csv: there is no place for the copy.
        string home = HomeWithRecord(null, ["group", "members", "add", "--from-csv"], new PlanRecordInput("rows.csv", "group,member\n"));

        (int exitCode, string stdout, _) = CliTests.RunWith(Graph(home), "group", "members", "add", "--resume", Name);

        Assert.Equal(2, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Contains("do not make the same command again", document.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AResumeOfARecordThatKeptAFileItsArgumentsNoLongerNameResumesTheArguments()
    {
        string home = HomeWithRecord(null, ["group", "members", "add", "Finance", "bob@contoso.com", "--yes"], new PlanRecordInput("rows.csv", "group,member"));

        (int exitCode, string stdout, _) = CliTests.RunWith(Graph(home), "--json", "group", "members", "add", "--resume", Name, "--dry-run");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("up-to-date", document.RootElement.GetProperty("data").GetProperty("mode").GetString());
    }

    private const string Name = "20261030T120000.000Z-group-members-add";

    private static Dictionary<string, string> Graph(string home) => new()
    {
        [GraphConnection.FixturesEnvironmentVariable] = GraphFixtures.Root,
        ["M365AEGIS_HOME"] = home,
    };

    /// <summary>A home with one stopped member write, written under <paramref name="profile"/> with <paramref name="arguments"/>.</summary>
    private static string HomeWithRecord(string? profile, IReadOnlyList<string> arguments, PlanRecordInput? input = null)
    {
        string home = PlanSamples.TempHome();
        var started = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        var change = new PlanChange(PlanAction.Add, "Finance", "Bob Example (bob@contoso.com) as a member", "not a member", "a member");
        var plan = new Plan("group-members-add", "Add members to Finance", [change], [], []);
        var store = new PlanStore(home, new PlanRecordContext("command line", profile, arguments) { Input = input });
        string path = store.Begin(plan with { Mode = PlanMode.Applying, Started = started });
        store.Finish(path, plan with
        {
            Mode = PlanMode.Applied,
            Started = started,
            Finished = started.AddSeconds(1),
            Record = path,
            Changes = [change with { Result = new ChangeResult(ChangeStatus.Skipped) }],
        });
        Assert.Equal(Name + ".json", Path.GetFileName(path));
        return home;
    }
}
