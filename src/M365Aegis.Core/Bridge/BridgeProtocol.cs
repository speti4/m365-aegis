using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using M365Aegis.Core.Operations;
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
/// Marks a request type of <see cref="BridgeMessageType"/> that plans a write (task 082): the host answers it with
/// <see cref="BridgeMessageType.Plan"/> and holds the plan until an <see cref="BridgeMessageType.ApplyPlan"/> with the
/// confirmation applies it. <see cref="BridgeMessageType.Writes"/> lists the marked types, which the write-guard test
/// of the bridge drives without a confirmation (task 079), so a new write request joins it by its mark.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class BridgeWriteAttribute : Attribute;

/// <summary>
/// The message types of the protocol. The front end sends <see cref="Ready"/> (a notification) and the requests
/// <see cref="Ping"/>, <see cref="WhoAmI"/>, <see cref="Login"/>, <see cref="OpenSignInPage"/>, <see cref="Logout"/>,
/// <see cref="GroupMembers"/>, <see cref="MailboxList"/>, <see cref="MailboxAccess"/>, <see cref="Access"/> and <see cref="SnapshotDiff"/>, the write requests
/// (<see cref="Writes"/>: <see cref="MailboxAccessGrant"/>, <see cref="MailboxAccessRevoke"/>,
/// <see cref="MailboxForwardingSet"/>, <see cref="MailboxForwardingClear"/>) and <see cref="ApplyPlan"/>; the host
/// answers each request with one reply of the type named here, or with <see cref="Error"/>, and sends
/// <see cref="DeviceCode"/> as a notification while a sign-in waits for the browser step.
/// gui/web/src/bridge/protocol.ts names the same strings, and the samples under tests/contracts/bridge hold one
/// message of each, in a file named after its type.
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

    /// <summary>
    /// Asks what one user reaches, from the newest snapshot of the window's profile, as <c>access &lt;user&gt;</c> reads
    /// it (task 102 over task 084's read): the local file only, no sign-in and no request to any service. The payload is
    /// an <see cref="AccessRequest"/>; answered with <see cref="AccessReport"/>, or with <see cref="NoSnapshot"/> when the
    /// profile has no snapshot yet.
    /// </summary>
    public const string Access = "access";

    /// <summary>What one user reaches: a <see cref="Snapshots.AccessReport"/> as the payload.</summary>
    public const string AccessReport = "access-report";

    /// <summary>
    /// The profile has no snapshot to answer <see cref="Access"/> from: a <see cref="NoSnapshotInfo"/> as the payload. A
    /// state of the card, as <see cref="SignedOut"/> is one of the sign-in card, not an error.
    /// </summary>
    public const string NoSnapshot = "no-snapshot";

    /// <summary>
    /// Asks what appeared and disappeared between two snapshots of the window's profile, as <c>snapshot diff</c> reads it
    /// (task 115 over task 098's read, and task 114's for one user): the local files only, no sign-in and no request to
    /// any service. The payload is a <see cref="SnapshotDiffRequest"/>, every field optional: the newest snapshot and the
    /// one before it by default. Answered with <see cref="SnapshotDiffReport"/>, or with <see cref="TooFewSnapshots"/>
    /// when there are not two to compare.
    /// </summary>
    public const string SnapshotDiff = "snapshot-diff";

    /// <summary>What appeared and disappeared: a <see cref="Snapshots.SnapshotDiffReport"/> as the payload.</summary>
    public const string SnapshotDiffReport = "snapshot-diff-report";

    /// <summary>
    /// The profile has fewer than two snapshots to compare (or none before the one named): a
    /// <see cref="TooFewSnapshotsInfo"/> as the payload. A state of the card, as <see cref="NoSnapshot"/> is one.
    /// </summary>
    public const string TooFewSnapshots = "too-few-snapshots";

    /// <summary>
    /// Plans a grant of mailbox rights as <c>mailbox access grant</c> does (task 082); the payload is a
    /// <see cref="MailboxAccessWriteRequest"/>. Answered with <see cref="Plan"/>.
    /// </summary>
    [BridgeWrite]
    public const string MailboxAccessGrant = "mailbox-access-grant";

    /// <summary>
    /// Plans a revoke of mailbox rights as <c>mailbox access revoke</c> does; the payload is a
    /// <see cref="MailboxAccessWriteRequest"/>, whose <c>noAutoMapping</c> is a grant's only. Answered with <see cref="Plan"/>.
    /// </summary>
    [BridgeWrite]
    public const string MailboxAccessRevoke = "mailbox-access-revoke";

    /// <summary>
    /// Plans a mailbox's forwarding as <c>mailbox forwarding set</c> does; the payload is a
    /// <see cref="MailboxForwardingWriteRequest"/>. Answered with <see cref="Plan"/>.
    /// </summary>
    [BridgeWrite]
    public const string MailboxForwardingSet = "mailbox-forwarding-set";

    /// <summary>
    /// Plans the end of a mailbox's forwarding as <c>mailbox forwarding clear</c> does; the payload is a
    /// <see cref="MailboxForwardingWriteRequest"/> with the mailbox alone. Answered with <see cref="Plan"/>.
    /// </summary>
    [BridgeWrite]
    public const string MailboxForwardingClear = "mailbox-forwarding-clear";

    /// <summary>
    /// The plan of a write request: a <see cref="PlanReady"/> as the payload, the <see cref="Operations.Plan"/> as
    /// <c>--dry-run</c> prints it (nothing sent, nothing recorded) and the id an <see cref="ApplyPlan"/> names; no id
    /// when there is nothing to change.
    /// </summary>
    public const string Plan = "plan";

    /// <summary>
    /// Applies a plan the host holds, as <c>--yes</c> does on the command line (task 082); the payload is an
    /// <see cref="ApplyPlanRequest"/>, whose <c>confirm</c> must be true: without it, or for a plan the host does not
    /// hold, nothing is sent. Answered with <see cref="PlanApplied"/>.
    /// </summary>
    public const string ApplyPlan = "apply-plan";

    /// <summary>
    /// The applied plan: a <see cref="Bridge.PlanApplied"/> as the payload, each change with its result and the plan
    /// record's file, and the failure when not every change was applied.
    /// </summary>
    public const string PlanApplied = "plan-applied";

    /// <summary>A request that failed: an <see cref="EnvelopeError"/> (code, message, hint) as the payload.</summary>
    public const string Error = "error";

    /// <summary>The write requests: the types marked <see cref="BridgeWriteAttribute"/>, in the order they are declared.</summary>
    public static IReadOnlyList<string> Writes { get; } = typeof(BridgeMessageType)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.IsDefined(typeof(BridgeWriteAttribute)))
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToList();
}

