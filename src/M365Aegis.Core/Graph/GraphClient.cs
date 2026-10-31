using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Graph;

/// <summary>How the client waits when Graph throttles it; the tests replace the clock and the dice.</summary>
public sealed record GraphClientOptions
{
    /// <summary>The retries of one request after a 429 or 503 (4 by default).</summary>
    public int MaxRetries { get; init; } = 4;

    /// <summary>
    /// The longest the client waits in all over its lifetime, which is one command (60 s by default): a
    /// <c>Retry-After</c> that would go past it fails the command with <see cref="FailureKind.Throttled"/> at once
    /// instead of keeping a terminal waiting.
    /// </summary>
    public TimeSpan MaxTotalDelay { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Waits; <see cref="Task.Delay(TimeSpan, CancellationToken)"/> by default.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    /// <summary>A number in [0, 1) for the jitter; <see cref="Random.Shared"/> by default.</summary>
    public Func<double> Jitter { get; init; } = Random.Shared.NextDouble;
}

/// <summary>
/// The Microsoft Graph client of the core (D-002: <see cref="HttpClient"/> and System.Text.Json, no SDK; D-006:
/// paging, throttling, batching and the version live here, never in a command). A lookup describes a request as a
/// <see cref="GraphRequest"/> and reads the answer typed (<see cref="GetAsync{T}"/>), as a stream of items over
/// every page (<see cref="GetAllAsync{T}"/>), or as up to twenty requests in one round trip
/// (<see cref="BatchAsync"/>). Under all of them: a 429 or 503 is retried after its <c>Retry-After</c> (or an
/// exponential back-off) with jitter, within <see cref="GraphClientOptions"/>; every answer's <c>request-id</c>
/// goes into the <see cref="RequestLog"/> the envelope's meta reads; a failed answer becomes an
/// <see cref="AegisException"/> of the contract (401 auth, 403 forbidden, 404 not found, 429/503 throttled, the
/// rest a service error). One client serves one command.
/// </summary>
public sealed class GraphClient
{
    /// <summary>The service root; a request's path goes under its version.</summary>
    public const string BaseUrl = "https://graph.microsoft.com";

    /// <summary>The most requests one <c>$batch</c> call carries; <see cref="BatchAsync"/> splits a longer list.</summary>
    public const int MaxBatchSize = 20;

    private static readonly IReadOnlyDictionary<string, string> NoHeaders = new Dictionary<string, string>();
    private static readonly IReadOnlyDictionary<string, string> Eventual =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ConsistencyLevel"] = "eventual" };

    private readonly IGraphTransport _transport;
    private readonly GraphClientOptions _options;
    private readonly Lock _lock = new();
    private TimeSpan _waited;

    /// <summary>A client over a transport (<see cref="GraphConnection"/> makes the product's).</summary>
    /// <param name="transport">The network, or the fixtures.</param>
    /// <param name="requestLog">Where the request ids go; a new log when null.</param>
    /// <param name="options">The throttling budget; the defaults when null.</param>
    public GraphClient(IGraphTransport transport, RequestLog? requestLog = null, GraphClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
        RequestLog = requestLog ?? new RequestLog();
        _options = options ?? new GraphClientOptions();
    }

    /// <summary>The request ids of every answer so far.</summary>
    public RequestLog RequestLog { get; }

    /// <summary>
    /// A client over the same transport and request log with another throttling budget, a fresh one, and the same clock
    /// and jitter: the snapshot's take (task 087) is a long run that may wait where a command would fail.
    /// </summary>
    internal GraphClient WithBudget(int maxRetries, TimeSpan maxTotalDelay) =>
        new(_transport, RequestLog, _options with { MaxRetries = maxRetries, MaxTotalDelay = maxTotalDelay });

    /// <summary>How long the client has waited on throttling so far.</summary>
    public TimeSpan Waited
    {
        get
        {
            lock (_lock)
            {
                return _waited;
            }
        }
    }

