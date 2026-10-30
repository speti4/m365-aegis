using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Operations;

/// <summary>The strict CSV reader of <c>--from-csv</c> (task 054): what it reads, and every way a file is refused whole.</summary>
public sealed class CsvRowsTests
{
    private static readonly string[] Group = ["group", "user"];

    [Fact]
    public void ReadsTheColumnsInAnyOrderAndCaseWithQuotesAndBlankLines()
    {
        string text = "﻿User,GROUP\r\n\r\n\"bob@contoso.com\",\"Finance, \"\"EU\"\"\"\r\ndave@contoso.com , Finance\r\n";

        IReadOnlyList<CsvRow> rows = CsvRows.Parse(text, "members.csv", Group, []);

        Assert.Equal(2, rows.Count);
        Assert.Equal(3, rows[0].Line);
        Assert.Equal("Finance, \"EU\"", rows[0]["group"]);
        Assert.Equal("bob@contoso.com", rows[0]["user"]);
        Assert.Equal(4, rows[1].Line);
        Assert.Equal("dave@contoso.com", rows[1]["user"]);
    }

    [Fact]
    public void ReadsSemicolonsWhenTheHeaderHasNoComma()
    {
        IReadOnlyList<CsvRow> rows = CsvRows.Parse("mailbox;to;keepCopy\nbob;bob@fabrikam.com;\n", "f.csv", ["mailbox", "to"], ["keepCopy"]);

        CsvRow row = Assert.Single(rows);
        Assert.Equal("bob@fabrikam.com", row["to"]);
        Assert.Null(row["keepCopy"]);
    }

    [Fact]
    public void AQuotedCellMayHoldALineBreakAndTheNextLineCountsOn()
    {
        IReadOnlyList<CsvRow> rows = CsvRows.Parse("group,user\n\"Finance\nEU\",bob@contoso.com\nFinance,dave@contoso.com\n", "m.csv", Group, []);

        Assert.Equal("Finance\nEU", rows[0]["group"]);
        Assert.Equal(2, rows[0].Line);
        Assert.Equal(4, rows[1].Line);
    }

    [Theory]
    [InlineData("", "line 1: the file is empty")]
    [InlineData("group,user\n", "line 1: no line under the header")]
    [InlineData("group,user,right\nFinance,bob@contoso.com,x\n", "line 1: unknown column 'right'")]
    [InlineData("group\nFinance\n", "line 1: the header has no 'user' column")]
    [InlineData("group,user,Group\nFinance,bob@contoso.com,x\n", "line 1: the column 'Group' is named twice")]
    [InlineData("group,user\nFinance\n", "line 2: 1 cell(s), the header has 2")]
    [InlineData("group,user\nFinance,bob@contoso.com,extra\n", "line 2: 3 cell(s), the header has 2")]
    [InlineData("group,user\nFinance,\n", "line 2: the 'user' cell is empty")]
    [InlineData("group,user\nFin\"ance,bob@contoso.com\n", "line 2: a quote inside a cell that is not quoted")]
    [InlineData("group,user\n\"Finance\"x,bob@contoso.com\n", "line 2: text after the closing quote of a cell")]
    [InlineData("group,user\nFinance,\"bob@contoso.com\n", "line 2: a quoted cell is not closed")]
    public void AMalformedFileIsRefusedWholeWithTheLine(string text, string message)
    {
        AegisException exception = Assert.Throws<AegisException>(() => CsvRows.Parse(text, "members.csv", Group, []));

        Assert.Equal(FailureKind.Usage, exception.Failure.Kind);
        Assert.StartsWith($"members.csv, {message}", exception.Failure.Message, StringComparison.Ordinal);
        Assert.Contains("group,user", exception.Failure.Hint, StringComparison.Ordinal);
        Assert.Contains("nothing was sent", exception.Failure.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingFileAndAFileThatIsNotUtf8AreUsageErrors()
    {
        AegisException missing = Assert.Throws<AegisException>(() => CsvRows.Read(PlanSamples.Csv("no-such-file.csv"), Group, []));
        string latin = Path.Combine(Path.GetTempPath(), $"m365-aegis-tests-{Guid.NewGuid():N}.csv");
        File.WriteAllBytes(latin, [.. "group,user\nP"u8, 0xE9, .. "ter,bob@contoso.com\n"u8]);

        AegisException notUtf8 = Assert.Throws<AegisException>(() => CsvRows.Read(latin, Group, []));

        Assert.Equal(FailureKind.Usage, missing.Failure.Kind);
        Assert.Equal(FailureKind.Usage, notUtf8.Failure.Kind);
        Assert.Contains("not UTF-8", notUtf8.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSampleFilesRead()
    {
        Assert.Equal(3, CsvRows.Read(PlanSamples.Csv("group-members-add.csv"), Group, []).Count);
        AegisException malformed = Assert.Throws<AegisException>(() => CsvRows.Read(PlanSamples.Csv("mailbox-grants-malformed.csv"), ["mailbox", "user", "right"], []));
        Assert.Equal("mailbox-grants-malformed.csv, line 3: a quoted cell is not closed", malformed.Failure.Message);
    }
}