/// <summary>The payload of a <see cref="BridgeMessageType.GroupMembers"/> request.</summary>
/// <param name="Group">The group: an object id, an exact display name or address, or a word of the name.</param>
public sealed record GroupMembersRequest(string? Group);

/// <summary>The payload of an <see cref="BridgeMessageType.Access"/> request.</summary>
/// <param name="User">The user: a user principal name, an address or an object id.</param>
public sealed record AccessRequest(string? User);

/// <summary>The payload of a <see cref="BridgeMessageType.NoSnapshot"/> reply.</summary>
/// <param name="Profile">The tenant profile whose snapshots were looked for.</param>
/// <param name="Message">That there is none, one sentence.</param>
/// <param name="Hint">What to do about it, in the window's words.</param>
public sealed record NoSnapshotInfo(string Profile, string Message, string Hint);

/// <summary>The payload of a <see cref="BridgeMessageType.SnapshotDiff"/> request; a blank or missing field takes its default.</summary>
/// <param name="From">The first snapshot, picked as <c>snapshot diff</c> picks one; the one before <paramref name="To"/> when missing.</param>
/// <param name="To">The second snapshot; the newest when missing.</param>
/// <param name="User">One user's changes only (task 114): a user principal name, an address or an object id.</param>
public sealed record SnapshotDiffRequest(string? From, string? To, string? User);

/// <summary>The payload of a <see cref="BridgeMessageType.TooFewSnapshots"/> reply.</summary>
/// <param name="Profile">The tenant profile whose snapshots were looked for.</param>
/// <param name="Snapshots">How many readable snapshots the profile has on this computer.</param>
/// <param name="Message">Why there is nothing to compare, one sentence.</param>
/// <param name="Hint">What to do about it, in the window's words.</param>
public sealed record TooFewSnapshotsInfo(string Profile, int Snapshots, string Message, string Hint);

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

/// <summary>
/// The payload of <see cref="BridgeMessageType.MailboxAccessGrant"/> and
/// <see cref="BridgeMessageType.MailboxAccessRevoke"/>: the arguments of <c>mailbox access grant|revoke</c>.
/// </summary>
/// <param name="Mailbox">The mailbox: its address, name or object id.</param>
/// <param name="Users">The holders: user principal names, addresses or a group's address.</param>
/// <param name="Rights">The rights: <c>full-access</c>, <c>send-as</c>, <c>send-on-behalf</c>, or any spelling <c>--right</c> takes.</param>
/// <param name="NoAutoMapping">A grant's FullAccess without automapping (<c>--no-automapping</c>, task 059).</param>
public sealed record MailboxAccessWriteRequest(string? Mailbox, IReadOnlyList<string>? Users, IReadOnlyList<string>? Rights, bool NoAutoMapping);

/// <summary>
/// The payload of <see cref="BridgeMessageType.MailboxForwardingSet"/> and
/// <see cref="BridgeMessageType.MailboxForwardingClear"/>: the arguments of <c>mailbox forwarding set|clear</c>.
/// </summary>
/// <param name="Mailbox">The mailbox: its address, name or object id.</param>
/// <param name="To">The address to forward to; a set's only.</param>
/// <param name="KeepCopy">Whether the mailbox keeps a copy (<c>--keep-copy</c>); a set's only.</param>
public sealed record MailboxForwardingWriteRequest(string? Mailbox, string? To, bool KeepCopy);

/// <summary>The payload of an <see cref="BridgeMessageType.ApplyPlan"/> request.</summary>
/// <param name="PlanId">The plan's id, as its <see cref="BridgeMessageType.Plan"/> reply named it.</param>
/// <param name="Confirm">The confirmation, which stands for <c>--yes</c>: only <c>true</c> applies the plan.</param>
public sealed record ApplyPlanRequest(string? PlanId, bool Confirm);

/// <summary>The payload of a <see cref="BridgeMessageType.Plan"/> reply.</summary>
/// <param name="PlanId">The id an <see cref="BridgeMessageType.ApplyPlan"/> names; null when there is nothing to change.</param>
/// <param name="Plan">The plan as <c>--dry-run</c> prints it: <c>mode</c> is <c>dry-run</c>, or <c>up-to-date</c>.</param>
public sealed record PlanReady(string? PlanId, Plan Plan);

/// <summary>The payload of a <see cref="BridgeMessageType.PlanApplied"/> reply.</summary>
/// <param name="Plan">The applied plan: each change with its result, the record's file, the times.</param>
/// <param name="Error">
/// When not every change was applied, the failure the command line's envelope carries beside the plan (exit code 6, or
/// the one failure's own code); null when every change was applied.
/// </param>
public sealed record PlanApplied(Plan Plan, EnvelopeError? Error);
