using System.Globalization;
using System.Text.RegularExpressions;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Tests.Output;

/// <summary>
/// The exit codes as a contract (D-006, task 073): the <see cref="ExitCode"/> enum frozen as <c>name = value</c> lines
/// (Snapshots/ExitCodeContractTests.TheExitCodes.verified.txt), and the exit code table of docs/development.md ("The
/// output contract") read back and held to the enum and to the failure kinds' error codes, both ways. A renumbered or
/// renamed member, a code missing from the docs or a row the enum does not have fails here instead of in a script.
/// </summary>
public sealed partial class ExitCodeContractTests
{
    [Fact]
    public Task TheExitCodes() => Verify(string.Join("\n", Enum.GetValues<ExitCode>().OrderBy(code => (int)code).Select(code => $"{code} = {(int)code}")));

    [Fact]
    public void TheDocsTableListsEveryExitCodeAndNothingElse()
    {
        IReadOnlyDictionary<int, string> documented = DocumentedCodes();
        int[] codes = Enum.GetValues<ExitCode>().Select(code => (int)code).Order().ToArray();

        Assert.Equal(codes, documented.Keys.Order().ToArray());
    }

    [Fact]
    public void TheDocsTableNamesTheErrorCodeOfEachExitCode()
    {
        IReadOnlyDictionary<int, string> documented = DocumentedCodes();
        Dictionary<int, string> expected = Enum.GetValues<FailureKind>().ToDictionary(kind => (int)kind.ToExitCode(), kind => kind.ToCode());
        expected[(int)ExitCode.Ok] = "";

        Assert.Equal(expected.OrderBy(pair => pair.Key), documented.OrderBy(pair => pair.Key));
    }

    [Fact]
    public void EveryExitCodeButOkHasOneFailureKind()
    {
        IEnumerable<int> fromKinds = Enum.GetValues<FailureKind>().Select(kind => (int)kind.ToExitCode()).Order();
        IEnumerable<int> failures = Enum.GetValues<ExitCode>().Where(code => code != ExitCode.Ok).Select(code => (int)code).Order();

        Assert.Equal(failures, fromKinds);
    }

    /// <summary>The rows of the exit code table under "The output contract": the code and the error code (empty for 0).</summary>
    private static Dictionary<int, string> DocumentedCodes()
    {
        string[] lines = File.ReadAllLines(Path.Combine(RepoRoot(), "docs", "development.md"));
        int section = Array.FindIndex(lines, line => line == "## The output contract");
        Assert.True(section >= 0, "docs/development.md has no section '## The output contract'");
        int header = Array.FindIndex(lines, section, line => line.StartsWith("| code | error code | meaning |", StringComparison.Ordinal));
        Assert.True(header >= 0, "the output contract has no table '| code | error code | meaning |'");
        var rows = new Dictionary<int, string>();
        foreach (string line in lines.Skip(header + 2).TakeWhile(line => line.StartsWith('|')))
        {
            Match row = TableRow().Match(line);
            Assert.True(row.Success, $"an exit code row that does not read as '| <code> | `<error code>` | <meaning> |': {line}");
            int code = int.Parse(row.Groups["code"].Value, CultureInfo.InvariantCulture);
            Assert.True(rows.TryAdd(code, row.Groups["error"].Value), $"the exit code {code} has two rows");
        }

        return rows;
    }

    private static string RepoRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docs", "development.md")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("no docs/development.md above the test's folder");
    }

    [GeneratedRegex(@"^\| (?<code>\d+) \| (?:`(?<error>[a-z-]+)` )?\| .+ \|$")]
    private static partial Regex TableRow();
}
