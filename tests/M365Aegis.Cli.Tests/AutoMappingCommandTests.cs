using System.Text.Json;
using M365Aegis.Tests;

namespace M365Aegis.Cli.Tests;

/// <summary>
/// <c>mailbox access grant … --no-automapping</c> (task 059) as child processes over the fake module, which records
/// each write in the file <c>M365AEGIS_FAKE_EXO_WRITES</c> names: the plan names the choice, the apply sends
/// <c>-AutoMapping:$false</c>, and the flag beside another right, or on a revoke, is a usage error.
/// </summary>
public sealed class AutoMappingCommandTests
{
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

    [Fact]
    public void TheDryRunNamesTheChoiceAndYesSendsAutoMappingFalse()
    {
        string home = PlanSamples.TempHome();
        string writes = Path.Combine(Path.GetTempPath(), $"m365-aegis-tests-{Guid.NewGuid():N}.jsonl");
        string[] grant = ["mailbox", "access", "grant", "reception", "--user", "carol@contoso.com", "--right", "FullAccess", "--no-automapping"];

        (int dryExit, string dryOut, _) = CliTests.RunWith(Exchange(home, writes), ["--json", .. grant, "--dry-run"]);
        (int exitCode, _, _) = CliTests.RunWith(Exchange(home, writes), ["--json", .. grant, "--yes"]);

        Assert.Equal(0, dryExit);
        using JsonDocument dry = JsonDocument.Parse(dryOut);
        Assert.Equal(
            "FullAccess for Carol Example (carol@contoso.com), without automapping",
            dry.RootElement.GetProperty("data").GetProperty("changes")[0].GetProperty("detail").GetString());
        Assert.Equal(0, exitCode);
        using JsonDocument write = JsonDocument.Parse(Assert.Single(File.ReadAllLines(writes)));
        Assert.Equal("Add-MailboxPermission", write.RootElement.GetProperty("cmdlet").GetString());
        Assert.False(write.RootElement.GetProperty("parameters").GetProperty("AutoMapping").GetBoolean());
    }

    [Fact]
    public void TheCsvsAutomappingColumnGoesToTheFullAccessLine()
    {
        string home = PlanSamples.TempHome();

        (int exitCode, string stdout, _) = CliTests.RunWith(Exchange(home), "--json", "mailbox", "access", "grant", "--from-csv", PlanSamples.Csv("mailbox-grants-automapping.csv"), "--dry-run");

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal(
            [(2, "FullAccess for Carol Example (carol@contoso.com), without automapping"), (3, "Send As for Carol Example (carol@contoso.com)"), (4, "FullAccess for Bob Example (bob@contoso.com)")],
            document.RootElement.GetProperty("data").GetProperty("changes").EnumerateArray().Select(change => (change.GetProperty("row").GetInt32(), change.GetProperty("detail").GetString())));
    }

    [Theory]
    [InlineData("grant", "SendAs")]
    [InlineData("revoke", "FullAccess")]
    public void TheFlagBesideAnotherRightOrOnARevokeIsAUsageError(string verb, string right)
    {
        (int exitCode, string stdout, _) = CliTests.RunWith(Exchange(PlanSamples.TempHome()), "--json", "mailbox", "access", verb, "reception", "--user", "carol@contoso.com", "--right", right, "--no-automapping");

        Assert.Equal(2, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("usage", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }
}
