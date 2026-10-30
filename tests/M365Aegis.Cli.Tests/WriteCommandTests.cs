using System.Text.Json;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Cli.Tests;

/// <summary>
/// The writes behind a plan (tasks 051 to 054) as child processes: the group writes over the recorded answers, the
/// mailbox writes over the fake module (which records each write in the file <c>M365AEGIS_FAKE_EXO_WRITES</c> names),
/// a temporary home for the plan records; and the plan's table in process, byte for byte, in its three renderings.
/// </summary>
public sealed class WriteCommandTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string> Graph(string home) => new()
    {
        [GraphConnection.FixturesEnvironmentVariable] = GraphFixtures.Root,
        ["M365AEGIS_HOME"] = home,
    };

    private static Dictionary<string, string> Exchange(string home, string? writes = null)
    {
        Dictionary<string, string> environment = MailboxCommandTests.FakeExchange;
        environment["M365AEGIS_HOME"] = home;
        if (writes is not null)
        {
            environment["M365AEGIS_FAKE_EXO_WRITES"] = writes;
        }

        return environment;
    }

    private static string[] Records(string home) =>
        Directory.Exists(Path.Combine(home, "plans")) ? Directory.GetFiles(Path.Combine(home, "plans")) : [];

    [Fact]
    public void ADryRunPrintsThePlanWithExitZeroAndWritesNoRecord()
    {
        string home = PlanSamples.TempHome();

        (int exitCode, string stdout, string stderr) = CliTests.RunWith(Graph(home), "--json", "group", "members", "add", "Finance", "dave@contoso.com", "bob@contoso.com", "--dry-run");

        Assert.Equal(0, exitCode);
        Assert.Equal("", stderr);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal("dry-run", data.GetProperty("mode").GetString());
        Assert.Equal("group-members-add", data.GetProperty("operation").GetString());
        Assert.Equal(1, data.GetProperty("changes").GetArrayLength());
        Assert.Equal(1, data.GetProperty("unchanged").GetArrayLength());
        Assert.Equal("graph", document.RootElement.GetProperty("meta").GetProperty("backend").GetString());
        Assert.Empty(Records(home));
    }

    [Fact]
    public void WithoutAFlagThePlanIsPrintedAndTheExitIsSeven()
    {
        string home = PlanSamples.TempHome();

        (int exitCode, string stdout, _) = CliTests.RunWith(Graph(home), "--json", "group", "members", "add", "Finance", "dave@contoso.com");

        Assert.Equal(7, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("refused-without-yes", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("refused", document.RootElement.GetProperty("data").GetProperty("mode").GetString());
        Assert.Empty(Records(home));
    }

    [Fact]
    public void YesAppliesThePlanAndWritesTheRecord()
    {
        string home = PlanSamples.TempHome();

        (int exitCode, string stdout, _) = CliTests.RunWith(Graph(home), "--json", "group", "members", "add", "Finance", "dave@contoso.com", "--yes");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal("applied", data.GetProperty("mode").GetString());
        Assert.Equal("applied", data.GetProperty("changes")[0].GetProperty("result").GetProperty("status").GetString());
        string record = Assert.Single(Records(home));
        Assert.Equal(record, data.GetProperty("record").GetString());
        Assert.EndsWith("-group-members-add.json", record, StringComparison.Ordinal);
        PlanRecord read = PlanStore.Read(record)!;
        Assert.Equal("command line", read.Source);
        Assert.Contains("--yes", read.Arguments);
        Assert.Contains("00000000-0000-0000-0000-0000000000ec", document.RootElement.GetProperty("meta").GetProperty("requestIds").EnumerateArray().Select(id => id.GetString()));
    }

    [Fact]
    public void DryRunAndYesTogetherAreAUsageError()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Graph(PlanSamples.TempHome()), "--json", "group", "owners", "add", "Finance", "bob@contoso.com", "--dry-run", "--yes");

        Assert.Equal(2, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("usage", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("local", document.RootElement.GetProperty("meta").GetProperty("backend").GetString());
    }

    [Fact]
    public void RemovingTheLastOwnerWarnsInTheTable()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Graph(PlanSamples.TempHome()), "--table", "group", "owners", "remove", "Finance", "alice@contoso.com", "--dry-run");

        Assert.Equal(0, exitCode);
        Assert.Contains("warning: Finance loses its last owner", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void AGroupCsvIsOnePlanAndAFailedLineIsExitSix()
    {
        string home = PlanSamples.TempHome();

        (int exitCode, string stdout, _) = CliTests.RunWith(Graph(home), "--json", "group", "members", "add", "--from-csv", PlanSamples.Csv("group-members-add.csv"), "--yes");

        Assert.Equal(6, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("partial", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        JsonElement changes = document.RootElement.GetProperty("data").GetProperty("changes");
        Assert.Equal([2, 4], changes.EnumerateArray().Select(change => change.GetProperty("row").GetInt32()));
        Assert.Equal(["applied", "failed"], changes.EnumerateArray().Select(change => change.GetProperty("result").GetProperty("status").GetString()));
        Assert.Single(Records(home));
    }

    [Fact]
    public void AMalformedCsvIsRefusedBeforeAnythingStarts()
    {
        string home = PlanSamples.TempHome();

        (int exitCode, string stdout, _) = CliTests.RunWith(Exchange(home), "--json", "mailbox", "access", "grant", "--from-csv", PlanSamples.Csv("mailbox-grants-malformed.csv"), "--yes");

        Assert.Equal(2, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("mailbox-grants-malformed.csv, line 3: a quoted cell is not closed", document.RootElement.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal("local", document.RootElement.GetProperty("meta").GetProperty("backend").GetString());
        Assert.Empty(Records(home));
    }

    [Fact]
    public void ACsvAndArgumentsTogetherAreAUsageError()
    {
        (int exitCode, _, _) = CliTests.RunWith(Exchange(PlanSamples.TempHome()), "--json", "mailbox", "access", "grant", "finance", "--from-csv", PlanSamples.Csv("mailbox-grants.csv"));

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void AMailboxGrantSkipsTheHeldRightAndYesRunsTheCmdletThroughTheSidecar()
    {
        string home = PlanSamples.TempHome();
        string writes = Path.Combine(Path.GetTempPath(), $"m365-aegis-tests-{Guid.NewGuid():N}.jsonl");

        (int dryExit, string dryOut, _) = CliTests.RunWith(Exchange(home, writes), "--json", "mailbox", "access", "grant", "finance", "--user", "alice@contoso.com", "--user", "bob@contoso.com", "--right", "FullAccess", "--dry-run");
        (int exitCode, string stdout, _) = CliTests.RunWith(Exchange(home, writes), "--json", "mailbox", "access", "grant", "finance", "--user", "bob@contoso.com", "--right", "FullAccess", "--yes");

        Assert.Equal(0, dryExit);
        using JsonDocument dry = JsonDocument.Parse(dryOut);
        Assert.Equal(1, dry.RootElement.GetProperty("data").GetProperty("changes").GetArrayLength());
        Assert.Equal("holds FullAccess already", dry.RootElement.GetProperty("data").GetProperty("unchanged")[0].GetProperty("state").GetString());
        Assert.Equal("pwsh", dry.RootElement.GetProperty("meta").GetProperty("backend").GetString());
        Assert.Equal(0, exitCode);
        using JsonDocument applied = JsonDocument.Parse(stdout);
        Assert.Equal("applied", applied.RootElement.GetProperty("data").GetProperty("mode").GetString());
        string line = Assert.Single(File.ReadAllLines(writes));
        using JsonDocument write = JsonDocument.Parse(line);
        Assert.Equal("Add-MailboxPermission", write.RootElement.GetProperty("cmdlet").GetString());
        Assert.Equal("bob@contoso.com", write.RootElement.GetProperty("parameters").GetProperty("User").GetString());
        Assert.Equal("finance@contoso.com", write.RootElement.GetProperty("parameters").GetProperty("Identity").GetString());
        Assert.Single(Records(home));
    }

    [Fact]
    public void ACsvOfThreeGrantsIsADryRunOfThreeChangesThenTwoAppliedAndOneFailed()
    {
        string home = PlanSamples.TempHome();
        string writes = Path.Combine(Path.GetTempPath(), $"m365-aegis-tests-{Guid.NewGuid():N}.jsonl");
        string csv = PlanSamples.Csv("mailbox-grants.csv");

        (int dryExit, string dryOut, _) = CliTests.RunWith(Exchange(home, writes), "--json", "mailbox", "access", "grant", "--from-csv", csv, "--dry-run");
        (int exitCode, string stdout, string stderr) = CliTests.RunWith(Exchange(home, writes), "--table", "mailbox", "access", "grant", "--from-csv", csv, "--yes");

        Assert.Equal(0, dryExit);
        using JsonDocument dry = JsonDocument.Parse(dryOut);
        Assert.Equal(3, dry.RootElement.GetProperty("data").GetProperty("changes").GetArrayLength());
        Assert.Equal(6, exitCode);
        Assert.Contains("failed (line 3, Finance (finance@contoso.com), Send As for Dave Private (dave@example.com))", stdout, StringComparison.Ordinal);
        Assert.Contains("error: 2 of 3 change(s) applied, 1 failed (partial)", stderr, StringComparison.Ordinal);
        Assert.Equal(["Add-MailboxPermission", "Add-RecipientPermission"], File.ReadAllLines(writes).Select(line => JsonDocument.Parse(line).RootElement.GetProperty("cmdlet").GetString()));
    }

    [Fact]
    public void ForwardingSetGoesThroughSetMailboxWithTheCopy()
    {
        string home = PlanSamples.TempHome();
        string writes = Path.Combine(Path.GetTempPath(), $"m365-aegis-tests-{Guid.NewGuid():N}.jsonl");

        (int exitCode, string stdout, _) = CliTests.RunWith(Exchange(home, writes), "--json", "mailbox", "forwarding", "set", "alice", "--to", "alice@fabrikam.com", "--keep-copy", "--yes");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.StartsWith("alice@fabrikam.com is outside the accepted domains", document.RootElement.GetProperty("data").GetProperty("warnings")[0].GetString(), StringComparison.Ordinal);
        using JsonDocument write = JsonDocument.Parse(Assert.Single(File.ReadAllLines(writes)));
        JsonElement parameters = write.RootElement.GetProperty("parameters");
        Assert.Equal("Set-Mailbox", write.RootElement.GetProperty("cmdlet").GetString());
        Assert.Equal("alice@fabrikam.com", parameters.GetProperty("ForwardingSmtpAddress").GetString());
        Assert.True(parameters.GetProperty("DeliverToMailboxAndForward").GetBoolean());
    }

    [Fact]
    public void ForwardingClearOfAMailboxThatForwardsNowhereIsUpToDate()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Exchange(PlanSamples.TempHome()), "--json", "mailbox", "forwarding", "clear", "alice");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("up-to-date", document.RootElement.GetProperty("data").GetProperty("mode").GetString());
    }

    [Fact]
    public void ACallWriteUnderDryRunIsThePlanAndAGetRefusesDryRun()
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Graph(PlanSamples.TempHome()), "--json", "call", "DELETE", "/groups/00000000-0000-0000-0000-000000000031", "--dry-run");
        (int getExit, _, _) = CliTests.RunWith(Graph(PlanSamples.TempHome()), "--json", "call", "GET", "/me", "--dry-run");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("send", document.RootElement.GetProperty("data").GetProperty("changes")[0].GetProperty("action").GetString());
        Assert.Equal(2, getExit);
    }

    [Fact]
    public async Task TheThreeRenderingsOfAPlan()
    {
        string home = PlanSamples.TempHome();
        string dryRun = await RenderAsync(PlanSamples.GroupWrite([]), WriteMode.DryRun, home);
        string refused = await RenderAsync(PlanSamples.GroupWrite([]), WriteMode.Refuse, home);
        string applied = await RenderAsync(PlanSamples.GroupWrite([], second: PlanSamples.Forbidden), WriteMode.Apply, home);

        await Verify($"=== --dry-run ===\n{dryRun}=== no flag ===\n{refused}=== --yes ===\n{applied}");
    }

    [Fact]
    public async Task APlanFromAFileFollowsItsLines()
    {
        string home = PlanSamples.TempHome();

        await Verify(await RenderAsync(PlanSamples.GroupWrite([], fromFile: true), WriteMode.DryRun, home));
    }

    private static async Task<string> RenderAsync(WritePlan write, WriteMode mode, string home)
    {
        CommandResult<Plan> result = await PlanGuard.RunAsync(write, mode, PlanSamples.Store(home), Ct);
        var output = new StringWriter();
        var error = new StringWriter();
        Renderer.Render(result, new EnvelopeMeta([], 12, Backend.Graph), PlanTableView.Instance, OutputMode.Table, output, error);
        return PlanSamples.Scrub($"--- stdout ---\n{output}--- stderr ---\n{error}", home);
    }
}
