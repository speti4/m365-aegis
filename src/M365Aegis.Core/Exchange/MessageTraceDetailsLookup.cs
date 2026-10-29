using System.Globalization;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Exchange;

/// <summary>What <c>trace details</c> was asked for.</summary>
/// <param name="Message">
/// The trace id of a <c>trace</c> row (a GUID), or the message's Message-ID header (<c>&lt;…@contoso.com&gt;</c>, the
/// angle brackets optional).
/// </param>
/// <param name="Recipient">The recipient whose delivery steps to list, or null for every recipient of the message.</param>
public sealed record MessageTraceDetailsQuery(string Message, string? Recipient);

/// <summary>The result of <c>trace details</c>: the steps of one message, per recipient.</summary>
/// <param name="MessageId">The Message-ID header, when the trace rows named it.</param>
/// <param name="Subject">The subject, when the trace rows named it.</param>
/// <param name="Recipients">Each recipient's delivery steps, by address.</param>
public sealed record MessageTraceDetails(string? MessageId, string? Subject, IReadOnlyList<MessageTraceRecipient> Recipients);

/// <summary>The delivery steps of a message to one recipient.</summary>
/// <param name="TraceId">The trace id.</param>
/// <param name="Recipient">The recipient's address.</param>
/// <param name="Status">The trace row's status (delivered, failed, …), or null when the row was not read.</param>
/// <param name="Events">The steps, oldest first.</param>
public sealed record MessageTraceRecipient(string TraceId, string Recipient, string? Status, IReadOnlyList<MessageTraceEvent> Events);

/// <summary>One step of a message's way through Exchange Online (an <c>exchangeMessageTraceDetail</c>).</summary>
/// <param name="Time">When it happened (UTC).</param>
/// <param name="Event">The event: Receive, Submit, Deliver, Fail, Defer, Expand, Transfer, …</param>
/// <param name="Action">The action taken, or null.</param>
/// <param name="Description">What happened, in the service's words.</param>
/// <param name="Data">The event's extra data, an XML string, or null.</param>
public sealed record MessageTraceEvent(DateTimeOffset Time, string? Event, string? Action, string? Description, string? Data);

/// <summary>
/// <c>trace details &lt;trace id | Message-ID&gt; [--recipient &lt;address&gt;]</c> (task 034): the delivery steps of one
/// message per recipient, from <c>GET /admin/exchange/tracing/messageTraces/{id}/getDetailsByRecipient(recipientAddress='…')</c>,
/// which takes the trace id and one recipient. A trace id with <c>--recipient</c> is one request; otherwise the
/// message's trace rows come first (<c>$filter=id eq …</c> or <c>messageId eq …</c>, which the service answers over
/// its default window of the last 48 hours) and name the trace id and the recipients, at most
/// <see cref="MaxRecipients"/> of them, whose steps are asked for side by side. The service allows 100 requests per
/// 5 minutes per tenant; the permission and the setup are the trace's.
/// </summary>
public static class MessageTraceDetailsLookup
{
    /// <summary>The most recipients whose steps one command asks for; a wider message needs <c>--recipient</c>.</summary>
    public const int MaxRecipients = 10;