    /// <summary>
    /// The absolute URL of a request: the path under the version, or the absolute Graph URL as it is.
    /// </summary>
    /// <exception cref="AegisException">A <see cref="FailureKind.Usage"/> failure for a path that is neither.</exception>
    public static Uri BuildUri(GraphRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string path = request.Path;
        if (path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            var absolute = new Uri(path);
            if (absolute.Scheme != Uri.UriSchemeHttps || !string.Equals(absolute.Host, HttpGraphTransport.Host, StringComparison.OrdinalIgnoreCase))
            {
                throw new AegisException(FailureKind.Usage, $"'{path}' is not a Microsoft Graph URL", $"give a path such as /me, or a URL under {BaseUrl}");
            }

            return absolute;
        }

        if (!path.StartsWith('/'))
        {
            throw new AegisException(FailureKind.Usage, $"'{path}' does not start with /", "give the path under the version, such as /me or /groups?$top=5");
        }

        return new Uri($"{BaseUrl}/{request.Version.Segment()}{path}");
    }

    /// <summary>
    /// Sends one request with the throttling retries and returns the successful answer as it came.
    /// </summary>
    /// <exception cref="AegisException">The answer was not a 2xx, mapped to the contract's failure.</exception>
    public async Task<GraphResponse> SendAsync(GraphRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var http = new GraphHttpRequest(request.Method, BuildUri(request), Headers(request), request.Body);
        GraphResponse response = await SendWithRetriesAsync(http, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            (string? code, string? message) = ReadError(response.Body);
            throw Failure(request, response.StatusCode, response.Headers, code, message);
        }

        return response;
    }

    /// <summary>Sends the request and reads the answer as <typeparamref name="T"/>; an empty answer is the default.</summary>
    public async Task<T?> GetAsync<T>(GraphRequest request, CancellationToken cancellationToken)
    {
        GraphResponse response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return Read<T>(request, response.Body);
    }

    /// <summary>
    /// Every page of a collection, following <c>@odata.nextLink</c> with the same headers (an advanced query's
    /// <c>ConsistencyLevel</c> included) until the last page.
    /// </summary>
    public async IAsyncEnumerable<GraphPage<T>> GetPagesAsync<T>(GraphRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        GraphRequest current = request;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            GraphPage<T> page = await GetAsync<GraphPage<T>>(current, cancellationToken).ConfigureAwait(false) ?? new GraphPage<T>();
            yield return page;
            // A next link seen before would loop forever; Graph never sends one, a broken fixture might.
            if (page.NextLink is null || !seen.Add(page.NextLink))
            {
                yield break;
            }

            current = new GraphRequest(HttpMethod.Get, page.NextLink)
            {
                Version = request.Version,
                EventualConsistency = request.EventualConsistency,
                Permission = request.Permission,
                Accept = request.Accept,
            };
        }
    }

    /// <summary>Every item of a collection over every page, as a stream.</summary>
    public async IAsyncEnumerable<T> GetAllAsync<T>(GraphRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (GraphPage<T> page in GetPagesAsync<T>(request, cancellationToken).ConfigureAwait(false))
        {
            foreach (T item in page.Value)
            {
                yield return item;
            }
        }
    }

    /// <summary>Every item of a collection over every page, as a list.</summary>
    public async Task<IReadOnlyList<T>> ListAsync<T>(GraphRequest request, CancellationToken cancellationToken)
    {
        var items = new List<T>();
        await foreach (T item in GetAllAsync<T>(request, cancellationToken).ConfigureAwait(false))
        {
            items.Add(item);
        }

        return items;
    }

