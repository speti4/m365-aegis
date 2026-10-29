using System.Globalization;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Exchange;

/// <summary>What <c>trace</c> was asked for.</summary>
/// <param name="Sender">The sender's address, or null.</param>
/// <param name="Recipient">The recipient's address, or null; at least one of the two is given.</param>
/// <param name="Days">The length of the window in days, 1 to 10; null for 2 (the service's own default).</param>
/// <param name="Until">The end of the window; null for now.</param>
/// <param name="Status">Only the messages with this delivery status (task 035), or null.</param>
/// <param name="Subject">Only the messages whose subject contains this text (task 035), or null.</param>
public sealed record MessageTraceQuery(string? Sender, string? Recipient, int? Days, DateTimeOffset? Until, string? Status = null, string? Subject = null);

/// <summary>One message of a trace, per recipient.</summary>
/// <param name="Received">When Exchange Online received it (UTC).</param>
/// <param name="Status">delivered, pending, failed, expanded, quarantined, filteredAsSpam, gettingStatus, …</param>
/// <param name="Sender">The sender's address.</param>
/// <param name="Recipient">The recipient's address.</param>
/// <param name="Subject">The subject.</param>
/// <param name="Size">The size in bytes.</param>
/// <param name="MessageId">The Message-ID header.</param>
/// <param name="Id">The trace id, which the details lookup takes.</param>
/// <param name="FromIp">The sending server's address.</param>
/// <param name="ToIp">The receiving server's address; empty for a message to Exchange Online.</param>
public sealed record MessageTraceEntry(
    DateTimeOffset Received,
    string? Status,
    string? Sender,
    string? Recipient,
    string? Subject,
    long? Size,
    string? MessageId,
    string Id,
    string? FromIp,
    string? ToIp);

/// <summary>The result of <c>trace</c>: the window that was asked and the messages in it, oldest first.</summary>
/// <param name="From">The start of the window (UTC).</param>
/// <param name="Until">The end of the window (UTC).</param>
/// <param name="Messages">The messages, oldest first.</param>
/// <param name="Status">The status filter, as the service names it (<c>failed</c>, <c>filteredAsSpam</c>), or null.</param>
/// <param name="Subject">The subject filter, or null.</param>
/// <param name="FilteredBy">
/// Where the status and subject filters were applied: <c>service</c> (the <c>$filter</c>, the documented way) or
/// <c>client</c> (the service refused the terms with a 400 and the window was traced without them); null without
/// either filter.
/// </param>
public sealed record MessageTrace(
    DateTimeOffset From,
    DateTimeOffset Until,
    IReadOnlyList<MessageTraceEntry> Messages,
    string? Status = null,
    string? Subject = null,
    string? FilteredBy = null);

/// <summary>
/// The message trace through Graph v1.0 (task 007; D-004: the first mailbox question Graph answers, no sidecar):
/// <c>GET /admin/exchange/tracing/messageTraces</c> with a <c>receivedDateTime</c> window and the sender, the
/// recipient or both. The service answers a window of at most 10 days, starting at most 90 days back and ending
/// no later than now; a wider window is refused here as a usage error with a hint, never sent and answered empty.
/// The service allows 100 requests per 5 minutes per tenant, so the pages are as large as it allows (5000) and the
/// client's throttling budget does the rest. The permission is <c>ExchangeMessageTrace.Read.All</c>, and the
/// tenant needs the service principal of the trace's Microsoft application once (docs/development.md).
/// </summary>
public static class MessageTraceLookup
{
    /// <summary>The permission the trace needs.</summary>
    public const string Permission = "ExchangeMessageTrace.Read.All";

    /// <summary>The widest window the service answers, in days.</summary>
    public const int MaxDays = 10;

    /// <summary>How far back the service keeps the trace, in days.</summary>
    public const int RetentionDays = 90;

    /// <summary>The window when none is given: the service's own default of 48 hours.</summary>
    public const int DefaultDays = 2;

    private const string ProvisioningHint =
        "the tenant needs, once, the service principal of the message trace application and the delegated permission " + Permission +
        " with admin consent (docs/development.md, \"The sign-in and the home folder\"); provisioning can take several hours, then run `m365-aegis login` for a fresh token";