    /// <summary>Runs the lookup.</summary>
    /// <exception cref="AegisException">A usage error, no such message, or the failure of a request.</exception>
    public static async Task<CommandResult<MessageTraceDetails>> RunAsync(GraphClient graph, MessageTraceDetailsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(query);
        string message = (query.Message ?? "").Trim();
        if (message.Length == 0)
        {
            throw new AegisException(FailureKind.Usage, "no message was named", "give the trace id of a `trace` row, or the message's Message-ID");
        }

        string? recipient = string.IsNullOrWhiteSpace(query.Recipient) ? null : query.Recipient.Trim();
        bool traceId = Guid.TryParse(message, out _);
        try
        {
            string? messageId = null;
            string? subject = null;
            List<(string TraceId, string Recipient, string? Status)> targets;
            if (traceId && recipient is not null)
            {
                targets = [(message, recipient, null)];
            }
            else
            {
                string filter = traceId ? $"id eq {GraphQuery.Literal(message)}" : $"messageId eq {GraphQuery.Literal(MessageId(message))}";
                GraphRequest rows = GraphRequest.Get(GraphQuery.Path("/admin/exchange/tracing/messageTraces", ("$filter", filter))) with { Permission = MessageTraceLookup.Permission };
                IReadOnlyList<MessageTraceLookup.GraphMessageTrace> traces = await graph.ListAsync<MessageTraceLookup.GraphMessageTrace>(rows, cancellationToken).ConfigureAwait(false);
                List<MessageTraceLookup.GraphMessageTrace> matching = traces
                    .Where(trace => trace.RecipientAddress is not null
                        && (recipient is null || string.Equals(trace.RecipientAddress, recipient, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                if (matching.Count == 0)
                {
                    throw NotFound(message, recipient, traceId);
                }

                messageId = matching.Select(trace => trace.MessageId).FirstOrDefault(id => !string.IsNullOrEmpty(id));
                subject = matching.Select(trace => trace.Subject).FirstOrDefault(text => !string.IsNullOrEmpty(text));
                targets = matching
                    .DistinctBy(trace => (trace.Id, trace.RecipientAddress!.ToLowerInvariant()))
                    .Select(trace => (trace.Id, trace.RecipientAddress!, trace.Status))
                    .ToList();
                if (targets.Count > MaxRecipients)
                {
                    throw new AegisException(
                        FailureKind.Usage,
                        $"the message went to {targets.Count.ToString(CultureInfo.InvariantCulture)} recipients, more than the {MaxRecipients.ToString(CultureInfo.InvariantCulture)} one command asks about",
                        "give --recipient for the one whose steps you need; `trace` lists the recipients");
                }
            }

            MessageTraceRecipient[] recipients = await Task.WhenAll(targets.Select(target => StepsAsync(graph, target, cancellationToken))).ConfigureAwait(false);
            return CommandResult.Success(new MessageTraceDetails(
                messageId,
                subject,
                recipients.OrderBy(item => item.Recipient, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.TraceId, StringComparer.Ordinal).ToList()));
        }
        catch (AegisException exception) when (MessageTraceLookup.IsSetupFailure(exception))
        {
            throw MessageTraceLookup.SetupFailure(exception);
        }
    }

    /// <summary>The request of one recipient's steps: the function with the address as its parameter.</summary>
    internal static GraphRequest Request(string traceId, string recipient)
    {
        string address = Uri.EscapeDataString(recipient.Replace("'", "''", StringComparison.Ordinal));
        string path = $"/admin/exchange/tracing/messageTraces/{Uri.EscapeDataString(traceId)}/getDetailsByRecipient(recipientAddress='{address}')";
        return GraphRequest.Get(path) with { Permission = MessageTraceLookup.Permission };
    }

    /// <summary>A Message-ID as the trace keeps it, in angle brackets.</summary>
    internal static string MessageId(string value) =>
        value.StartsWith('<') && value.EndsWith('>') ? value : $"<{value.Trim('<', '>')}>";

    private static async Task<MessageTraceRecipient> StepsAsync(GraphClient graph, (string TraceId, string Recipient, string? Status) target, CancellationToken cancellationToken)
    {
        IReadOnlyList<GraphMessageTraceDetail> details = await graph.ListAsync<GraphMessageTraceDetail>(Request(target.TraceId, target.Recipient), cancellationToken).ConfigureAwait(false);
        List<MessageTraceEvent> events = details
            .Select(detail => new MessageTraceEvent(
                detail.DateTime ?? default,
                Blank(detail.Event),
                Blank(detail.Action),
                Blank(detail.Description),
                Blank(detail.Data)))
            .OrderBy(step => step.Time)
            .ToList();
        return new MessageTraceRecipient(target.TraceId, target.Recipient, target.Status, events);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static AegisException NotFound(string message, string? recipient, bool traceId)
    {
        string what = traceId ? $"trace id {message}" : $"Message-ID {MessageId(message)}";
        string to = recipient is null ? "" : $" to {recipient}";
        return new AegisException(
            FailureKind.NotFound,
            $"no message with the {what}{to} in the trace's last 48 hours",
            "the lookup by id covers the service's default window of 48 hours; for an older message give the trace id from `trace --until …` with --recipient");
    }

    /// <summary>A step as the service returns it (the <c>exchangeMessageTraceDetail</c> resource).</summary>
    private sealed record GraphMessageTraceDetail : GraphObject
    {
        public string? Id { get; init; }

        public string? MessageId { get; init; }

        public DateTimeOffset? DateTime { get; init; }

        public string? Event { get; init; }

        public string? Action { get; init; }

        public string? Description { get; init; }

        public string? Data { get; init; }
    }
}
