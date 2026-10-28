using System.Globalization;
using System.Text;

namespace M365Aegis.Cli.Output;

/// <summary>
/// A plain text table: a header, a rule, the rows, columns padded to the widest cell. No colors, no terminal
/// width detection, so the same input gives the same bytes on every machine, which the snapshot tests rely on.
/// </summary>
internal static class TableWriter
{
    /// <summary>Renders the table; every row has as many cells as there are headers.</summary>
    internal static string Render(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        int[] widths = new int[headers.Count];
        for (int column = 0; column < headers.Count; column++)
        {
            widths[column] = headers[column].Length;
            foreach (IReadOnlyList<string> row in rows)
            {
                if (row.Count != headers.Count)
                {
                    throw new ArgumentException(
                        string.Format(CultureInfo.InvariantCulture, "a row has {0} cells, the header {1}", row.Count, headers.Count),
                        nameof(rows));
                }

                widths[column] = Math.Max(widths[column], row[column].Length);
            }
        }

        var builder = new StringBuilder();
        AppendRow(builder, headers, widths);
        builder.Append(string.Join("  ", widths.Select(width => new string('-', width)))).Append('\n');
        foreach (IReadOnlyList<string> row in rows)
        {
            AppendRow(builder, row, widths);
        }

        return builder.ToString();
    }

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string> cells, int[] widths)
    {
        for (int column = 0; column < cells.Count; column++)
        {
            if (column > 0)
            {
                builder.Append("  ");
            }

            // The last column is not padded, so a line carries no trailing spaces.
            builder.Append(column == cells.Count - 1 ? cells[column] : cells[column].PadRight(widths[column]));
        }

        builder.Append('\n');
    }
}
