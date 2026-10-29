using System.CommandLine;
using System.Globalization;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli.Commands;

/// <summary>
/// <c>m365-aegis trace --sender &lt;address&gt; | --recipient &lt;address&gt; [--days N] [--until &lt;time&gt;]</c>
/// (task 007): the message trace through Graph, at most 10 days per window and 90 days back; a wider window is a
/// usage error with a hint. The result is <see cref="MessageTrace"/>, the table <see cref="TraceTableView"/>.
/// </summary>
internal static class TraceCommand
{
    internal static Command Build(Option<bool> json, Option<bool> table, Option<string?> profile)
    {
        Option<string?> sender = new("--sender") { Description = "The address the message came from." };
        Option<string?> recipient = new("--recipient") { Description = "The address the message went to." };
        Option<int?> days = new("--days")
        {
            Description = $"The window in days, 1 to {MessageTraceLookup.MaxDays} (default {MessageTraceLookup.DefaultDays}).",
        };
        Option<string?> until = new("--until")
        {
            Description = $"The end of the window, an ISO 8601 date or time (UTC unless it names an offset); now when omitted. At most {MessageTraceLookup.RetentionDays} days back.",
        };
        Command command = new("trace", "Trace the messages a sender sent or a recipient received.");
        command.Options.Add(sender);
        command.Options.Add(recipient);
        command.Options.Add(days);
        command.Options.Add(until);
        command.SetAction((parsed, cancellationToken) => GraphRunner.RunAsync(
            parsed, json, table, profile,
            (graph, ct) => MessageTraceLookup.RunAsync(
                graph,
                new MessageTraceQuery(parsed.GetValue(sender), parsed.GetValue(recipient), parsed.GetValue(days), ParseUntil(parsed.GetValue(until))),
                DateTimeOffset.UtcNow,
                ct),
            TraceTableView.Instance,
            cancellationToken));
        return command;
    }

    /// <summary>The <c>--until</c> value; a time without an offset is UTC.</summary>
    internal static DateTimeOffset? ParseUntil(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed))
        {
            return parsed;
        }

        throw new AegisException(FailureKind.Usage, $"'{value}' is not a date or time for --until", "give an ISO 8601 date or time, such as 2026-10-20 or 2026-10-20T14:30:00Z");
    }
}

/// <summary>The table of <c>trace</c>: a row per message and recipient, oldest first, the window under the table.</summary>
internal sealed class TraceTableView : ITableView<MessageTrace>
{
    internal static TraceTableView Instance { get; } = new();

    private TraceTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["received (UTC)", "status", "sender", "recipient", "subject"];

    public IEnumerable<IReadOnlyList<string>> Rows(MessageTrace data) => data.Messages.Select(message => (IReadOnlyList<string>)
    [
        message.Received.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        message.Status ?? "-",
        message.Sender ?? "-",
        message.Recipient ?? "-",
        message.Subject ?? "-",
    ]);

    public IEnumerable<string> Footer(MessageTrace data)
    {
        yield return string.Format(
            CultureInfo.InvariantCulture,
            "{0} message(s) received between {1:yyyy-MM-dd HH:mm} and {2:yyyy-MM-dd HH:mm} UTC",
            data.Messages.Count,
            data.From.UtcDateTime,
            data.Until.UtcDateTime);
    }
}
