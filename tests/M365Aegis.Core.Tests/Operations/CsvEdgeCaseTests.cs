using M365Aegis.Core.Exchange;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Operations;

/// <summary>
/// The CSV edge cases of the bulk writes (task 074) over the reader of every write's <c>--from-csv</c>, in process:
/// the files under <c>tests/fixtures/csv/edge-cases/&lt;family&gt;/&lt;case&gt;.csv</c>, one per case for each set of
/// columns (the group writes; the mailbox access writes, whose grant and revoke read the same file; forwarding set;
/// forwarding clear). A file Excel writes (a byte order mark, CRLF), LF, a quoted name with a comma, spaces around the
/// cells and the header's names, the header in another case and order and blank lines at the end read as the same
/// rows; a repeated line is a row of its own for the group and access writes (the plan makes it one change, which the
/// Cli test holds) and a refusal naming both lines for forwarding; a malformed last line refuses the whole file with
/// its line number. The child-process dry runs of the same files are <c>CsvEdgeCaseCommandTests</c> in the Cli tests.
/// </summary>
public sealed class CsvEdgeCaseTests
{
    /// <summary>The cases, one file each per family.</summary>
    internal static readonly string[] CaseNames =
        ["bom-crlf", "lf", "quoted-comma", "surrounding-spaces", "header-case-order", "duplicate-line", "trailing-empty-lines", "malformed-last-line"];

    /// <summary>The readers of the writes: what each write command's <c>--from-csv</c> calls, and the family of files it reads.</summary>
    private static readonly Dictionary<string, (string Family, Func<string, IEnumerable<string>> Read)> Readers = new(StringComparer.Ordinal)
    {
        ["group members|owners add|remove"] = ("group", path => GroupWrites.FromCsv(path).Select(row => $"{row.Row}|{row.Group}|{row.User}")),
        ["mailbox access grant"] = ("mailbox-access", path => MailboxLookups.AccessRowsFromCsv(path, grant: true).Select(Access)),
        ["mailbox access revoke"] = ("mailbox-access", path => MailboxLookups.AccessRowsFromCsv(path, grant: false).Select(Access)),
        ["mailbox forwarding set"] = ("forwarding-set", path => MailboxLookups.ForwardingRowsFromCsv(path, set: true).Select(row => $"{row.Row}|{row.Mailbox}|{row.To}|{row.KeepCopy}")),
        ["mailbox forwarding clear"] = ("forwarding-clear", path => MailboxLookups.ForwardingRowsFromCsv(path, set: false).Select(row => $"{row.Row}|{row.Mailbox}")),
    };

    /// <summary>What each family's files read as: the rows (<c>line|cells…</c>), or the refusal's message.</summary>
    private static readonly Dictionary<string, Dictionary<string, Expected>> Expectations = new(StringComparer.Ordinal)
    {
        ["group"] = Family(
            ["2|Finance|dave@contoso.com", "3|Finance|bob@contoso.com"],
            quoted: ["2|Finance, EU|dave@contoso.com", "3|Finance|bob@contoso.com"],
            duplicate: Rows("2|Finance|dave@contoso.com", "3|Finance|dave@contoso.com", "4|Finance|bob@contoso.com"),
            malformed: "malformed-last-line.csv, line 4: 1 cell(s), the header has 2"),
        ["mailbox-access"] = Family(
            ["2|finance|bob@contoso.com|FullAccess", "3|reception|carol@contoso.com|SendAs"],
            quoted: ["2|Finance, Shared|bob@contoso.com|FullAccess", "3|reception|carol@contoso.com|SendAs"],
            duplicate: Rows("2|finance|bob@contoso.com|FullAccess", "3|finance|bob@contoso.com|FullAccess", "4|reception|carol@contoso.com|SendAs"),
            malformed: "malformed-last-line.csv, line 4: a quoted cell is not closed"),
        ["forwarding-set"] = Family(
            ["2|alice|alice@fabrikam.com|True", "3|carol|carol@fabrikam.com|False"],
            quoted: ["2|Alice, Example|alice@fabrikam.com|True", "3|carol|carol@fabrikam.com|False"],
            duplicate: Refused("duplicate-line.csv: alice is named twice (lines 2 and 3)"),
            malformed: "malformed-last-line.csv, line 4: text after the closing quote of a cell"),
        ["forwarding-clear"] = Family(
            ["2|bob", "3|carol"],
            quoted: ["2|Bob, Example", "3|carol"],
            duplicate: Refused("duplicate-line.csv: bob is named twice (lines 2 and 3)"),
            malformed: "malformed-last-line.csv, line 4: 2 cell(s), the header has 1"),
    };

    /// <summary>Every reader with every case.</summary>
    public static TheoryData<string, string> Cases
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (string reader in Readers.Keys)
            {
                foreach (string name in CaseNames)
                {
                    data.Add(reader, name);
                }
            }

            return data;
        }
    }

    /// <summary>A case's file of a family.</summary>
    internal static string File(string family, string name) => PlanSamples.Csv(Path.Combine("edge-cases", family, $"{name}.csv"));

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryReaderReadsEveryCaseAsDocumented(string reader, string name)
    {
        (string family, Func<string, IEnumerable<string>> read) = Readers[reader];
        Expected expected = Expectations[family][name];
        string path = File(family, name);

        if (expected.Refusal is { } message)
        {
            AegisException refused = Assert.Throws<AegisException>(() => read(path).ToList());
            Assert.Equal(FailureKind.Usage, refused.Failure.Kind);
            Assert.Equal(message, refused.Failure.Message);
            Assert.Contains("nothing was sent", refused.Failure.Hint, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(expected.Rows, read(path));
        }
    }

    [Fact]
    public void EveryFamilyHasAFileOfEveryCaseAndNoOther()
    {
        foreach (string family in Expectations.Keys)
        {
            IEnumerable<string> files = Directory.GetFiles(Path.GetDirectoryName(File(family, "lf"))!, "*.csv").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal)!;
            Assert.Equal(CaseNames.Order(StringComparer.Ordinal), files);
        }
    }

    [Fact]
    public void TheExcelFilesCarryAByteOrderMarkAndCrlf()
    {
        foreach (string family in Expectations.Keys)
        {
            byte[] bytes = System.IO.File.ReadAllBytes(File(family, "bom-crlf"));
            Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
            Assert.Contains("\r\n", System.Text.Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
            Assert.DoesNotContain("\r", System.Text.Encoding.UTF8.GetString(System.IO.File.ReadAllBytes(File(family, "lf"))), StringComparison.Ordinal);
        }
    }

    private static string Access(AccessChangeRow row) => $"{row.Row}|{row.Mailbox}|{row.User}|{row.Right}";

    /// <summary>A family's expectations: every case reads as <paramref name="rows"/> but the four that differ.</summary>
    private static Dictionary<string, Expected> Family(string[] rows, string[] quoted, Expected duplicate, string malformed)
    {
        Dictionary<string, Expected> cases = CaseNames.ToDictionary(name => name, _ => new Expected(rows, null), StringComparer.Ordinal);
        cases["quoted-comma"] = new Expected(quoted, null);
        cases["duplicate-line"] = duplicate;
        cases["malformed-last-line"] = Refused(malformed);
        return cases;
    }

    private static Expected Rows(params string[] rows) => new(rows, null);

    private static Expected Refused(string message) => new([], message);

    /// <summary>The rows a file reads as, or the message of its refusal.</summary>
    private sealed record Expected(string[] Rows, string? Refusal);
}
