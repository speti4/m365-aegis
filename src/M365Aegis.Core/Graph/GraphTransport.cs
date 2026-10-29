namespace M365Aegis.Core.Graph;

/// <summary>
/// The one seam between the Graph client and the network: an HTTP request goes in, the status, the headers and the
/// body come out. <see cref="HttpGraphTransport"/> sends it with the bearer token; <see cref="FixtureGraphTransport"/>
/// answers from the recorded fixtures, which is how every test runs offline. The client does everything else
/// (the URL, the retries, the paging, the batch, the errors), so it is the same code under both.
/// </summary>
public interface IGraphTransport
{
    /// <summary>Sends one request and returns the answer, whatever its status; throws only when nothing came back.</summary>
    Task<GraphResponse> SendAsync(GraphHttpRequest request, CancellationToken cancellationToken);
}

/// <summary>One HTTP request as the transport sends it.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Uri">The absolute URL, always under <c>https://graph.microsoft.com</c>.</param>
/// <param name="Headers">The headers beyond the token (<c>ConsistencyLevel</c>); the transport adds its own.</param>
/// <param name="Body">The JSON body, or null.</param>
public sealed record GraphHttpRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string? Body);

/// <summary>One HTTP answer: the status, the headers (looked up regardless of case) and the body as text.</summary>
public sealed class GraphResponse
{
    /// <summary>An answer; the headers are copied into a case-insensitive map.</summary>
    public GraphResponse(int statusCode, IEnumerable<KeyValuePair<string, string>> headers, string body)
    {
        ArgumentNullException.ThrowIfNull(headers);
        StatusCode = statusCode;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string value) in headers)
        {
            map[name] = value;
        }

        Headers = map;
        Body = body ?? "";
    }

    /// <summary>The HTTP status code.</summary>
    public int StatusCode { get; }

    /// <summary>The response headers, case-insensitive.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>The body as text; empty when there is none.</summary>
    public string Body { get; }

    /// <summary>True for a 2xx status.</summary>
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    /// <summary>The service's <c>request-id</c> header, which a support case names, or null.</summary>
    public string? RequestId => Headers.TryGetValue("request-id", out string? id) ? id : null;
}
