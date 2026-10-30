using System.Globalization;
using System.Text;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Operations;

/// <summary>One data line of a CSV file: its line number and its cells by column name.</summary>
/// <param name="Line">The line the record starts on (the header is line 1).</param>
/// <param name="Cells">The cells, by the header's column names, compared without case; trimmed.</param>
public sealed record CsvRow(int Line, IReadOnlyDictionary<string, string> Cells)
{
    /// <summary>A cell's value, or null when the column is missing or the cell is empty.</summary>
    public string? this[string column] => Cells.TryGetValue(column, out string? value) && value.Length > 0 ? value : null;
}

/// <summary>
/// The CSV files of the writes' <c>--from-csv</c> (task 054), read strictly so that a file the reader is not sure of
/// is refused as a whole before anything is sent: a header line naming the columns (any case, any order), then one
/// change per line; commas between the cells, or semicolons when the header has semicolons and no comma (a
/// spreadsheet saved under a locale whose decimal mark is the comma); a cell in double quotes may hold the separator,
/// a line break and a doubled quote. UTF-8, with or without a byte order mark. Blank lines are skipped. A file that
/// breaks the rules (an unknown or missing column, a line with more or fewer cells than the header, a stray quote, no
/// line under the header) is a usage error naming the file and the line.
/// </summary>
public static class CsvRows
{
    /// <summary>Reads a file whose header must name every column of <paramref name="required"/> and nothing outside it and <paramref name="optional"/>.</summary>
    /// <exception cref="AegisException">A usage error naming the file and the line.</exception>
    public static IReadOnlyList<CsvRow> Read(string path, IReadOnlyList<string> required, IReadOnlyList<string> optional)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(required);
        ArgumentNullException.ThrowIfNull(optional);
        string name = Path.GetFileName(path);
        string columns = string.Join(",", required.Concat(optional));
        if (!File.Exists(path))
        {
            throw new AegisException(FailureKind.Usage, $"no file '{path}' for --from-csv", $"give the path of a CSV file with the columns {columns}");
        }

        string text;
        try
        {
            text = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
        }
        catch (DecoderFallbackException exception)
        {
            throw new AegisException(FailureKind.Usage, $"{name} is not UTF-8 text", "save the file as CSV UTF-8; nothing was sent", exception);
        }

        return Parse(text, name, required, optional);
    }

    /// <summary>Reads the text of a file (<paramref name="name"/> names it in the messages).</summary>
    /// <exception cref="AegisException">A usage error naming the file and the line.</exception>
    public static IReadOnlyList<CsvRow> Parse(string text, string name, IReadOnlyList<string> required, IReadOnlyList<string> optional)
    {
        ArgumentNullException.ThrowIfNull(text);
        string columns = string.Join(",", required.Concat(optional));
        string hint = $"the columns are {columns}, in a header line, then one change per line; nothing was sent";
        AegisException Refuse(int line, string what) =>
            new(FailureKind.Usage, string.Format(CultureInfo.InvariantCulture, "{0}, line {1}: {2}", name, line, what), hint);

        text = text.TrimStart('﻿');
        int firstBreak = text.IndexOfAny(['\r', '\n']);
        string headerLine = firstBreak < 0 ? text : text[..firstBreak];
        char separator = headerLine.Contains(';', StringComparison.Ordinal) && !headerLine.Contains(',', StringComparison.Ordinal) ? ';' : ',';

        List<(int Line, List<string> Cells)> records = Records(text, separator, Refuse);
        if (records.Count == 0)
        {
            throw Refuse(1, "the file is empty");
        }

        List<string> header = records[0].Cells.Select(cell => cell.Trim()).ToList();
        var known = new HashSet<string>(required.Concat(optional), StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string column in header)
        {
            if (!known.Contains(column))
            {
                throw Refuse(records[0].Line, column.Length == 0 ? "the header has an empty column name" : $"unknown column '{column}'");
            }

            if (!seen.Add(column))
            {
                throw Refuse(records[0].Line, $"the column '{column}' is named twice");
            }
        }

        foreach (string column in required)
        {
            if (!seen.Contains(column))
            {
                throw Refuse(records[0].Line, $"the header has no '{column}' column");
            }
        }

        var rows = new List<CsvRow>();
        foreach ((int line, List<string> cells) in records.Skip(1))
        {
            if (cells.Count != header.Count)
            {
                throw Refuse(line, string.Format(CultureInfo.InvariantCulture, "{0} cell(s), the header has {1}", cells.Count, header.Count));
            }

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < header.Count; index++)
            {
                map[header[index]] = cells[index].Trim();
            }

            foreach (string column in required)
            {
                if (map[column].Length == 0)
                {
                    throw Refuse(line, $"the '{column}' cell is empty");
                }
            }

            rows.Add(new CsvRow(line, map));
        }

        return rows.Count > 0 ? rows : throw Refuse(records[^1].Line, "no line under the header");
    }

    /// <summary>The records of the text with the line each starts on; a line of nothing but blanks is skipped.</summary>
    private static List<(int Line, List<string> Cells)> Records(string text, char separator, Func<int, string, AegisException> refuse)
    {
        var records = new List<(int, List<string>)>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        int line = 1;
        int start = 1;
        bool quoted = false;
        bool closed = false;
        bool any = false;

        void EndRecord()
        {
            cells.Add(cell.ToString());
            if (any || cells.Count > 1 || cells[0].Trim().Length > 0)
            {
                records.Add((start, cells));
            }

            cells = [];
            cell.Clear();
            any = false;
            closed = false;
        }

        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        cell.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                        closed = true;
                    }
                }
                else
                {
                    if (character == '\n')
                    {
                        line++;
                    }

                    cell.Append(character);
                }

                continue;
            }

            if (character == separator)
            {
                cells.Add(cell.ToString());
                cell.Clear();
                closed = false;
                any = true;
            }
            else if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                EndRecord();
                line++;
                start = line;
            }
            else if (character == '"')
            {
                if (closed || cell.ToString().Trim().Length > 0)
                {
                    throw refuse(line, "a quote inside a cell that is not quoted (write it as two quotes inside a quoted cell)");
                }

                cell.Clear();
                quoted = true;
                any = true;
            }
            else if (closed)
            {
                if (!char.IsWhiteSpace(character))
                {
                    throw refuse(line, "text after the closing quote of a cell");
                }
            }
            else
            {
                cell.Append(character);
            }
        }

        if (quoted)
        {
            throw refuse(start, "a quoted cell is not closed");
        }

        if (cell.Length > 0 || cells.Count > 0 || any)
        {
            EndRecord();
        }

        return records;
    }
}
