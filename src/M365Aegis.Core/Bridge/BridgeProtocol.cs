using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using M365Aegis.Core.Output;

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
    public static BridgeMessage Reply(BridgeMessage request, string type, JsonNode? data)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new(Version, request.Id, type, data);
    }

    /// <summary>
    /// The answer to a request with a result of the core as its payload, written the way the envelope writes its
    /// data (D-006: camelCase, nulls left out, enums as kebab-case strings), so the window reads the same shape a
    /// script reads from <c>--json</c>.
    /// </summary>
    public static BridgeMessage Reply<T>(BridgeMessage request, string type, T data) =>
        Reply(request, type, JsonSerializer.SerializeToNode(data, EnvelopeJson.Options));

    /// <summary>The <c>error</c> answer to a request: the failure's code, message and hint, as the envelope's error.</summary>
    public static BridgeMessage Error(BridgeMessage request, AegisFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return Reply(request, BridgeMessageType.Error, failure.ToError());
    }
}

/// <summary>
/// The message types of the protocol. The front end sends <see cref="Ready"/> (a notification) and the requests
/// <see cref="Ping"/>, <see cref="WhoAmI"/> and <see cref="GroupMembers"/>; the host answers each request with one
/// reply of the type named here, or with <see cref="Error"/>. gui/web/src/bridge/protocol.ts names the same
/// strings, and the samples under tests/contracts/bridge hold one message of each.
/// </summary>
public static class BridgeMessageType
{
    /// <summary>The page is loaded and listening (a notification; the smoke run waits for it).</summary>
    public const string Ready = "ready";

    /// <summary>Asks for the product and the version; answered with <see cref="Pong"/>.</summary>
    public const string Ping = "ping";

    /// <summary>The answer to <see cref="Ping"/>.</summary>
    public const string Pong = "pong";

    /// <summary>
    /// Asks for the sign-in state, silently as the command line's <c>whoami</c>; answered with
    /// <see cref="SignedIn"/> or <see cref="SignedOut"/>.
    /// </summary>
    public const string WhoAmI = "whoami";

    /// <summary>The signed-in account: a <see cref="Auth.SignInInfo"/> as the payload.</summary>
    public const string SignedIn = "signed-in";

    /// <summary>No usable silent sign-in: a <see cref="SignedOutInfo"/> as the payload.</summary>
    public const string SignedOut = "signed-out";

    /// <summary>
    /// Asks for a group's direct members as <c>group members</c> does; the payload is a
    /// <see cref="GroupMembersRequest"/>. Answered with <see cref="Members"/>.
    /// </summary>
    public const string GroupMembers = "group-members";

    /// <summary>The members of a group: a <see cref="Groups.GroupMembers"/> as the payload.</summary>
    public const string Members = "members";

    /// <summary>A request that failed: an <see cref="EnvelopeError"/> (code, message, hint) as the payload.</summary>
    public const string Error = "error";
}

/// <summary>The payload of a <see cref="BridgeMessageType.GroupMembers"/> request.</summary>
/// <param name="Group">The group: an object id, an exact display name or address, or a word of the name.</param>
public sealed record GroupMembersRequest(string? Group);

/// <summary>The payload of a <see cref="BridgeMessageType.SignedOut"/> reply.</summary>
/// <param name="Profile">The tenant profile the sign-in was tried for, or null when there is none.</param>
/// <param name="Account">The profile's account, or null when there is no profile.</param>
/// <param name="Message">Why there is no sign-in, one sentence.</param>
/// <param name="Hint">What to do about it, or null.</param>
public sealed record SignedOutInfo(string? Profile, string? Account, string Message, string? Hint);
