using System.Net;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Tests.Graph;

/// <summary>The HTTP transport over a scripted handler: the headers it sends, the token's one host, the failures.</summary>
public sealed class HttpGraphTransportTests
{
    private static readonly IReadOnlyDictionary<string, string> Eventual = new Dictionary<string, string> { ["ConsistencyLevel"] = "eventual" };

    [Fact]
    public async Task TheTokenIsAskedForOnceAndSentAsABearer()
    {
        int tokens = 0;
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"id\":\"00000000-0000-0000-0000-000000000001\"}"),
            Headers = { { "request-id", "00000000-0000-0000-0000-0000000000f1" } },
        });
        var transport = new HttpGraphTransport(_ => { tokens++; return Task.FromResult("token-of-the-tests"); }, handler);
        var request = new GraphHttpRequest(HttpMethod.Get, new Uri("https://graph.microsoft.com/v1.0/me"), Eventual, null);

        GraphResponse first = await transport.SendAsync(request, CancellationToken.None);
        await transport.SendAsync(request, CancellationToken.None);

        Assert.Equal(1, tokens);
        Assert.Equal(200, first.StatusCode);
        Assert.Equal("00000000-0000-0000-0000-0000000000f1", first.RequestId);
        HttpRequestMessage sent = handler.Requests[0];
        Assert.Equal("Bearer", sent.Headers.Authorization?.Scheme);
        Assert.Equal("token-of-the-tests", sent.Headers.Authorization?.Parameter);
        Assert.Equal("eventual", Assert.Single(sent.Headers.GetValues("ConsistencyLevel")));
        Assert.True(Guid.TryParse(Assert.Single(sent.Headers.GetValues("client-request-id")), out _));
        Assert.Contains(sent.Headers.UserAgent, product => product.Product?.Name == "m365-aegis");
    }

    [Fact]
    public async Task JsonIsAcceptedUnlessTheRequestNamesAnotherType()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("2117") });
        var transport = new HttpGraphTransport(_ => Task.FromResult("token"), handler);
        var plain = new Dictionary<string, string> { ["Accept"] = "text/plain", ["ConsistencyLevel"] = "eventual" };

        await transport.SendAsync(new GraphHttpRequest(HttpMethod.Get, new Uri("https://graph.microsoft.com/v1.0/me"), Eventual, null), CancellationToken.None);
        GraphResponse count = await transport.SendAsync(new GraphHttpRequest(HttpMethod.Get, new Uri("https://graph.microsoft.com/v1.0/groups/$count"), plain, null), CancellationToken.None);

        Assert.Equal("application/json", Assert.Single(handler.Requests[0].Headers.Accept).MediaType);
        Assert.Equal("text/plain", Assert.Single(handler.Requests[1].Headers.Accept).MediaType);
        Assert.Equal("2117", count.Body);
    }

    [Fact]
    public async Task ABodyIsSentAsJson()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}") });
        var transport = new HttpGraphTransport(_ => Task.FromResult("token"), handler);

        GraphResponse response = await transport.SendAsync(
            new GraphHttpRequest(HttpMethod.Post, new Uri("https://graph.microsoft.com/v1.0/groups"), new Dictionary<string, string>(), "{\"displayName\":\"Finance\"}"),
            CancellationToken.None);

        Assert.Equal(201, response.StatusCode);
        Assert.Equal("application/json", handler.Requests[0].Content?.Headers.ContentType?.MediaType);
        Assert.Equal("{\"displayName\":\"Finance\"}", handler.Bodies[0]);
    }

    [Fact]
    public async Task TheTokenNeverGoesToAnotherHost()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        bool asked = false;
        var transport = new HttpGraphTransport(_ => { asked = true; return Task.FromResult("token"); }, handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendAsync(
            new GraphHttpRequest(HttpMethod.Get, new Uri("https://contoso.com/v1.0/me"), new Dictionary<string, string>(), null),
            CancellationToken.None));

        Assert.False(asked);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NoNetworkIsAServiceError()
    {
        var handler = new Handler(_ => throw new HttpRequestException("No such host is known."));
        var transport = new HttpGraphTransport(_ => Task.FromResult("token"), handler);

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => transport.SendAsync(
            new GraphHttpRequest(HttpMethod.Get, new Uri("https://graph.microsoft.com/v1.0/me"), new Dictionary<string, string>(), null),
            CancellationToken.None));

        Assert.Equal(ExitCode.ServiceError, exception.Failure.ExitCode);
    }

    [Fact]
    public async Task NoSignInPassesThroughAsAnAuthFailure()
    {
        var transport = new HttpGraphTransport(
            _ => throw new AegisException(FailureKind.Auth, "no silent sign-in", "run `m365-aegis login`"),
            new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        AegisException exception = await Assert.ThrowsAsync<AegisException>(() => transport.SendAsync(
            new GraphHttpRequest(HttpMethod.Get, new Uri("https://graph.microsoft.com/v1.0/me"), new Dictionary<string, string>(), null),
            CancellationToken.None));

        Assert.Equal(ExitCode.Auth, exception.Failure.ExitCode);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        internal List<HttpRequestMessage> Requests { get; } = [];

        internal List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            return answer(request);
        }
    }
}
