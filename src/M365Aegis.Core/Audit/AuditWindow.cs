using System.Globalization;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Audit;

/// <summary>
/// The time window of a log lookup (<c>signin list</c>, <c>audit list</c>): the last <c>--days N</c> (1 by
/// default, at most the 30 days the logs keep), or from an explicit <c>--since</c> moment; never both; up to now, or
/// to an <c>--until</c> moment, which a truncated answer's footer names to continue from. The bounds are cut to whole
/// seconds and written as the OData date-time literal the logs' <c>$filter</c> takes.
/// </summary>
public static class AuditWindow
{
    /// <summary>The days a lookup reaches back without <c>--days</c> or <c>--since</c>.</summary>
    public const int DefaultDays = 1;

    /// <summary>The longest window: the sign-in and audit logs keep 30 days at most.</summary>
    public const int MaxDays = 30;

    /// <summary>The start of the window.</summary>
    /// <param name="days">The last N days, or null.</param>
    /// <param name="since">The explicit start, or null.</param>
    /// <param name="now">The current moment.</param>
    /// <exception cref="AegisException">A usage failure: both given, a day count out of range, a start in the future.</exception>
    public static DateTimeOffset Start(int? days, DateTimeOffset? since, DateTimeOffset now)
    {
        if (days is not null && since is not null)
        {
            throw new AegisException(FailureKind.Usage, "--days and --since both name the start of the window", "give one of them");
        }

        DateTimeOffset start;
        if (since is { } explicitStart)
        {
            if (explicitStart > now)
            {
                throw new AegisException(FailureKind.Usage, $"--since {Literal(explicitStart)} is in the future", "give a moment in the past, such as 2026-10-01 or 2026-10-01T08:00:00Z");
            }

            start = explicitStart;
        }
        else
        {
            int count = days ?? DefaultDays;
            if (count is < 1 or > MaxDays)
            {
                throw new AegisException(
                    FailureKind.Usage,
                    $"--days {count.ToString(CultureInfo.InvariantCulture)} is outside 1 to {MaxDays.ToString(CultureInfo.InvariantCulture)}",
                    "the logs keep 30 days at most; give a number from 1 to 30");
            }

            start = now.AddDays(-count);
        }

        return Whole(start);
    }

    /// <summary>
    /// The window of a log lookup with an optional end (<c>--until</c>, tasks 037 and 038): <c>--days N</c> counts back
    /// from the end when there is one (as <c>trace</c> does), <c>--since</c> names the start; the end is cut to whole
    /// seconds, no later than now and after the start. Without <paramref name="until"/> the end is null: the
    /// window runs to now and the request names no end.
    /// </summary>
    /// <exception cref="AegisException">A usage failure: the rules of <see cref="Start"/>, an end in the future or not after the start.</exception>
    public static (DateTimeOffset Start, DateTimeOffset? End) Window(int? days, DateTimeOffset? since, DateTimeOffset? until, DateTimeOffset now)
    {
        if (until is not { } given)
        {
            return (Start(days, since, now), null);
        }

        DateTimeOffset end = Whole(given);
        if (end > now)
        {
            throw new AegisException(FailureKind.Usage, $"--until {Literal(end)} is in the future", "give a moment in the past, or leave --until out for now");
        }

        if (since is { } explicitStart && explicitStart > end)
        {
            throw new AegisException(FailureKind.Usage, $"--since {Literal(explicitStart)} is after --until {Literal(end)}", "give a start before the end");
        }

        return (Start(days, since, end), end);
    }

    /// <summary>A moment cut to whole seconds, in UTC.</summary>
    private static DateTimeOffset Whole(DateTimeOffset value)
    {
        DateTime utc = value.UtcDateTime;
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }

    /// <summary>
    /// The <c>--since</c> value: a date (<c>2026-10-01</c>, midnight UTC) or a date and time, UTC unless it names
    /// an offset (<c>2026-10-01T08:00:00+02:00</c>); null for null.
    /// </summary>
    /// <exception cref="AegisException">A usage failure for a value that is not a date.</exception>
    public static DateTimeOffset? ParseSince(string? value) => Parse(value, "--since");

    /// <summary>The <c>--until</c> value, read as <see cref="ParseSince"/> reads <c>--since</c>; null for null.</summary>
    /// <exception cref="AegisException">A usage failure for a value that is not a date.</exception>
    public static DateTimeOffset? ParseUntil(string? value) => Parse(value, "--until");

    private static DateTimeOffset? Parse(string? value, string option)
    {
        if (value is null)
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset moment))
        {
            return moment;
        }

        throw new AegisException(FailureKind.Usage, $"{option} '{value}' is not a date", "give a date or a date and time, such as 2026-10-01 or 2026-10-01T08:00:00Z (UTC unless an offset is named)");
    }

    /// <summary>A moment as an OData date-time literal in UTC: <c>2026-10-28T09:00:00Z</c>.</summary>
    public static string Literal(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Checks the cap on the items a log lookup returns.</summary>
    /// <exception cref="AegisException">A usage failure for a cap below 1.</exception>
    internal static void CheckLimit(int limit)
    {
        if (limit < 1)
        {
            throw new AegisException(FailureKind.Usage, $"--limit {limit.ToString(CultureInfo.InvariantCulture)} is below 1", "give the most entries to return, such as 100");
        }
    }
}
