using System.Net.Http.Headers;
using System.Text;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Graph;

/// <summary>
/// The transport of the product: <see cref="HttpClient"/> with the bearer token of the Graph resource (task 002), a
/// <c>client-request-id</c> per request and the product's user agent. The token is asked for once, on the first
/// request, so a command that fails before it calls Graph never touches the sign-in; it is sent to
/// <c>https://graph.microsoft.com</c> only.
/// </summary>
public sealed class HttpGraphTransport : IGraphTransport
{
    /// <summary>The one host the token is sent to.</summary>
    public const string Host = "graph.microsoft.com";

    // One client for the process: the connection pool is reused across the requests of a command, and the pooled
    // connections are recycled so a DNS change is picked up by a long-lived window.
    private static readonly HttpClient SharedClient = new(new SocketsHttpHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    });

    private readonly HttpClient _client;
    private readonly Func<CancellationToken, Task<string>> _accessToken;
    private readonly Lock _lock = new();
    private Task<string>? _token;

    /// <summary>A transport that asks <paramref name="accessToken"/> for the token (<c>AuthService.GetAccessTokenAsync</c>).</summary>
    /// <param name="accessToken">Returns the bearer token; an <see cref="AegisException"/> from it passes through.</param>
    /// <param name="handler">The HTTP handler; null for the shared one (the tests pass a scripted handler).</param>
    public HttpGraphTransport(Func<CancellationToken, Task<string>> accessToken, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(accessToken);
        _accessToken = accessToken;
        _client = handler is null ? SharedClient : new HttpClient(handler, disposeHandler: false);
    }

    /// <inheritdoc/>
    public async Task<GraphResponse> SendAsync(GraphHttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Uri.Scheme != Uri.UriSchemeHttps || !string.Equals(request.Uri.Host, Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"the Graph transport sends the token to https://{Host} only, not to {request.Uri.GetLeftPart(UriPartial.Authority)}");
        }

        // One token per command: requests sent side by side (a lookup's parallel queries) wait on the same one.
        Task<string> pending;
        lock (_lock)
        {
            pending = _token ??= _accessToken(cancellationToken);
        }

        string token = await pending.ConfigureAwait(false);

        using var message = new HttpRequestMessage(request.Method, request.Uri);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // JSON unless the request names another type (a raw $count answers text/plain only).
        if (!request.Headers.ContainsKey("Accept"))
        {
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        message.Headers.UserAgent.Add(new ProductInfoHeaderValue(ProductInfo.Name, ProductInfo.Version));
        message.Headers.TryAddWithoutValidation("client-request-id", Guid.NewGuid().ToString());
        foreach ((string name, string value) in request.Headers)
        {
            message.Headers.TryAddWithoutValidation(name, value);
        }

        if (request.Body is not null)
        {
            message.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
        }

        try
        {
            using HttpResponseMessage response = await _client.SendAsync(message, cancellationToken).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            IEnumerable<KeyValuePair<string, string>> headers = response.Headers
                .Concat(response.Content.Headers)
                .Select(header => new KeyValuePair<string, string>(header.Key, string.Join(", ", header.Value)));
            return new GraphResponse((int)response.StatusCode, headers, body);
        }
        catch (HttpRequestException exception)
        {
            throw new AegisException(
                FailureKind.ServiceError,
                $"Microsoft Graph could not be reached: {exception.Message}",
                "check the network connection and the proxy, then run the command again",
                exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AegisException(
                FailureKind.ServiceError,
                $"Microsoft Graph did not answer within {_client.Timeout.TotalSeconds:0} s",
                "run the command again; a large query may need a narrower filter",
                exception);
        }
    }
}