    /// <summary>
    /// Sends the requests through <c>$batch</c>, twenty per call, and returns one answer per request in the order
    /// given. A request the service throttled inside a batch is sent again in a new batch after the longest
    /// <c>Retry-After</c>, within the same budget as a single request; an item that still failed comes back with its
    /// status, and <see cref="GraphBatchResponse.EnsureSuccess"/> turns it into the contract's failure. Every request
    /// must use the same version, and a path must be relative (or an absolute URL of that version).
    /// </summary>
    /// <exception cref="AegisException">The batch call itself failed.</exception>
    public async Task<IReadOnlyList<GraphBatchResponse>> BatchAsync(IReadOnlyList<GraphRequest> requests, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
        {
            return [];
        }

        GraphVersion version = requests[0].Version;
        if (requests.Any(request => request.Version != version))
        {
            throw new ArgumentException("every request of a batch uses the same Graph version", nameof(requests));
        }

        var results = new GraphBatchResponse?[requests.Count];
        for (int start = 0; start < requests.Count; start += MaxBatchSize)
        {
            List<int> pending = Enumerable.Range(start, Math.Min(MaxBatchSize, requests.Count - start)).ToList();
            int attempt = 0;
            while (pending.Count > 0)
            {
                IReadOnlyList<GraphBatchResponse> answers = await SendBatchAsync(requests, pending, version, cancellationToken).ConfigureAwait(false);
                var throttled = new List<int>();
                TimeSpan? longest = null;
                foreach (GraphBatchResponse answer in answers)
                {
                    int index = answer.Index;
                    results[index] = answer;
                    if (IsThrottled(answer.StatusCode))
                    {
                        TimeSpan? delay = NextDelay(answer.Headers, attempt);
                        if (delay is { } wait)
                        {
                            throttled.Add(index);
                            longest = longest is { } current && current > wait ? current : wait;
                        }
                    }
                }

                if (throttled.Count == 0 || longest is not { } pause || !TryReserve(pause))
                {
                    break;
                }

                await _options.Delay(pause, cancellationToken).ConfigureAwait(false);
                pending = throttled;
                attempt++;
            }
        }

        return results.Select((result, index) => result ?? throw new InvalidOperationException($"the batch returned no answer for request {index + 1}")).ToList();
    }

    private async Task<IReadOnlyList<GraphBatchResponse>> SendBatchAsync(
        IReadOnlyList<GraphRequest> requests, IReadOnlyList<int> indexes, GraphVersion version, CancellationToken cancellationToken)
    {
        var items = new JsonArray();
        foreach (int index in indexes)
        {
            GraphRequest request = requests[index];
            var item = new JsonObject
            {
                ["id"] = (index + 1).ToString(CultureInfo.InvariantCulture),
                ["method"] = request.Method.Method,
                ["url"] = RelativeUrl(request, version),
            };
            var headers = new JsonObject();
            if (request.EventualConsistency)
            {
                headers["ConsistencyLevel"] = "eventual";
            }

            if (request.Body is not null)
            {
                headers["Content-Type"] = "application/json";
                item["body"] = JsonNode.Parse(request.Body);
            }

            if (headers.Count > 0)
            {
                item["headers"] = headers;
            }

            items.Add(item);
        }

        string body = new JsonObject { ["requests"] = items }.ToJsonString();
        var batch = new GraphRequest(HttpMethod.Post, "/$batch") { Version = version, Body = body };
        GraphResponse response = await SendAsync(batch, cancellationToken).ConfigureAwait(false);
        BatchBody parsed = Read<BatchBody>(batch, response.Body) ?? new BatchBody([]);
        var answers = new List<GraphBatchResponse>();
        foreach (BatchItem item in parsed.Responses)
        {
            if (!int.TryParse(item.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id < 1 || id > requests.Count)
            {
                continue;
            }

            answers.Add(new GraphBatchResponse(id - 1, requests[id - 1], item.Status, item.Headers ?? [], item.Body));
        }

        return answers;
    }

    /// <summary>The URL of a batch item: the path under the version, without the service root.</summary>
    private static string RelativeUrl(GraphRequest request, GraphVersion version)
    {
        if (!request.Path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            _ = BuildUri(request);
            return request.Path;
        }

        string prefix = $"{BaseUrl}/{version.Segment()}";
        if (!request.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"a batch item's URL is not under {prefix}: {request.Path}", nameof(request));
        }

        return request.Path[prefix.Length..];
    }

