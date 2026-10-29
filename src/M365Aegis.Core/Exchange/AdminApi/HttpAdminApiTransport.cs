using System.Net.Http.Headers;
using System.Text;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Exchange.AdminApi;

/// <summary>
/// The transport of the Exchange Online Admin API backend (task 042): <see cref="HttpClient"/> with the bearer token
/// of <c>https://outlook.office365.com/.default</c> (the same token the sidecar connects with, carrying
/// <c>Exchange.ManageV2</c>), a <c>client-request-id</c> per request and the product's user agent; the request's own
/// headers (<c>X-AnchorMailbox</c>) are added as given. The token is sent to <see cref="Host"/> only. It implements
/// the Graph client's transport seam, so the tests answer the backend from the same fixture folder
/// (<see cref="FixtureGraphTransport"/>).
/// </summary>
public sealed class HttpAdminApiTransport : IGraphTransport
{
    /// <summary>The one host the token is sent to (the commercial cloud's).</summary>
    public const string Host = FixtureGraphTransport.AdminApiHost;

    private static readonly HttpClient SharedClient = new(new SocketsHttpHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    });

    private readonly HttpClient _client;
    private readonly Func<CancellationToken, Task<string>> _accessToken;

    /// <summary>A transport that asks <paramref name="accessToken"/> for the token before each request.</summary>
    /// <param name="accessToken">Returns the bearer token; an <see cref="AegisException"/> from it passes through.</param>
    /// <param name="handler">The HTTP handler; null for the shared one (the tests pass a scripted handler).</param>
    public HttpAdminApiTransport(Func<CancellationToken, Task<string>> accessToken, HttpMessageHandler? handler = null)
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
            throw new InvalidOperationException($"the Admin API transport sends the token to https://{Host} only, not to {request.Uri.GetLeftPart(UriPartial.Authority)}");
        }

        string token = await _accessToken(cancellationToken).ConfigureAwait(false);
        using var message = new HttpRequestMessage(request.Method, request.Uri);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
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
                $"the Exchange Online Admin API could not be reached: {exception.Message}",
                "check the network connection and the proxy, then run the command again",
                exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AegisException(
                FailureKind.ServiceError,
                $"the Exchange Online Admin API did not answer within {_client.Timeout.TotalSeconds:0} s",
                "run the command again",
                exception);
        }
    }
}
