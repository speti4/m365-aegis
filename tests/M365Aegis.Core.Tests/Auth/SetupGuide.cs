namespace M365Aegis.Core.Tests.Auth;

/// <summary>One row of the setup guide's permissions tables: the <c>###</c> heading above it and its four cells.</summary>
/// <param name="Section">The table's heading (<c>Read-only: the lookups</c>).</param>
/// <param name="Cells">The command group, the permission, the API and the role, trimmed.</param>
internal sealed record GuideRow(string Section, string[] Cells)
{
    /// <summary>The first cell without its backticks, as the maps name a row.</summary>
    internal string Commands => Cells[0].Replace("`", "", StringComparison.Ordinal);
}

/// <summary>
/// docs/setup.md as the map tests read it (tasks 109 and 111): its lines, and the rows of the tables under
/// "## Permissions", each with four cells.
/// </summary>
internal static class SetupGuide
{
    internal static string[] Lines { get; } = File.ReadAllLines(Path.Combine(RepoRoot(), "docs", "setup.md"));

    /// <summary>Every row of the tables under "## Permissions", the header and the separator left out.</summary>
    internal static List<GuideRow> PermissionsTable()
    {
        int start = Array.IndexOf(Lines, "## Permissions");
        Assert.True(start >= 0, "docs/setup.md has no section '## Permissions'");
        int end = Array.FindIndex(Lines, start + 1, line => line.StartsWith("## ", StringComparison.Ordinal));
        string section = "";
        var rows = new List<GuideRow>();
        foreach (string line in Lines[(start + 1)..end])
        {
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                section = line[4..];
                continue;
            }

            if (!line.StartsWith("| ", StringComparison.Ordinal) || line.StartsWith("| command group |", StringComparison.Ordinal) || line.StartsWith("| ---", StringComparison.Ordinal))
            {
                continue;
            }

            string[] cells = [.. line.Split('|')[1..^1].Select(cell => cell.Trim())];
            Assert.True(cells.Length == 4, $"a permissions row that does not have four cells: {line}");
            rows.Add(new GuideRow(section, cells));
        }

        return rows;
    }

    private static string RepoRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docs", "setup.md")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("no docs/setup.md above the test's folder");
    }
}