    private async Task<GraphResponse> SendWithRetriesAsync(GraphHttpRequest request, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            GraphResponse response = await _transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
            RequestLog.Add(response.RequestId);
            if (!IsThrottled(response.StatusCode) || NextDelay(response.Headers, attempt) is not { } delay || !TryReserve(delay))
            {
                return response;
            }

            await _options.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsThrottled(int status) => status is 429 or 503;

    /// <summary>
    /// The headers a request adds: <c>ConsistencyLevel</c> for an advanced query, <c>Accept</c> and <c>Prefer</c> when it
    /// names them.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Headers(GraphRequest request)
    {
        if (request.Accept is null && request.Prefer is null)
        {
            return request.EventualConsistency ? Eventual : NoHeaders;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (request.Accept is not null)
        {
            headers["Accept"] = request.Accept;
        }

        if (request.Prefer is not null)
        {
            headers["Prefer"] = request.Prefer;
        }

        if (request.EventualConsistency)
        {
            headers["ConsistencyLevel"] = "eventual";
        }

        return headers;
    }

    /// <summary>The wait before the next try, or null when the retries of this request are spent.</summary>
    private TimeSpan? NextDelay(IReadOnlyDictionary<string, string> headers, int attempt)
    {
        if (attempt >= _options.MaxRetries)
        {
            return null;
        }

        if (RetryAfter(headers, DateTimeOffset.UtcNow) is { } after)
        {
            // The service's own figure, plus a little jitter so parallel callers do not come back in step.
            return after + TimeSpan.FromMilliseconds(250 * _options.Jitter());
        }

        // No Retry-After: an exponential back-off with jitter (1, 2, 4, 8 s, at most 30), as the guidance suggests.
        double seconds = Math.Min(30, Math.Pow(2, attempt));
        return TimeSpan.FromSeconds(seconds * (0.5 + (0.5 * _options.Jitter())));
    }

    /// <summary>Counts a wait against the budget; false when it would go past it.</summary>
    private bool TryReserve(TimeSpan delay)
    {
        lock (_lock)
        {
            if (_waited + delay > _options.MaxTotalDelay)
            {
                return false;
            }

            _waited += delay;
            return true;
        }
    }

    /// <summary>The <c>Retry-After</c> header as a span: seconds, or an HTTP date.</summary>
    internal static TimeSpan? RetryAfter(IReadOnlyDictionary<string, string> headers, DateTimeOffset now)
    {
        if (!headers.TryGetValue("Retry-After", out string? value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds))
        {
            return TimeSpan.FromSeconds(Math.Max(0, seconds));
        }

        if (DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset date))
        {
            TimeSpan span = date - now;
            return span > TimeSpan.Zero ? span : TimeSpan.Zero;
        }

        return null;
    }

    private static T? Read<T>(GraphRequest request, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, GraphJson.Options);
        }
        catch (JsonException exception)
        {
            throw new AegisException(
                FailureKind.ServiceError,
                $"the answer to {request.Method} {Describe(request)} is not the JSON the client expected: {exception.Message}",
                "run the command again; if it persists, report it with the request id",
                exception);
        }
    }

    /// <summary>The code and the message of an error body, when it has the Graph shape.</summary>
    internal static (string? Code, string? Message) ReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        try
        {
            GraphErrorBody? error = JsonSerializer.Deserialize<GraphErrorBody>(body, GraphJson.Options);
            return (error?.Error?.Code, error?.Error?.Message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    /// <summary>
    /// The contract's failure for a Graph answer that was not a 2xx (D-006): 401 auth, 403 forbidden with the
    /// permission in the hint, 404 not found, 429 and 503 throttled, everything else a service error.
    /// </summary>
    internal static AegisException Failure(GraphRequest request, int status, IReadOnlyDictionary<string, string> headers, string? code, string? message)
    {
        string what = $"{request.Method} {Describe(request)}";
        string detail = (code, message) switch
        {
            (null or "", null or "") => "",
            (null or "", _) => $": {message}",
            (_, null or "") => $": {code}",
            _ => $": {code}: {message}",
        };
        string sentence = $"Microsoft Graph answered {status.ToString(CultureInfo.InvariantCulture)} to {what}{detail}";
        AegisException failure = status switch
        {
            401 => new AegisException(FailureKind.Auth, sentence, "the token was refused; run `m365-aegis login` and try again"),
            403 => new AegisException(FailureKind.Forbidden, sentence, ForbiddenHint(request.Permission)),
            404 => new AegisException(FailureKind.NotFound, sentence, "check the id or the name; `m365-aegis call GET <path>` shows what Graph has"),
            429 or 503 => new AegisException(FailureKind.Throttled, sentence, ThrottledHint(headers)),
            >= 500 => new AegisException(FailureKind.ServiceError, sentence, "a service error on Microsoft's side; run the command again later"),
            _ => new AegisException(FailureKind.ServiceError, sentence, "Graph refused the request as it was written; check the path, the query and the body"),
        };
        return new AegisException(failure.Failure) { HttpStatus = status };
    }

    private static string ForbiddenHint(string? permission) => permission is null
        ? "the app registration lacks the delegated permission this call needs, or the account lacks the role; an administrator consents to it in the portal (docs/development.md, \"The sign-in and the home folder\"), then run `m365-aegis login` for a fresh token; `m365-aegis whoami` lists what the token carries"
        : $"the app registration needs the delegated permission {permission} with admin consent (docs/development.md, \"The sign-in and the home folder\"), or the account lacks the role; after the consent, run `m365-aegis login` for a fresh token; `m365-aegis whoami` lists what the token carries";

    private static string ThrottledHint(IReadOnlyDictionary<string, string> headers) =>
        RetryAfter(headers, DateTimeOffset.UtcNow) is { } after
            ? $"Graph asked to wait {Math.Ceiling(after.TotalSeconds).ToString(CultureInfo.InvariantCulture)} s, longer than the client's budget; run the command again after that"
            : "the retries ran out; run the command again in a minute";

    /// <summary>The request's path without the service root and the query, for a sentence.</summary>
    private static string Describe(GraphRequest request)
    {
        string path = request.Path;
        if (path.StartsWith(BaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            path = path[BaseUrl.Length..];
            int slash = path.IndexOf('/', 1);
            path = slash > 0 ? path[slash..] : path;
        }

        int query = path.IndexOf('?', StringComparison.Ordinal);
        return query >= 0 ? path[..query] : path;
    }

    private sealed record BatchBody(IReadOnlyList<BatchItem> Responses);

    private sealed record BatchItem(string Id, int Status, Dictionary<string, string>? Headers, JsonElement? Body);
}

/// <summary>One answer of a <c>$batch</c> call, next to the request it answers.</summary>
public sealed class GraphBatchResponse
{
    internal GraphBatchResponse(int index, GraphRequest request, int statusCode, IReadOnlyDictionary<string, string> headers, JsonElement? body)
    {
        Index = index;
        Request = request;
        StatusCode = statusCode;
        Headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
        Body = body is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } ? body : null;
    }

    /// <summary>The position of the request in the list given to <see cref="GraphClient.BatchAsync"/>.</summary>
    public int Index { get; }

    /// <summary>The request this answers.</summary>
    public GraphRequest Request { get; }

    /// <summary>The HTTP status of this request inside the batch.</summary>
    public int StatusCode { get; }

    /// <summary>This request's response headers, case-insensitive.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>The body, or null when there is none.</summary>
    public JsonElement? Body { get; }

    /// <summary>True for a 2xx status.</summary>
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    /// <summary>The body as <typeparamref name="T"/>; the default when there is none.</summary>
    public T? Deserialize<T>() => Body is { } body ? body.Deserialize<T>(GraphJson.Options) : default;

    /// <summary>Throws the contract's failure when this request failed.</summary>
    /// <exception cref="AegisException">The status was not a 2xx.</exception>
    public void EnsureSuccess()
    {
        if (IsSuccess)
        {
            return;
        }

        (string? code, string? message) = GraphClient.ReadError(Body?.GetRawText() ?? "");
        throw GraphClient.Failure(Request, StatusCode, Headers, code, message);
    }
}