    /// <summary>The query as the request's window and filter, or a usage error.</summary>
    internal static (DateTimeOffset From, DateTimeOffset Until, string Filter) Describe(MessageTraceQuery query, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(query);
        string? sender = string.IsNullOrWhiteSpace(query.Sender) ? null : query.Sender.Trim();
        string? recipient = string.IsNullOrWhiteSpace(query.Recipient) ? null : query.Recipient.Trim();
        if (sender is null && recipient is null)
        {
            throw new AegisException(FailureKind.Usage, "a trace needs --sender, --recipient or both", "name the address the message came from or went to");
        }

        int days = query.Days ?? DefaultDays;
        if (days < 1)
        {
            throw new AegisException(FailureKind.Usage, "--days is at least 1", $"give 1 to {MaxDays} days");
        }

        if (days > MaxDays)
        {
            throw new AegisException(
                FailureKind.Usage,
                $"the message trace answers a window of at most {MaxDays} days, not {days.ToString(CultureInfo.InvariantCulture)}",
                $"trace {MaxDays} days or fewer at a time; --until moves the window back (up to {RetentionDays} days), so a longer span is several traces");
        }

        DateTimeOffset current = Truncate(now);
        DateTimeOffset until = query.Until is { } given ? Truncate(given) : current;
        if (until > current)
        {
            throw new AegisException(FailureKind.Usage, "--until is in the future", "give a time up to now, or leave --until out for now");
        }

        DateTimeOffset from = until.AddDays(-days);
        if (from < current.AddDays(-RetentionDays))
        {
            throw new AegisException(
                FailureKind.Usage,
                $"the message trace keeps {RetentionDays} days, and the window starts {Format(from)}",
                $"move --until or narrow --days so the window starts after {Format(current.AddDays(-RetentionDays))}");
        }

        var clauses = new List<string>
        {
            $"receivedDateTime ge {Format(from)}",
            $"receivedDateTime le {Format(until)}",
        };
        if (sender is not null)
        {
            clauses.Add($"senderAddress eq {GraphQuery.Literal(sender)}");
        }

        if (recipient is not null)
        {
            clauses.Add($"recipientAddress eq {GraphQuery.Literal(recipient)}");
        }

        return (from, until, string.Join(" and ", clauses));
    }

    /// <summary>The delivery statuses the trace reports, as the service writes them (<c>--status</c> takes any case).</summary>
    public static IReadOnlyList<string> Statuses { get; } =
        ["delivered", "failed", "pending", "expanded", "quarantined", "filteredAsSpam", "gettingStatus", "none"];

    /// <summary>
    /// The <c>--status</c> value as the service writes it (<c>FAILED</c> and <c>filtered-as-spam</c> are
    /// <c>failed</c> and <c>filteredAsSpam</c>); null for null.
    /// </summary>
    /// <exception cref="AegisException">A usage failure for a status the trace does not report.</exception>
    public static string? Status(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string compact = value.Trim().Replace("-", "", StringComparison.Ordinal);
        return Statuses.FirstOrDefault(status => string.Equals(status, compact, StringComparison.OrdinalIgnoreCase))
            ?? throw new AegisException(FailureKind.Usage, $"--status '{value}' is not a delivery status of the trace", $"give one of {string.Join(", ", Statuses)}");
    }

    /// <summary>
    /// The status and subject terms of the <c>$filter</c> (<c>status eq '…'</c>, <c>contains(subject,'…')</c>, both
    /// documented filters of the trace), or null without either.
    /// </summary>
    internal static string? Narrowing(string? status, string? subject)
    {
        var clauses = new List<string>();
        if (status is not null)
        {
            clauses.Add($"status eq {GraphQuery.Literal(status)}");
        }

        if (subject is not null)
        {
            clauses.Add($"contains(subject,{GraphQuery.Literal(subject)})");
        }

        return clauses.Count == 0 ? null : string.Join(" and ", clauses);
    }

