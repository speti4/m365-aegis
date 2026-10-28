using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace M365Aegis.Core.Bridge;

/// <summary>
/// A message between the window's front end and the host, both ways: one JSON object per message, tagged with the
/// protocol version so an unrelated string on the channel is refused. A request carries an <c>id</c> the answer
/// repeats; a notification (such as <c>ready</c>) carries none. The sample messages under tests/contracts/bridge
/// are read by the C# and the TypeScript tests alike.
/// </summary>
/// <param name="Aegis">The protocol version; <see cref="BridgeProtocol.Version"/>.</param>
/// <param name="Id">The request id the answer repeats; null for a notification.</param>
/// <param name="Type">The message type: a verb from the front end, or the answer's kind.</param>
/// <param name="Data">The payload, if any.</param>
public sealed record BridgeMessage(int Aegis, string? Id, string Type, JsonNode? Data);

/// <summary>Parses and writes bridge messages.</summary>
public static class BridgeProtocol
{
    /// <summary>The protocol version both sides must carry.</summary>
    public const int Version = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>A message from its JSON text; null when the text is not a message of this protocol.</summary>
    public static BridgeMessage? TryParse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            BridgeMessage? message = JsonSerializer.Deserialize<BridgeMessage>(text, Options);
            return message is { Aegis: Version } && !string.IsNullOrEmpty(message.Type) ? message : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A message as one line of JSON.</summary>
    public static string Write(BridgeMessage message) => JsonSerializer.Serialize(message, Options);

    /// <summary>The answer to a request: the same id, the given type and payload.</summary>
    public static BridgeMessage Reply(BridgeMessage request, string type, JsonNode? data) =>
        new(Version, request.Id, type, data);
}

/// <summary>
/// Routes a front-end message to its handler. The scaffold answers <c>ping</c> with <c>pong</c> and the product
/// version; every later verb (the task cards of M4) registers here, and every write goes through a plan and a
/// confirmation the front end shows (D-006).
/// </summary>
public static class BridgeDispatcher
{
    /// <summary>The answer to a request, or null for a notification that needs none.</summary>
    public static BridgeMessage? Dispatch(BridgeMessage request)
    {
        switch (request.Type)
        {
            case "ping":
                var data = new JsonObject { ["product"] = ProductInfo.Name, ["version"] = ProductInfo.Version };
                return BridgeProtocol.Reply(request, "pong", data);
            case "ready":
                return null;
            default:
                var error = new JsonObject { ["code"] = "unknown-type", ["message"] = $"no handler for '{request.Type}'" };
                return BridgeProtocol.Reply(request, "error", error);
        }
    }
}
