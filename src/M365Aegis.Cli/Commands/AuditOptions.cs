using System.CommandLine;
using System.Globalization;
using M365Aegis.Core.Audit;

namespace M365Aegis.Cli.Commands;

/// <summary>The window options the log lookups share (<c>signin list</c>, <c>audit list</c>), and their table cells.</summary>
internal static class AuditOptions
{
    /// <summary>The most entries a log lookup returns unless <c>--limit</c> says otherwise.</summary>
    internal const int DefaultLimit = 1000;

    /// <summary><c>--days N</c>: the last N days.</summary>
    internal static Option<int?> Days() => new("--days")
    {
        Description = $"The last N days, from 1 to {AuditWindow.MaxDays.ToString(CultureInfo.InvariantCulture)} ({AuditWindow.DefaultDays.ToString(CultureInfo.InvariantCulture)} when neither --days nor --since is given).",
    };

    /// <summary><c>--since &lt;date&gt;</c>: an explicit start.</summary>
    internal static Option<string?> Since() => new("--since")
    {
        Description = "The start of the window instead of --days: a date (2026-10-01) or a date and time, UTC unless an offset is named.",
    };

    /// <summary><c>--until &lt;date&gt;</c>: an explicit end.</summary>
    internal static Option<string?> Until() => new("--until")
    {
        Description = "The end of the window (now when omitted): a date or a date and time, UTC unless an offset is named; --days counts back from it.",
    };

    /// <summary><c>--limit N</c>: the cap on the entries.</summary>
    internal static Option<int> Limit(string what) => new("--limit")
    {
        Description = $"The most {what} to return, the newest first ({DefaultLimit.ToString(CultureInfo.InvariantCulture)} by default).",
        DefaultValueFactory = _ => DefaultLimit,
    };

    /// <summary>The window under a log lookup's table: <c>since … UTC</c>, and <c>until … UTC</c> when it has an end.</summary>
    internal static string Window(DateTimeOffset since, DateTimeOffset? until) =>
        until is { } end ? $"since {Time(since)} until {Time(end)} UTC" : $"since {Time(since)} UTC";

    /// <summary>
    /// The line under a truncated log lookup's table (task 038): how far back the answer reached, and the window that
    /// lists what came before it.
    /// </summary>
    internal static string Truncated(string what, int count, DateTimeOffset since, DateTimeOffset? oldest)
    {
        if (oldest is not { } reached)
        {
            return $"more {what} match: --limit raises the cap, --days, --since or --until narrows the window";
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "stopped at the --limit of {0}: the oldest {1} listed is from {2} UTC; --since {3} --until {4} lists from there back",
            count,
            what,
            Time(reached),
            AuditWindow.Literal(since),
            AuditWindow.Literal(reached));
    }

    /// <summary>A moment in a table: UTC to the second.</summary>
    internal static string Time(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>A cell cut to a width, with an ellipsis; <c>-</c> for nothing.</summary>
    internal static string Cut(string? text, int width)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "-";
        }

        string line = text.ReplaceLineEndings(" ").Trim();
        return line.Length > width ? string.Concat(line.AsSpan(0, width - 1), "…") : line;
    }
}
