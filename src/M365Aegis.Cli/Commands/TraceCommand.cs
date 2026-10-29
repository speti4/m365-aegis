using System.CommandLine;
using System.Globalization;
using M365Aegis.Cli.Output;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli.Commands;

/// <summary>
/// <c>m365-aegis trace --sender &lt;address&gt; | --recipient &lt;address&gt; [--days N] [--until &lt;time&gt;]
/// [--status &lt;status&gt;] [--subject &lt;text&gt;]</c> (task 007; the status and subject filters task 035): the
/// message trace through Graph, at most 10 days per window and 90 days back; a wider window is a
/// usage error with a hint. The result is <see cref="MessageTrace"/>, the table <see cref="TraceTableView"/>.
/// <c>trace details &lt;trace id | Message-ID&gt; [--recipient &lt;address&gt;]</c> (task 034) lists one message's
/// delivery steps per recipient: <see cref="MessageTraceDetails"/>, the table <see cref="TraceDetailsTableView"/>.
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
        Option<string?> status = new("--status")
        {
            Description = $"Only the messages with this delivery status: {string.Join(", ", MessageTraceLookup.Statuses)}.",
        };
        Option<string?> subject = new("--subject")
        {
            Description = "Only the messages whose subject contains this text.",
        };
        Command command = new("trace", "Trace the messages a sender sent or a recipient received.");
        command.Options.Add(sender);
        command.Options.Add(recipient);
        command.Options.Add(days);
        command.Options.Add(until);
        command.Options.Add(status);
        command.Options.Add(subject);
        command.SetAction((parsed, cancellationToken) => GraphRunner.RunAsync(
            parsed, json, table, profile,
            (graph, ct) => MessageTraceLookup.RunAsync(
                graph,
                new MessageTraceQuery(
                    parsed.GetValue(sender),
                    parsed.GetValue(recipient),
                    parsed.GetValue(days),
                    ParseUntil(parsed.GetValue(until)),
                    parsed.GetValue(status),
                    parsed.GetValue(subject)),
                DateTimeOffset.UtcNow,
                ct),
            TraceTableView.Instance,
            cancellationToken));
        command.Subcommands.Add(Details(json, table, profile));
        return command;
    }

    private static Command Details(Option<bool> json, Option<bool> table, Option<string?> profile)
    {
        Argument<string> message = new("message")
        {
            Description = "The trace id of a `trace` row, or the message's Message-ID header.",
        };
        Option<string?> recipient = new("--recipient")
        {
            Description = $"The recipient whose delivery steps to list (every recipient, up to {MessageTraceDetailsLookup.MaxRecipients}, when omitted).",
        };
        Command details = new("details", "List the delivery steps of one message, per recipient.");
        details.Arguments.Add(message);
        details.Options.Add(recipient);
        details.SetAction((parsed, cancellationToken) => GraphRunner.RunAsync(
            parsed, json, table, profile,
            (graph, ct) => MessageTraceDetailsLookup.RunAsync(graph, new MessageTraceDetailsQuery(parsed.GetValue(message) ?? "", parsed.GetValue(recipient)), ct),
            TraceDetailsTableView.Instance,
            cancellationToken));
        return details;
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
        if (data.FilteredBy is { } where)
        {
            var filters = new List<string>();
            if (data.Status is not null)
            {
                filters.Add($"status {data.Status}");
            }

            if (data.Subject is not null)
            {
                filters.Add($"subject containing '{data.Subject}'");
            }

            yield return where == "service"
                ? $"filtered by the service: {string.Join(", ", filters)}"
                : $"filtered here: {string.Join(", ", filters)} (the service refused the filter, so the whole window was traced)";
        }
    }
}

/// <summary>
/// The table of <c>trace details</c>: a row per step and recipient, oldest first per recipient, the message and each
/// recipient's status under the table.
/// </summary>
internal sealed class TraceDetailsTableView : ITableView<MessageTraceDetails>
{
    internal static TraceDetailsTableView Instance { get; } = new();

    private TraceDetailsTableView()
    {
    }

    public IReadOnlyList<string> Headers => ["time (UTC)", "recipient", "event", "action", "detail"];

    public IEnumerable<IReadOnlyList<string>> Rows(MessageTraceDetails data) => data.Recipients.SelectMany(recipient => recipient.Events.Select(step => (IReadOnlyList<string>)
    [
        step.Time.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        recipient.Recipient,
        step.Event ?? "-",
        step.Action ?? "-",
        AuditOptions.Cut(step.Description, 80),
    ]));

    public IEnumerable<string> Footer(MessageTraceDetails data)
    {
        int steps = data.Recipients.Sum(recipient => recipient.Events.Count);
        string message = data.MessageId ?? data.Recipients.Select(recipient => recipient.TraceId).FirstOrDefault() ?? "-";
        string subject = data.Subject is null ? "" : $" ('{data.Subject}')";
        yield return string.Format(CultureInfo.InvariantCulture, "{0} step(s) of {1}{2} to {3} recipient(s)", steps, message, subject, data.Recipients.Count);
        foreach (MessageTraceRecipient recipient in data.Recipients)
        {
            string status = recipient.Status is null ? "" : $"{recipient.Status}, ";
            yield return $"{recipient.Recipient}: {status}trace id {recipient.TraceId}";
        }
    }
}
