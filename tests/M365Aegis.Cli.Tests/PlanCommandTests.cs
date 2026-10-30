using System.Text.Json;
using System.Text.RegularExpressions;
using M365Aegis.Cli.Commands;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Cli.Tests;

/// <summary>
/// <c>plan list</c> and <c>plan show</c> (task 060) over the three records of <see cref="PlanRecordSamples"/> in a
/// temporary home, as child processes and as table snapshots; <c>--resume</c> (task 062) on the group writes over the
/// recorded Graph answers and on a mailbox grant over the fake module; and a distribution group's members through
/// Exchange (task 061).
/// </summary>
public sealed partial class PlanCommandTests
{
    private static Dictionary<string, string> Home(string home) => new() { ["M365AEGIS_HOME"] = home };

    private static Dictionary<string, string> Graph(string home) => new()
    {
        [GraphConnection.FixturesEnvironmentVariable] = GraphFixtures.Root,
        ["M365AEGIS_HOME"] = home,
    };

    /// <summary>Graph's recorded answers, the Admin API's among them, and the fake module for the sidecar.</summary>
    private static Dictionary<string, string> Layers(string home, string? writes = null)
    {
        Dictionary<string, string> environment = MailboxCommandTests.FakeExchange;
        environment[GraphConnection.FixturesEnvironmentVariable] = GraphFixtures.Root;
        environment["M365AEGIS_HOME"] = home;
        if (writes is not null)
        {
            environment["M365AEGIS_FAKE_EXO_WRITES"] = writes;
        }

        return environment;
    }

    private static string HomeWithThreeRecords()
    {
        string home = PlanSamples.TempHome();
        PlanRecordSamples.Write(home);
        return home;
    }

    private static string[] Records(string home) => Directory.GetFiles(Path.Combine(home, "plans"));

    /// <summary>The output with the temporary home as <c>&lt;home&gt;</c> and every path separator as <c>/</c>, the same on every system.</summary>
    private static string Scrub(string text, string home) =>
        text.Replace(home.Replace("\\", "\\\\", StringComparison.Ordinal), "<home>", StringComparison.Ordinal)
            .Replace(home, "<home>", StringComparison.Ordinal)
            .Replace("\\\\", "/", StringComparison.Ordinal)
            .Replace("\\", "/", StringComparison.Ordinal);

    [Fact]
    public void PlanListIsAnEnvelopeOfTheRecordsNewestFirstWithNoSignIn()
    {
        string home = HomeWithThreeRecords();

        (int exitCode, string stdout, string stderr) = CliTests.RunWith(Home(home), "--json", "plan", "list");

        Assert.Equal(0, exitCode);
        Assert.Equal("", stderr);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("local", document.RootElement.GetProperty("meta").GetProperty("backend").GetString());
        JsonElement records = document.RootElement.GetProperty("data").GetProperty("records");
        Assert.Equal([PlanRecordSamples.Stopped, PlanRecordSamples.Grants, PlanRecordSamples.Owners], records.EnumerateArray().Select(record => record.GetProperty("name").GetString()));
        Assert.Equal(2, records[0].GetProperty("failed").GetInt32());
        Assert.Equal(3, records[0].GetProperty("skipped").GetInt32());
    }

