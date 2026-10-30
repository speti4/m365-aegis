using System.Text.Json;
using System.Text.Json.Nodes;
using M365Aegis.Core.Graph;
using M365Aegis.Tests;

namespace M365Aegis.Core.Tests.Groups;

/// <summary>
/// Answers each <c>$batch</c> call from a recorded batch answer under tests/fixtures/batches (task 058), the next one
/// of a queue: a recorded answer of twenty parts, out of order as Graph writes them, whose part <c>n</c> answers the
/// call's <c>n</c>-th item, under that item's id. The batch call's own answer carries a made-up request id per call
/// (<c>…021</c>, <c>…022</c>, …). Every other request, and a batch call once the queue is empty, goes to the recorded
/// answers of tests/fixtures/graph.
/// </summary>
internal sealed class RecordedBatchTransport(params string[] answers) : IGraphTransport
{
    private readonly FixtureGraphTransport _fixtures = new(GraphFixtures.Root);
    private readonly Queue<string> _answers = new(answers);
    private readonly List<GraphHttpRequest> _requests = [];

    /// <summary>The folder of the recorded batch answers: the repo's tests/fixtures/batches.</summary>
    internal static string Folder { get; } = Path.Combine(Path.GetDirectoryName(GraphFixtures.Root)!, "batches");

    /// <summary>Every request sent, in order.</summary>
    internal IReadOnlyList<GraphHttpRequest> Requests => _requests;

    /// <summary>The ids of the items of each batch call, in order.</summary>
    internal List<IReadOnlyList<string>> Batches { get; } = [];

    public async Task<GraphResponse> SendAsync(GraphHttpRequest request, CancellationToken cancellationToken)
    {
        _requests.Add(request);
        if (request.Method != HttpMethod.Post || !request.Uri.AbsolutePath.EndsWith("/$batch", StringComparison.Ordinal) || _answers.Count == 0)
        {
            return await _fixtures.SendAsync(request, cancellationToken);
        }

        List<string> ids = JsonNode.Parse(request.Body!)!["requests"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).ToList();
        Batches.Add(ids);
        JsonNode recorded = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(Folder, _answers.Dequeue()), cancellationToken))!;
        var parts = new JsonArray();
        foreach (JsonNode? part in recorded["responses"]!.AsArray())
        {
            int position = int.Parse(part!["id"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture) - 1;
            if (position >= ids.Count)
            {
                continue;
            }

            JsonNode copy = part.DeepClone();
            copy["id"] = ids[position];
            parts.Add(copy);
        }

        string requestId = $"00000000-0000-0000-0000-0000000000{0x20 + Batches.Count:x2}";
        return new GraphResponse(200, [new("request-id", requestId)], new JsonObject { ["responses"] = parts }.ToJsonString(new JsonSerializerOptions()));
    }
}