    /// <summary>
    /// Runs the trace; <paramref name="now"/> is the clock (the tests fix it). A status or subject filter goes into
    /// the <c>$filter</c>; should the service refuse it (400), the window is traced again without it and the filter is
    /// applied here, so a refused filter is never the answer, and the result says which way it went.
    /// </summary>
    public static async Task<CommandResult<MessageTrace>> RunAsync(GraphClient graph, MessageTraceQuery query, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(query);
        (DateTimeOffset from, DateTimeOffset until, string filter) = Describe(query, now);
        string? status = Status(query.Status);
        string? subject = string.IsNullOrWhiteSpace(query.Subject) ? null : query.Subject.Trim();
        string? narrowing = Narrowing(status, subject);
        string? filteredBy = null;
        IReadOnlyList<GraphMessageTrace> traces;
        try
        {
            if (narrowing is null)
            {
                traces = await ListAsync(graph, filter, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                try
                {
                    traces = await ListAsync(graph, $"{filter} and {narrowing}", cancellationToken).ConfigureAwait(false);
                    filteredBy = "service";
                }
                catch (AegisException exception) when (exception.HttpStatus == 400)
                {
                    traces = (await ListAsync(graph, filter, cancellationToken).ConfigureAwait(false))
                        .Where(trace => status is null || string.Equals(trace.Status, status, StringComparison.OrdinalIgnoreCase))
                        .Where(trace => subject is null || (trace.Subject ?? "").Contains(subject, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    filteredBy = "client";
                }
            }
        }
        catch (AegisException exception) when (IsSetupFailure(exception))
        {
            throw SetupFailure(exception);
        }

        IReadOnlyList<MessageTraceEntry> messages = traces
            .Select(trace => new MessageTraceEntry(
                trace.ReceivedDateTime ?? default,
                trace.Status,
                trace.SenderAddress,
                trace.RecipientAddress,
                trace.Subject,
                trace.Size,
                trace.MessageId,
                trace.Id,
                trace.FromIP,
                trace.ToIP))
            .OrderBy(message => message.Received)
            .ThenBy(message => message.Recipient, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return CommandResult.Success(new MessageTrace(from, until, messages, status, subject, filteredBy));
    }

    private static Task<IReadOnlyList<GraphMessageTrace>> ListAsync(GraphClient graph, string filter, CancellationToken cancellationToken)
    {
        string path = GraphQuery.Path("/admin/exchange/tracing/messageTraces", ("$filter", filter), ("$top", "5000"));
        return graph.ListAsync<GraphMessageTrace>(GraphRequest.Get(path) with { Permission = Permission }, cancellationToken);
    }

    /// <summary>
    /// True for a failure that is the tenant's setup: a 403, or the 401 that names the missing service principal
    /// (while it is missing or still provisioning); that is a setup step, not a sign-in to repeat.
    /// </summary>
    internal static bool IsSetupFailure(AegisException exception) =>
        exception.Failure.Kind is FailureKind.Forbidden
        || (exception.Failure.Kind == FailureKind.Auth && exception.Message.Contains("service principal", StringComparison.OrdinalIgnoreCase));

    /// <summary>The setup failure as exit code 8 with the consent and the provisioning step as the hint.</summary>
    internal static AegisException SetupFailure(AegisException exception) =>
        new(FailureKind.Forbidden, exception.Message, ProvisioningHint, exception);

    private static DateTimeOffset Truncate(DateTimeOffset time)
    {
        DateTimeOffset utc = time.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, TimeSpan.Zero);
    }

    /// <summary>The ISO 8601 form the service asks for: <c>YYYY-MM-DDThh:mm:ssZ</c>.</summary>
    private static string Format(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>A trace as the service returns it.</summary>
    internal sealed record GraphMessageTrace : GraphObject
    {
        public string Id { get; init; } = "";

        public DateTimeOffset? ReceivedDateTime { get; init; }

        public string? Status { get; init; }

        public string? SenderAddress { get; init; }

        public string? RecipientAddress { get; init; }

        public string? Subject { get; init; }

        public long? Size { get; init; }

        public string? MessageId { get; init; }

        public string? FromIP { get; init; }

        public string? ToIP { get; init; }
    }
}