    [Fact]
    public void PlanListOfAnEmptyHomeIsAnEmptyList()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Home(PlanSamples.TempHome()), "--json", "plan", "list");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal(0, document.RootElement.GetProperty("data").GetProperty("records").GetArrayLength());
    }

    [Fact]
    public void PlanShowOfAMissingRecordIsExitFour()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Home(HomeWithThreeRecords()), "--json", "plan", "show", "20200101T000000.000Z-nothing");

        Assert.Equal(4, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("not-found", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("local", document.RootElement.GetProperty("meta").GetProperty("backend").GetString());
    }

    [Fact]
    public void PlanShowIsTheRecordAsItsFileHoldsIt()
    {
        string home = HomeWithThreeRecords();

        (int exitCode, string stdout, _) = CliTests.RunWith(Home(home), "--json", "plan", "show", PlanRecordSamples.Grants);

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal("mailbox-access-grant", data.GetProperty("plan").GetProperty("operation").GetString());
        Assert.Equal("failed", data.GetProperty("plan").GetProperty("changes")[1].GetProperty("result").GetProperty("status").GetString());
        Assert.Equal("mailbox-grants.csv", data.GetProperty("input").GetProperty("file").GetString());
    }

    [Fact]
    public async Task TheTablesOfPlanListAndPlanShow()
    {
        string home = HomeWithThreeRecords();
        string list = Render(PlanStore.List(home), PlanListTableView.Instance);
        string show = Render(PlanStore.Open(home, PlanRecordSamples.Stopped).Record, PlanRecordTableView.Instance);
        string grants = Render(PlanStore.Open(home, PlanRecordSamples.Grants).Record, PlanRecordTableView.Instance);

        // The records hold the temporary home's path, whose length differs from one machine to another, and so does their size.
        string sized = SizeOnDisk().Replace(list, "<size> on disk");
        await Verify(Scrub($"=== plan list ===\n{sized}=== plan show (stopped) ===\n{show}=== plan show (a batch) ===\n{grants}", home));
    }

    [Fact]
    public void AResumeDryRunPlansTheFailedAndSkippedChangesOnly()
    {
        string home = HomeWithThreeRecords();

        (int exitCode, string stdout, _) = CliTests.RunWith(Graph(home), "--json", "group", "members", "add", "--resume", PlanRecordSamples.Stopped, "--dry-run");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal("dry-run", data.GetProperty("mode").GetString());
        Assert.Equal(2, data.GetProperty("changes").GetArrayLength());
        Assert.Equal(3, data.GetProperty("unchanged").GetArrayLength());
        Assert.EndsWith(PlanRecordSamples.Stopped, data.GetProperty("resumes").GetString(), StringComparison.Ordinal);
        Assert.Equal(3, Records(home).Length);
    }

    [Fact]
    public void AResumeAppliedWritesANewRecordThatLinksTheOld()
    {
        string home = HomeWithThreeRecords();

        (int exitCode, string stdout, _) = CliTests.RunWith(Graph(home), "--json", "group", "members", "add", "--resume", PlanRecordSamples.Stopped, "--yes");

        // Dave is added, Eve refused (403), as their recorded answers say.
        Assert.Equal(6, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        string record = document.RootElement.GetProperty("data").GetProperty("record").GetString()!;
        Assert.Equal(4, Records(home).Length);
        PlanRecord written = PlanStore.Read(record)!;
        Assert.Equal(Path.Combine(home, "plans", PlanRecordSamples.Stopped), written.Plan.Resumes);
        // The original command line, its flags replaced by this run's.
        Assert.Equal([.. PlanRecordSamples.StoppedArguments[1..11], "--json", "--yes"], written.Arguments);
        Assert.Equal(["applied", "failed"], written.Plan.Changes.Select(change => change.Result!.Status.ToString().ToLowerInvariant()));
    }

    [Fact]
    public void AResumeOfABatchReplaysTheFileTheRecordKeeps()
    {
        string home = HomeWithThreeRecords();

        (int exitCode, string stdout, _) = CliTests.RunWith(Layers(home), "--json", "mailbox", "access", "grant", "--resume", PlanRecordSamples.Grants, "--dry-run");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement change = Assert.Single(document.RootElement.GetProperty("data").GetProperty("changes").EnumerateArray());
        Assert.Equal(3, change.GetProperty("row").GetInt32());
        Assert.Equal("Send As for Dave Private (dave@example.com)", change.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData(2, "group", "members", "add", "Finance", "dave@contoso.com", "--resume", PlanRecordSamples.Stopped, "--dry-run")]
    [InlineData(2, "group", "members", "add", "--from-csv", "group-members-add.csv", "--resume", PlanRecordSamples.Stopped, "--dry-run")]
    [InlineData(2, "group", "owners", "add", "--resume", PlanRecordSamples.Stopped, "--dry-run")]
    [InlineData(2, "group", "owners", "add", "--resume", PlanRecordSamples.Owners, "--dry-run")]
    [InlineData(2, "group", "members", "add", "--resume", PlanRecordSamples.Stopped, "--dry-run", "--yes")]
    [InlineData(2, "group", "members", "add", "--resume", PlanRecordSamples.Stopped, "--profile", "other", "--dry-run")]
    [InlineData(2, "call", "DELETE", "/groups/00000000-0000-0000-0000-000000000031", "--resume", PlanRecordSamples.Stopped)]
    [InlineData(4, "group", "members", "add", "--resume", "20200101T000000.000Z-nothing", "--dry-run")]
    public void AResumeIsRefusedBeforeAnythingStarts(int expected, params string[] arguments)
    {
        string home = HomeWithThreeRecords();

        (int exitCode, string stdout, _) = CliTests.RunWith(Graph(home), ["--json", .. arguments]);

        Assert.Equal(expected, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("local", document.RootElement.GetProperty("meta").GetProperty("backend").GetString());
        Assert.Equal(3, Records(home).Length);
    }

    [Fact]
    public void ADistributionGroupsMembersArePlannedAndAppliedThroughExchange()
    {
        string home = PlanSamples.TempHome();
        string writes = Path.Combine(Path.GetTempPath(), $"m365-aegis-tests-{Guid.NewGuid():N}.jsonl");

        (int dryExit, string dryOut, _) = CliTests.RunWith(Layers(home, writes), "--json", "group", "members", "add", "Sales", "dave@contoso.com", "bob@contoso.com", "--dry-run");
        (int exitCode, string stdout, _) = CliTests.RunWith(Layers(home, writes), "--json", "group", "members", "add", "Sales", "dave@contoso.com", "--yes");

        Assert.Equal(0, dryExit);
        using JsonDocument dry = JsonDocument.Parse(dryOut);
        Assert.Equal(1, dry.RootElement.GetProperty("data").GetProperty("changes").GetArrayLength());
        Assert.Equal(0, dry.RootElement.GetProperty("data").GetProperty("warnings").GetArrayLength());
        Assert.Equal("graph+admin-api", dry.RootElement.GetProperty("meta").GetProperty("backend").GetString());
        Assert.Equal(0, exitCode);
        using JsonDocument applied = JsonDocument.Parse(stdout);
        Assert.Equal("graph+admin-api+pwsh", applied.RootElement.GetProperty("meta").GetProperty("backend").GetString());
        using JsonDocument write = JsonDocument.Parse(Assert.Single(File.ReadAllLines(writes)));
        Assert.Equal("Add-DistributionGroupMember", write.RootElement.GetProperty("cmdlet").GetString());
        Assert.Equal("dave@contoso.com", write.RootElement.GetProperty("parameters").GetProperty("Member").GetString());
    }

    [Fact]
    public void ADistributionGroupsOwnersArePlannedAndAppliedThroughTheSidecar()
    {
        string home = PlanSamples.TempHome();
        string writes = Path.Combine(Path.GetTempPath(), $"m365-aegis-tests-{Guid.NewGuid():N}.jsonl");

        (int dryExit, string dryOut, _) = CliTests.RunWith(Layers(home, writes), "--json", "group", "owners", "add", "Sales", "dave@contoso.com", "alice@contoso.com", "--dry-run");
        (int tableExit, string table, _) = CliTests.RunWith(Layers(home, writes), "--table", "group", "owners", "add", "Sales", "dave@contoso.com", "--dry-run");
        (int exitCode, string stdout, _) = CliTests.RunWith(Layers(home, writes), "--json", "group", "owners", "add", "Sales", "dave@contoso.com", "--yes");

        Assert.Equal(0, dryExit);
        using JsonDocument dry = JsonDocument.Parse(dryOut);
        JsonElement plan = dry.RootElement.GetProperty("data");
        Assert.Equal("Dave Example (dave@contoso.com) as an owner", Assert.Single(plan.GetProperty("changes").EnumerateArray()).GetProperty("detail").GetString());
        Assert.Equal(1, plan.GetProperty("unchanged").GetArrayLength());
        Assert.Equal(0, plan.GetProperty("warnings").GetArrayLength());
        Assert.Equal("graph+pwsh", dry.RootElement.GetProperty("meta").GetProperty("backend").GetString());
        Assert.Equal(0, tableExit);
        Assert.Contains("Dave Example (dave@contoso.com) as an owner", table, StringComparison.Ordinal);
        Assert.Equal(0, exitCode);
        using JsonDocument applied = JsonDocument.Parse(stdout);
        Assert.Equal("graph+pwsh", applied.RootElement.GetProperty("meta").GetProperty("backend").GetString());
        using JsonDocument write = JsonDocument.Parse(Assert.Single(File.ReadAllLines(writes)));
        Assert.Equal("Set-DistributionGroup", write.RootElement.GetProperty("cmdlet").GetString());
        Assert.Equal("dave@contoso.com", write.RootElement.GetProperty("parameters").GetProperty("ManagedBy").GetProperty("Add")[0].GetString());
    }

    [Fact]
    public void AResumeOfAVersionTwoBatchPlansOnlyItsPendingLines()
    {
        string home = PlanSamples.TempHome();
        string record = PlanRecordSamples.WriteBatch(home);

        (int exitCode, string stdout, _) = CliTests.RunWith(Graph(home), "--json", "group", "members", "add", "--resume", record, "--dry-run");

        // The line of nobody, applied and deleted since, is not read again: only Dave's line is planned.
        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement change = Assert.Single(document.RootElement.GetProperty("data").GetProperty("changes").EnumerateArray());
        Assert.Equal(3, change.GetProperty("row").GetInt32());
        Assert.Equal(2, change.GetProperty("index").GetInt32());
        Assert.Equal("00000000-0000-0000-0000-000000000031/00000000-0000-0000-0000-000000000005", change.GetProperty("key").GetString());
    }

    [Fact]
    public void AResumeOfAVersionOneBatchPlansEveryLineAgain()
    {
        string home = PlanSamples.TempHome();
        string record = PlanRecordSamples.WriteBatch(home);
        PlanRecordSamples.AsVersionOne(Path.Combine(home, "plans", record));

        (int exitCode, string stdout, _) = CliTests.RunWith(Graph(home), "--json", "group", "members", "add", "--resume", record, "--dry-run");

        // As before task 065, the whole batch is planned, and the deleted user fails the plan.
        Assert.Equal(4, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Contains("nobody@contoso.com", document.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PlanListCountsTheRecordsAndTheirSize()
    {
        string home = HomeWithThreeRecords();

        (int exitCode, string stdout, _) = CliTests.RunWith(Home(home), "--json", "plan", "list");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(3, data.GetProperty("count").GetInt32());
        Assert.Equal(Records(home).Sum(file => new FileInfo(file).Length), data.GetProperty("bytes").GetInt64());
    }

    [Fact]
    public void PlanPruneIsAWriteOverTheLocalRecords()
    {
        string home = HomeWithThreeRecords();
        // The samples started on 2026-10-28, 29 and 30; the ones older than a day go, counted against today.
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-1);
        int older = new[] { new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 29, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 30, 11, 0, 0, TimeSpan.Zero) }
            .Count(started => started < cutoff);

        (int refusedExit, string refused, _) = CliTests.RunWith(Home(home), "--json", "plan", "prune", "--older-than", "1");
        (int dryExit, string dry, _) = CliTests.RunWith(Home(home), "--json", "plan", "prune", "--older-than", "1", "--dry-run");
        Assert.Equal(3, Records(home).Length);
        (int exitCode, string stdout, _) = CliTests.RunWith(Home(home), "--json", "plan", "prune", "--older-than", "1", "--yes");

        using JsonDocument dryRun = JsonDocument.Parse(dry);
        Assert.Equal("local", dryRun.RootElement.GetProperty("meta").GetProperty("backend").GetString());
        Assert.Equal("plan-prune", dryRun.RootElement.GetProperty("data").GetProperty("operation").GetString());
        Assert.Equal(older, dryRun.RootElement.GetProperty("data").GetProperty("changes").GetArrayLength());
        using JsonDocument applied = JsonDocument.Parse(stdout);
        if (older == 0)
        {
            Assert.Equal((0, 0, 0), (refusedExit, dryExit, exitCode));
            Assert.Equal("up-to-date", applied.RootElement.GetProperty("data").GetProperty("mode").GetString());
            Assert.Equal(3, Records(home).Length);
        }
        else
        {
            Assert.Equal((7, 0, 0), (refusedExit, dryExit, exitCode));
            using JsonDocument refusal = JsonDocument.Parse(refused);
            Assert.Equal("refused-without-yes", refusal.RootElement.GetProperty("error").GetProperty("code").GetString());
            // The older records went; the prune's own record joined the newer ones.
            Assert.Equal(3 - older + 1, Records(home).Length);
            Assert.Contains(Records(home), file => file.EndsWith("-plan-prune.json", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("plan", "prune", "--dry-run")]
    [InlineData("plan", "prune", "--older-than", "0", "--dry-run")]
    [InlineData("plan", "prune", "--older-than", "1", "--dry-run", "--yes")]
    [InlineData("plan", "prune", "--older-than", "1", "--resume", PlanRecordSamples.Stopped)]
    public void PlanPruneIsRefusedBeforeAnythingIsRemoved(params string[] arguments)
    {
        string home = HomeWithThreeRecords();

        (int exitCode, string stdout, _) = CliTests.RunWith(Home(home), ["--json", .. arguments]);

        Assert.Equal(2, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("usage", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(3, Records(home).Length);
    }

    [Fact]
    public void AResumeContinuesARecordTheWindowWrote()
    {
        // The window records the command line of the same plan with --yes (task 082); a record of it cut short while
        // applying is continued by the command line's --resume, which parses those arguments again.
        string home = PlanSamples.TempHome();
        string[] window = ["mailbox", "access", "grant", "finance@contoso.com", "--user", "bob@contoso.com", "--right", "FullAccess", "--no-automapping", "--yes"];
        (int planned, string plan, _) = CliTests.RunWith(Layers(home), ["--json", .. window[..^1], "--dry-run"]);
        Assert.Equal(0, planned);
        using JsonDocument dryRun = JsonDocument.Parse(plan);
        Plan read = JsonSerializer.Deserialize<Plan>(dryRun.RootElement.GetProperty("data").GetRawText(), EnvelopeJson.Options)!;
        var store = new PlanStore(home, new PlanRecordContext("window", null, window));
        string record = Path.GetFileName(store.Begin(read with { Mode = PlanMode.Applying, Started = DateTimeOffset.UtcNow }));

        (int exitCode, string stdout, _) = CliTests.RunWith(Layers(home), "--json", "mailbox", "access", "grant", "--resume", record, "--dry-run");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal("dry-run", data.GetProperty("mode").GetString());
        Assert.Equal("FullAccess for Bob Example (bob@contoso.com), without automapping", Assert.Single(data.GetProperty("changes").EnumerateArray()).GetProperty("detail").GetString());
        Assert.Single(Records(home));
    }

    private static string Render<T>(T data, ITableView<T> view)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Renderer.Render(CommandResult.Success(data), new EnvelopeMeta([], 3, Backend.Local), view, OutputMode.Table, output, error);
        return $"{output}{error}";
    }

    [GeneratedRegex(@"[0-9.]+ (B|KB|MB) on disk", RegexOptions.CultureInvariant)]
    private static partial Regex SizeOnDisk();
}
