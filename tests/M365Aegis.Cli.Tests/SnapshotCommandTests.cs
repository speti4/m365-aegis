using System.Diagnostics;
using System.Text.Json;
using M365Aegis.Cli.Commands;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;
using M365Aegis.Core.Snapshots;
using M365Aegis.Tests;

namespace M365Aegis.Cli.Tests;

/// <summary>
/// <c>snapshot take|list|show</c> (task 087), <c>snapshot diff</c> (task 098) and <c>access &lt;user&gt;</c> (task 084) as child processes: the take over
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

    private static async Task<string> HomeWithTwoSnapshotsAsync()
    {
        string home = await HomeWithTheFixtureSnapshotAsync();
        await SnapshotSamples.SeedChangedAsync(home);
        return home;
    }

    [Fact]
    public async Task DiffListsWhatAppearedAndDisappearedBetweenTwoSnapshots()
    {
        string home = await HomeWithTwoSnapshotsAsync();

        (int exitCode, string stdout, string stderr) = CliTests.RunWith(Layers(home), "--json", "snapshot", "diff", "2026-10-31");
        (int tableExit, string table, _) = CliTests.RunWith(Layers(home), "--table", "snapshot", "diff", "2026-10-31T060000Z", "latest");

        Assert.Equal((0, 0), (exitCode, tableExit));
        Assert.Equal("", stderr);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal("2026-10-31T060000Z.sqlite", data.GetProperty("from").GetProperty("snapshot").GetString());
        Assert.Equal("2026-11-01T060000Z.sqlite", data.GetProperty("to").GetProperty("snapshot").GetString());
        Assert.Equal("latest", data.GetProperty("to").GetProperty("pick").GetString());
        Assert.Equal((8, 6), (data.GetProperty("added").GetInt32(), data.GetProperty("removed").GetInt32()));
        Assert.False(data.TryGetProperty("user", out _));
        Assert.Equal("local", document.RootElement.GetProperty("meta").GetProperty("backend").GetString());
        Assert.StartsWith("domain             change   what", table, StringComparison.Ordinal);
        Assert.Matches(@"\ngrants +removed +reception@contoso\.com +Finance Staff +full-access\n", table);
        Assert.Contains("\nfrom 2026-10-31T060000Z.sqlite (taken 2026-10-31 06:00:00 UTC, ", table, StringComparison.Ordinal);
        Assert.Contains("\n8 added, 6 removed: members +3 -1, owners +1 -1, sharing +1 -1, grants +1 -1, folderPermissions +1 -1, forwarding +1 -1\n", table, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiffForOneUserListsTheNestedMembershipAndWhatComesThroughIt()
    {
        string home = await HomeWithTwoSnapshotsAsync();

        (int exitCode, string stdout, string stderr) = CliTests.RunWith(Layers(home), "--json", "snapshot", "diff", "2026-10-31", "--user", "bob@contoso.com");
        (int tableExit, string table, _) = CliTests.RunWith(Layers(home), "--table", "snapshot", "diff", "2026-10-31T060000Z", "latest", "--user", "00000000-0000-0000-0000-000000000003");

        Assert.Equal((0, 0), (exitCode, tableExit));
        Assert.Equal("", stderr);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal("bob@contoso.com", data.GetProperty("user").GetProperty("userPrincipalName").GetString());
        Assert.Equal((7, 1), (data.GetProperty("added").GetInt32(), data.GetProperty("removed").GetInt32()));
        Assert.Equal("Audit", data.GetProperty("changes")[1].GetProperty("through").GetString());
        Assert.StartsWith("section", table, StringComparison.Ordinal);
        Assert.Matches(@"\ngroups +added +Finance Staff +bob@contoso\.com +member +Audit\n", table);
        Assert.Contains("\nfor bob@contoso.com (Bob Example), nested memberships and what comes through them included\n", table, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiffForAUserNeitherSnapshotKnowsIsNotFound()
    {
        string home = await HomeWithTwoSnapshotsAsync();

        (int unknownExit, _, string unknown) = CliTests.RunWith(Layers(home), "--table", "snapshot", "diff", "2026-10-31", "--user", "nobody@contoso.com");
        (int blankExit, _, string blank) = CliTests.RunWith(Layers(home), "--table", "snapshot", "diff", "2026-10-31", "--user", " ");

        Assert.Equal((4, 2), (unknownExit, blankExit));
        Assert.Contains("error: neither 2026-10-31T060000Z.sqlite nor 2026-11-01T060000Z.sqlite knows a user 'nobody@contoso.com' (not-found)", unknown, StringComparison.Ordinal);
        Assert.Contains("error: no user was named (usage)", blank, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheUserDiffTable()
    {
        string home = await HomeWithTwoSnapshotsAsync();
        SnapshotDiffReport diff = SnapshotDiff.Compare(home, "contoso", "yesterday", "latest", SnapshotSamples.ChangedTaken.AddHours(2), "bob@contoso.com");

        var output = new StringWriter();
        var error = new StringWriter();
        Renderer.Render(CommandResult.Success(diff), new EnvelopeMeta([], 3, Backend.Local), SnapshotDiffTableView.Instance, OutputMode.Table, output, error);

        await Verify($"--- stdout ---\n{output}--- stderr ---\n{error}");
    }

    [Fact]
    public async Task DiffOfASnapshotWithItselfSaysNoChange()
    {
        string home = await HomeWithTheFixtureSnapshotAsync();

        (int exitCode, string stdout, _) = CliTests.RunWith(Layers(home), "--table", "snapshot", "diff", "latest", "latest");

        Assert.Equal(0, exitCode);
        Assert.Contains("\nno change in members, owners, sharing, grants, folderPermissions, forwarding\n", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiffWithAPickThatFindsNothingOrIsNoneEndsWithItsExitCode()
    {
        string home = await HomeWithTheFixtureSnapshotAsync();

        (int earlyExit, _, string early) = CliTests.RunWith(Layers(home), "--table", "snapshot", "diff", "2026-01-01");
        (int wordExit, _, string word) = CliTests.RunWith(Layers(home), "--table", "snapshot", "diff", "last-week");
        (int noneExit, _, _) = CliTests.RunWith(Layers(SnapshotSamples.WriteProfile(PlanSamples.TempHome())), "--json", "snapshot", "diff", "latest");

        Assert.Equal((4, 2, 4), (earlyExit, wordExit, noneExit));
        Assert.Contains("error: no snapshot of the profile 'contoso' taken on 2026-01-01 (UTC) or before (not-found)", early, StringComparison.Ordinal);
        Assert.Contains("error: 'last-week' names no snapshot (usage)", word, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDiffTable()
    {
        string home = await HomeWithTwoSnapshotsAsync();
        SnapshotDiffReport diff = SnapshotDiff.Compare(home, "contoso", "yesterday", "latest", SnapshotSamples.ChangedTaken.AddHours(2));

        var output = new StringWriter();
        var error = new StringWriter();
        Renderer.Render(CommandResult.Success(diff), new EnvelopeMeta([], 12, Backend.Local), SnapshotDiffTableView.Instance, OutputMode.Table, output, error);

        await Verify($"--- stdout ---\n{output}--- stderr ---\n{error}");
    }

    [Fact]
    public async Task TheDiffAnswersInUnderASecondInAColdProcess()
    {
        string home = await HomeWithTwoSnapshotsAsync();
        CliTests.RunWith(Layers(home), "--json", "snapshot", "diff", "2026-10-31");

        var clock = Stopwatch.StartNew();
        (int exitCode, _, _) = CliTests.RunWith(Layers(home), "--json", "snapshot", "diff", "2026-10-31");
        clock.Stop();

        // The whole process, the start of dotnet included; CI runners are slower than a desktop, hence the margin.
        Assert.Equal(0, exitCode);
        Assert.True(clock.ElapsedMilliseconds < 3000, $"{clock.ElapsedMilliseconds} ms");
    }
}
