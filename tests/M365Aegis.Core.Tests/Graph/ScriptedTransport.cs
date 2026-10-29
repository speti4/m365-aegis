using M365Aegis.Core.Graph;

namespace M365Aegis.Core.Tests.Graph;

/// <summary>A transport whose answers a test computes from the request; every request is recorded.</summary>
internal sealed class ScriptedTransport(Func<GraphHttpRequest, GraphResponse> answer) : IGraphTransport
{
    internal List<GraphHttpRequest> Requests { get; } = [];

    public Task<GraphResponse> SendAsync(GraphHttpRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(answer(request));
    }
}
