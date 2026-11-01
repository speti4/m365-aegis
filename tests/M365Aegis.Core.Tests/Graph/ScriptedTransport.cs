using M365Aegis.Core.Graph;

namespace M365Aegis.Core.Tests.Graph;

/// <summary>A transport whose answers a test computes from the request; every request is recorded (under a lock: the take's reads run several at once).</summary>
internal sealed class ScriptedTransport(Func<GraphHttpRequest, GraphResponse> answer) : IGraphTransport
{
    private readonly Lock _lock = new();

    internal List<GraphHttpRequest> Requests { get; } = [];

    public Task<GraphResponse> SendAsync(GraphHttpRequest request, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            Requests.Add(request);
        }

        return Task.FromResult(answer(request));
    }
}
