using System.Text.Json;
using M365Aegis.Tests;

namespace M365Aegis.Cli.Tests;

/// <summary>
/// The CSV edge cases of task 074 through every write command that takes <c>--from-csv</c>, found in the command tree
/// as the write guard finds it, as child processes under <c>--dry-run</c> over every layer at once
/// (<see cref="WriteGuardTests.GuardRun"/>): the file Excel writes (a byte order mark, CRLF) is one plan over its two
/// lines, a repeated line is one change and not two (forwarding: the usage error naming both lines), and a malformed
/// last line refuses the whole file with its line number before anything is called. The reader's own cases, every
/// one over every reader, are <c>CsvEdgeCaseTests</c> in the Core tests.
/// </summary>
public sealed class CsvEdgeCaseCommandTests
{
    /// <summary>The write commands that take <c>--from-csv</c>, with every case the command line runs.</summary>
    public static TheoryData<string, string> Cases
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach ((string path, System.CommandLine.Command command) in WriteGuardTests.WriteCommands())
            {
                if (WriteGuardTests.Has(command, "--from-csv"))
                {
                    foreach (string name in new[] { "bom-crlf", "duplicate-line", "malformed-last-line" })
                    {
                        data.Add(path, name);
                    }
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ADryRunOfEveryCsvWriteReadsTheCaseAsDocumented(string path, string name)
    {
        string family = Family(path);
        var run = new WriteGuardTests.GuardRun();

        (int exitCode, string stdout, string stderr) = run.Run([.. path.Split(' '), "--from-csv", PlanSamples.Csv(Path.Combine("edge-cases", family, $"{name}.csv")), "--dry-run"]);

        run.AssertNothingSent(0);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement root = document.RootElement;
        string? refusal = (name, family) switch
        {
            ("malformed-last-line", "group") => "malformed-last-line.csv, line 4: 1 cell(s), the header has 2",
            ("malformed-last-line", "mailbox-access") => "malformed-last-line.csv, line 4: a quoted cell is not closed",
            ("malformed-last-line", "forwarding-set") => "malformed-last-line.csv, line 4: text after the closing quote of a cell",
            ("malformed-last-line", "forwarding-clear") => "malformed-last-line.csv, line 4: 2 cell(s), the header has 1",
            ("duplicate-line", "forwarding-set") => "duplicate-line.csv: alice is named twice (lines 2 and 3)",
            ("duplicate-line", "forwarding-clear") => "duplicate-line.csv: bob is named twice (lines 2 and 3)",
            _ => null,
        };
        if (refusal is not null)
        {
            // Refused whole by the reader, before a sign-in or a request: the backend is local.
            Assert.Equal(2, exitCode);
            Assert.Equal("usage", root.GetProperty("error").GetProperty("code").GetString());
            Assert.Equal(refusal, root.GetProperty("error").GetProperty("message").GetString());
            Assert.Equal("local", root.GetProperty("meta").GetProperty("backend").GetString());
            return;
        }

        Assert.True(exitCode == 0, $"{path} --from-csv {name}: exit {exitCode}: {stdout}{stderr}");
        JsonElement plan = root.GetProperty("data");
        List<(int Row, string Change)> changes = plan.GetProperty("changes").EnumerateArray()
            .Select(change => (change.GetProperty("row").GetInt32(), $"{change.GetProperty("action").GetString()} {change.GetProperty("target").GetString()}: {change.GetProperty("detail").GetString()}"))
            .ToList();
        List<int> unchanged = plan.GetProperty("unchanged").EnumerateArray().Select(item => item.GetProperty("row").GetInt32()).ToList();

        // Every line of the file is planned once, as a change or as an object already as asked.
        int[] lines = name == "duplicate-line" ? [2, 3, 4] : [2, 3];
        Assert.Equal(lines, changes.Select(change => change.Row).Concat(unchanged).Order());
        Assert.Equal(changes.Count, changes.Select(change => change.Change).Distinct(StringComparer.Ordinal).Count());
        if (name == "duplicate-line")
        {
            // The repeated line is the same change as line 2: never a second change.
            Assert.DoesNotContain(3, changes.Select(change => change.Row));
        }
    }

    /// <summary>The family of edge-case files a write reads (its columns); a new CSV write fails here until it has one.</summary>
    private static string Family(string path) => path switch
    {
        _ when path.StartsWith("group ", StringComparison.Ordinal) => "group",
        _ when path.StartsWith("mailbox access ", StringComparison.Ordinal) => "mailbox-access",
        "mailbox forwarding set" => "forwarding-set",
        "mailbox forwarding clear" => "forwarding-clear",
        _ => throw new InvalidOperationException($"the CSV write '{path}' has no family of edge-case files under tests/fixtures/csv/edge-cases"),
    };
}
