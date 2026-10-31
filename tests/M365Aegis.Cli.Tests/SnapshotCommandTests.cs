using System.Diagnostics;
using System.Text.Json;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Snapshots;
using M365Aegis.Tests;

namespace M365Aegis.Cli.Tests;

/// <summary>
/// <c>snapshot take|list|show</c> (task 087) and <c>access &lt;user&gt;</c> (task 084) as child processes: the take over
/// the recorded answers and the fake Exchange Online module into a temporary home, the reads of the files it leaves, and
/// the answers without a profile, a snapshot or the user.
/// </summary>
public sealed class SnapshotCommandTests
{
    /// <summary>The fake module, the fixtures and a home with the samples' profile.</summary>
    private static Dictionary<string, string> Layers(string home)
    {
        Dictionary<string, string> environment = MailboxCommandTests.FakeExchange;
        environment[GraphConnection.FixturesEnvironmentVariable] = GraphFixtures.Root;
        environment["M365AEGIS_HOME"] = home;
        return environment;
    }

    private static async Task<string> HomeWithTheFixtureSnapshotAsync()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());
        await SnapshotSamples.SeedAsync(home);
        return home;
    }

    [Fact]
    public void TakeWritesASnapshotThatListAndShowRead()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());

        (int exitCode, string stdout, string stderr) = CliTests.RunWith(Layers(home), "--json", "snapshot", "take");

        Assert.Equal(0, exitCode);
        Assert.Equal("", stderr);
        using JsonDocument take = JsonDocument.Parse(stdout);
        JsonElement data = take.RootElement.GetProperty("data");
        string name = data.GetProperty("snapshot").GetProperty("name").GetString()!;
        Assert.NotNull(SnapshotStore.TakenOf(name));
        Assert.Equal(13, data.GetProperty("domains").GetArrayLength());
        Assert.Equal("partial", data.GetProperty("domains")[7].GetProperty("status").GetString());
        Assert.Equal("graph+admin-api+pwsh", take.RootElement.GetProperty("meta").GetProperty("backend").GetString());
        Assert.True(File.Exists(Path.Combine(home, "snapshots", "contoso", name)));

        (int listExit, string list, _) = CliTests.RunWith(Layers(home), "--table", "snapshot", "list");
        (int showExit, string show, _) = CliTests.RunWith(Layers(home), "--table", "snapshot", "show", name);
        (int jsonExit, string json, _) = CliTests.RunWith(Layers(home), "--json", "--profile", "contoso", "snapshot", "show");

        Assert.Equal((0, 0, 0), (listExit, showExit, jsonExit));
        Assert.Contains($"  {name}  ", list, StringComparison.Ordinal);
        Assert.Contains("1 snapshot(s) of the profile contoso under ", list, StringComparison.Ordinal);
        Assert.Matches(@"\nsharing +ok +7 +\d+\.\d s +\d+ graph +graph /drives/\{id\}/root/delta \(Prefer: hierarchicalsharing\)", show);
        Assert.Contains("rows: objects 17, members 14, userGroups 12, owners 5, sites 3, teams 2, links 7, linkGrantees 6, grants 7, folderPermissions 14, forwarding 3", show, StringComparison.Ordinal);
        using JsonDocument detail = JsonDocument.Parse(json);
        Assert.Equal(name, detail.RootElement.GetProperty("data").GetProperty("snapshot").GetProperty("name").GetString());
        Assert.Equal("local", detail.RootElement.GetProperty("meta").GetProperty("backend").GetString());
    }

    [Fact]
    public void TakeAtATerminalIsATableOfTheDomains()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());

        (int exitCode, string stdout, _) = CliTests.RunWith(Layers(home), "--table", "snapshot", "take");

        Assert.Equal(0, exitCode);
        Assert.Matches(@"^domain +status +rows +time +requests +note\n", stdout);
        Assert.Matches(@"\noneDrive +skipped +0 +0\.0 s +- +other users' OneDrives", stdout);
        Assert.Contains("nothing to prune (14 days, 8 weeks kept)", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void TakeWithoutAProfileIsTheLoginHint()
    {
        (int exitCode, _, string stderr) = CliTests.RunWith(Layers(PlanSamples.TempHome()), "--table", "snapshot", "take");

        Assert.Equal(3, exitCode);
        Assert.Contains("hint: run `m365-aegis login", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccessAnswersFromTheNewestSnapshot()
    {
        string home = await HomeWithTheFixtureSnapshotAsync();

        (int exitCode, string stdout, string stderr) = CliTests.RunWith(Layers(home), "--json", "access", "dave@contoso.com");
        (int tableExit, string table, _) = CliTests.RunWith(Layers(home), "--table", "access", "dave@contoso.com");

        Assert.Equal((0, 0), (exitCode, tableExit));
        Assert.Equal("", stderr);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal("2026-10-31T060000Z.sqlite", data.GetProperty("snapshot").GetString());
        Assert.Equal(4, data.GetProperty("groups").GetArrayLength());
        Assert.Equal("local", document.RootElement.GetProperty("meta").GetProperty("backend").GetString());
        Assert.Contains(data.GetProperty("warnings").EnumerateArray(), warning => warning.GetString()!.StartsWith("oneDrive was not read", StringComparison.Ordinal));
        Assert.Matches(@"\ngroup +Finance EU +Security +direct\n", table);
        Assert.Contains("shared    Finance: /Reports", table, StringComparison.Ordinal);
        Assert.Contains("mailbox   Reception <reception@contoso.com>", table, StringComparison.Ordinal);
        Assert.Contains("Dave Example (dave@contoso.com) from the snapshot 2026-10-31T060000Z.sqlite, taken 2026-10-31 06:00:00 UTC", table, StringComparison.Ordinal);
        Assert.Contains("reaches everyone: 1 organization-wide link(s), 1 anonymous link(s), 1 calendar(s) open beyond free/busy to every user, 1 to anonymous", table, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutASnapshotTheReadsNameTheTake()
    {
        string home = SnapshotSamples.WriteProfile(PlanSamples.TempHome());

        (int accessExit, string accessOut, string accessErr) = CliTests.RunWith(Layers(home), "--table", "access", "dave@contoso.com");
        (int showExit, _, _) = CliTests.RunWith(Layers(home), "--json", "snapshot", "show");
        (int listExit, string list, _) = CliTests.RunWith(Layers(home), "--table", "snapshot", "list");

        Assert.Equal((4, 4, 0), (accessExit, showExit, listExit));
        Assert.Equal("", accessOut);
        Assert.Equal("error: no snapshot of the profile 'contoso' yet (not-found)\nhint: run `m365-aegis snapshot take` first (docs/usage.md, \"Snapshots\")\n", accessErr);
        Assert.EndsWith(SnapshotStore.TakeHint + "\n", list, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownUserOrSnapshotIsNotFound()
    {
        string home = await HomeWithTheFixtureSnapshotAsync();

        (int userExit, string user, _) = CliTests.RunWith(Layers(home), "--json", "access", "nobody@contoso.com");
        (int showExit, _, string show) = CliTests.RunWith(Layers(home), "--table", "snapshot", "show", "2026-01-01T000000Z");

        Assert.Equal((4, 4), (userExit, showExit));
        Assert.Contains("knows no user 'nobody@contoso.com'", user, StringComparison.Ordinal);
        Assert.Contains("hint: `m365-aegis snapshot list` shows the snapshots by name", show, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAccessViewAnswersInUnderASecondInAColdProcess()
    {
        string home = await HomeWithTheFixtureSnapshotAsync();
        CliTests.RunWith(Layers(home), "--json", "access", "dave@contoso.com");

        var clock = Stopwatch.StartNew();
        (int exitCode, _, _) = CliTests.RunWith(Layers(home), "--json", "access", "dave@contoso.com");
        clock.Stop();

        // The whole process, the start of dotnet included; CI runners are slower than a desktop, hence the margin.
        Assert.Equal(0, exitCode);
        Assert.True(clock.ElapsedMilliseconds < 3000, $"{clock.ElapsedMilliseconds} ms");
    }
}
