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

    /// <summary>
    /// A notification from the host (no id, no answer expected) with a result of the core as its payload, written as
    /// <see cref="Reply{T}"/> writes one: the device code of a sign-in in flight (task 040).
    /// </summary>
    public static BridgeMessage Notification<T>(string type, T data) =>
        new(Version, null, type, JsonSerializer.SerializeToNode(data, EnvelopeJson.Options));

    /// <summary>The <c>error</c> answer to a request: the failure's code, message and hint, as the envelope's error.</summary>
    public static BridgeMessage Error(BridgeMessage request, AegisFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return Reply(request, BridgeMessageType.Error, failure.ToError());
    }
}

/// <summary>
/// The message types of the protocol. The front end sends <see cref="Ready"/> (a notification) and the requests
/// <see cref="Ping"/>, <see cref="WhoAmI"/>, <see cref="Login"/>, <see cref="OpenSignInPage"/>, <see cref="Logout"/>,
/// <see cref="GroupMembers"/>, <see cref="MailboxList"/> and <see cref="MailboxAccess"/>; the host answers each request with one reply of the
/// type named here, or with <see cref="Error"/>, and sends <see cref="DeviceCode"/> as a notification while a
/// sign-in waits for the browser step. gui/web/src/bridge/protocol.ts names the same strings, and the samples under
/// tests/contracts/bridge hold one message of each.
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
    /// Signs in as <c>m365-aegis login</c> does for the window's profile: silently when it can, else the broker's
    /// account picker over the window, else (or with <see cref="LoginRequest.DeviceCode"/>) a device code, which the
    /// host sends as a <see cref="DeviceCode"/> notification. Answered with <see cref="SignedIn"/> (task 040).
    /// </summary>
    public const string Login = "login";

    /// <summary>
    /// The page to open and the code to type for a device-code sign-in in flight: a notification from the host, a
    /// <see cref="Auth.DeviceCodePrompt"/> as the payload.
    /// </summary>
    public const string DeviceCode = "device-code";

    /// <summary>
    /// Opens the page of the device-code sign-in in flight in the default browser (task 047): no payload, since the
    /// host opens only the address its own <see cref="DeviceCode"/> notification carried, and only on a Microsoft
    /// sign-in host (<see cref="SignInPage"/>). Answered with <see cref="SignInPageOpened"/>.
    /// </summary>
    public const string OpenSignInPage = "open-sign-in-page";

    /// <summary>The answer to <see cref="OpenSignInPage"/>: a <see cref="SignInPageOpenedInfo"/> as the payload.</summary>
    public const string SignInPageOpened = "sign-in-page-opened";

    /// <summary>Signs the profile's account out as <c>m365-aegis logout</c> does; answered with <see cref="LoggedOut"/>.</summary>
    public const string Logout = "logout";

    /// <summary>The answer to <see cref="Logout"/>: a <see cref="Auth.SignOutInfo"/> as the payload.</summary>
    public const string LoggedOut = "logged-out";

    /// <summary>
    /// Asks for the mailboxes as <c>mailbox list [--shared]</c> does; the payload is a <see cref="MailboxListRequest"/>.
    /// Answered with <see cref="Mailboxes"/> (task 045).
    /// </summary>
    public const string MailboxList = "mailbox-list";

    /// <summary>The mailboxes: an <see cref="Exchange.MailboxList"/> as the payload.</summary>
    public const string Mailboxes = "mailboxes";

    /// <summary>
    /// Asks who holds which right on a mailbox, or which mailboxes a user holds a right on, as
    /// <c>mailbox access</c> does; the payload is a <see cref="MailboxAccessRequest"/>. Answered with
    /// <see cref="MailboxHolders"/> or <see cref="UserGrants"/> (task 045).
    /// </summary>
    public const string MailboxAccess = "mailbox-access";

    /// <summary>
    /// Who holds which right on a mailbox: an <see cref="Exchange.MailboxAccess"/> as the payload, read over the window's
    /// layers as <c>mailbox access &lt;mailbox&gt;</c> reads it, the mailbox and each grant with the layer that read it
    /// (task 063).
    /// </summary>
    public const string MailboxHolders = "mailbox-holders";

    /// <summary>
    /// The mailboxes a user holds a right on: an <see cref="Exchange.UserAccess"/> as the payload; a partial answer
    /// (some lists could not be read) is this reply with its <c>failed</c> list, as the envelope carries the data of
    /// exit code 6.
    /// </summary>
    public const string UserGrants = "user-grants";

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

/// <summary>The payload of a <see cref="BridgeMessageType.Login"/> request; none is a sign-in through the broker.</summary>
/// <param name="DeviceCode">Skip the broker and sign in through a browser with a device code.</param>
public sealed record LoginRequest(bool DeviceCode);

/// <summary>The payload of a <see cref="BridgeMessageType.MailboxList"/> request.</summary>
/// <param name="Shared">Only the shared mailboxes.</param>
public sealed record MailboxListRequest(bool Shared);

/// <summary>The payload of a <see cref="BridgeMessageType.MailboxAccess"/> request: one of the two, never both.</summary>
/// <param name="Mailbox">The mailbox (its address, name or object id): who holds which right on it.</param>
/// <param name="User">A user principal name: which mailboxes the user holds a right on.</param>
public sealed record MailboxAccessRequest(string? Mailbox, string? User);

/// <summary>The payload of a <see cref="BridgeMessageType.SignInPageOpened"/> reply.</summary>
/// <param name="VerificationUri">The page the default browser was asked to open.</param>
public sealed record SignInPageOpenedInfo(Uri VerificationUri);

/// <summary>The payload of a <see cref="BridgeMessageType.SignedOut"/> reply.</summary>
/// <param name="Profile">The tenant profile the sign-in was tried for, or null when there is none.</param>
/// <param name="Account">The profile's account, or null when there is no profile.</param>
/// <param name="Message">Why there is no sign-in, one sentence.</param>
/// <param name="Hint">What to do about it, or null.</param>
public sealed record SignedOutInfo(string? Profile, string? Account, string Message, string? Hint);
