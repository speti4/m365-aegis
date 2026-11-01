using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Config;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Operations;
using M365Aegis.Core.Output;
using M365Aegis.Core.Snapshots;

namespace M365Aegis.Core.Bridge;

/// <summary>
/// What the window's requests run on: the same sign-in, Graph client and Exchange Online session the command line
/// uses, held for the life of the window. <see cref="ForWindow"/> wires the product; the tests give a sign-in over a
/// scripted broker, a client over the fixtures and a session over the fake module.
/// </summary>
/// <param name="Auth">The sign-in: <c>whoami</c> and the tokens silently, <c>login</c> and <c>logout</c> on request.</param>
/// <param name="Profile">The tenant profile, or null for the default one.</param>
/// <param name="Graph">A Graph client per request, writing its request ids into the given log.</param>
/// <param name="LogInternalError">Writes an unexpected exception to the log; the file, or null when it could not.</param>
public sealed record BridgeServices(
    AuthService Auth,
    string? Profile,
    Func<RequestLog, GraphClient> Graph,
    Func<Exception, string?> LogInternalError)
{
    /// <summary>
    /// The window's one Exchange Online session (task 045) and its layers over one token source (task 056), or null
    /// where the services have none (then a mailbox request answers exit code 11's failure). The window disposes it
    /// when it closes.
    /// </summary>
    public WindowExchange? Exchange { get; init; }

    /// <summary>
    /// Hands a device-code sign-in's page to the default browser (the host's <c>IUiHost.OpenInBrowser</c>, task 047),
    /// or null where there is no browser to hand it to (then the request answers that it cannot).
    /// </summary>
    public Func<Uri, Task>? OpenInBrowser { get; init; }

    /// <summary>
    /// The clock of the window's writes (task 082): when a plan was made, whether it is still fresh, and the times of
    /// its plan record. The system's in the window; a clock that stands still in the tests.
    /// </summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>
    /// The window's services over a home folder: MSAL through the Windows account manager with the window as the
    /// account picker's parent and the picker started on the window's thread (<paramref name="onWindowThread"/>),
    /// the cache, the profiles and the log of that home folder (shared with the command line), the Graph client of
    /// <see cref="GraphConnection"/> (the fixtures when <see cref="GraphConnection.FixturesEnvironmentVariable"/> is
    /// set), and one Exchange Online session for the window's lifetime over the same sign-in (the offline tests'
    /// organization when <see cref="ExchangeConnection.TestOrganizationVariable"/> is set, as on the command line),
    /// with the Admin API beside it over the same token source (task 056; the fixtures when
    /// <see cref="GraphConnection.FixturesEnvironmentVariable"/> is set, none with only the test organization).
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Wires the window's real layers: MSAL through the Windows account manager over the window, Graph and an Exchange Online session that a first cmdlet would start against the tenant; no test may run them, the windows job's --smoke run starts the window with it, and each layer it wires is measured on its own.")]
    public static BridgeServices ForWindow(string home, string? profile, Func<IntPtr> parentWindow, Func<Func<Task>, Task> onWindowThread)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        ArgumentNullException.ThrowIfNull(parentWindow);
        ArgumentNullException.ThrowIfNull(onWindowThread);
        // The window's own hints (task 046): a failed sign-in names the card's buttons, never a command to type.
        var auth = new AuthService(home, tenantProfile => MsalTokenBroker.Create(tenantProfile, home, parentWindow, onWindowThread), SignInHints.Window);
        // One set of session options per token source, as the command line's run has (task 077): the warm session and
        // the extra sidecars of a split read connect with its token and ask for the organization once.
        var sessionOptions = new ConditionalWeakTable<ExchangeTokenSource, ExchangeSessionOptions>();
        return new BridgeServices(
            auth,
            profile,
            requestLog => GraphConnection.Create(profile, requestLog, () => auth),
            exception => InternalError.WriteToLog(exception, home))
        {
            // The command line's layers over the window's own sign-in, one token source for the session and the Admin
            // API (task 056). The session's one Graph call (the organization's .onmicrosoft.com name) and the Admin
            // API's calls log their request ids into logs of their own: the window shows no meta, so nothing reads them.
            Exchange = new WindowExchange(
                () => ExchangeConnection.Tokens(profile, () => auth),
                tokens => new ExchangeSession(sessionOptions.GetValue(tokens, source => ExchangeConnection.Options(profile, new RequestLog(), () => auth, source))),
                tokens => AdminApiConnection.Create(profile, new RequestLog(), () => auth, tokens)),
        };
    }
}

/// <summary>
/// Routes a front-end request to the core and makes the reply: <c>ping</c>, the sign-in (<c>whoami</c>,
/// <c>login</c>, <c>logout</c>), a group's members (<c>group-members</c>), the mailbox lookups over the window's
/// Exchange Online session (<c>mailbox-list</c>, <c>mailbox-access</c>), what one user reaches from the newest snapshot
/// (<c>access</c>, task 102), the users whose name or address starts with a text, from the directory or else that
/// snapshot (<c>user-search</c>, task 112) and the mailbox writes (task 082), each
/// answered with its reply type or with <c>error</c>. A failure of the core becomes the <c>error</c> reply with the
/// contract's code, message and hint (D-006), the hint in the window's words where the core's names a command (task 057,
/// <see cref="SignInHints.ForSurface"/>); an unexpected exception becomes the <c>internal</c> error with the stack
/// trace in the log, as on the command line. A write request (<see cref="BridgeMessageType.Writes"/>) is planned over
/// the window's layers and printed by the write guard as <c>--dry-run</c> prints it; the dispatcher holds the plan, and
/// only an <c>apply-plan</c> that names it with <c>"confirm": true</c> applies it, once, through the guard, over the
/// window's warm session, with the plan record the command line writes. Nothing else sends a write.
/// </summary>
public sealed class BridgeDispatcher
{
    /// <summary>How long a plan waits for its confirmation: an older one is refused, and the card plans again.</summary>
    public static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(15);

    /// <summary>What a plan record names as the program that applied it.</summary>
    public const string RecordSource = "window";

    private readonly BridgeServices _services;

    /// <summary>The plans waiting for their confirmation, by id.</summary>
    private readonly ConcurrentDictionary<string, HeldPlan> _plans = new(StringComparer.Ordinal);

    /// <summary>The page of the device-code sign-in waiting for its browser step, or null when none waits.</summary>
    private volatile Uri? _signInPage;

    /// <summary>A dispatcher over the given services.</summary>
    public BridgeDispatcher(BridgeServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <summary>The answer to a request, or null for a notification that needs none. Never throws but on cancellation.</summary>
    public Task<BridgeMessage?> DispatchAsync(BridgeMessage request, CancellationToken cancellationToken) =>
        DispatchAsync(request, _ => Task.CompletedTask, cancellationToken);

    /// <summary>
    /// The answer to a request, or null for a notification that needs none; <paramref name="notify"/> sends the
    /// host's notifications while the request runs (the device code of a <c>login</c>). Never throws but on
    /// cancellation.
    /// </summary>
    public async Task<BridgeMessage?> DispatchAsync(BridgeMessage request, Func<BridgeMessage, Task> notify, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(notify);
        try
        {
            switch (request.Type)
            {
                case BridgeMessageType.Ready:
                    return null;
                case BridgeMessageType.Ping:
                    var data = new JsonObject { ["product"] = ProductInfo.Name, ["version"] = ProductInfo.Version };
                    return BridgeProtocol.Reply(request, BridgeMessageType.Pong, data);
                case BridgeMessageType.WhoAmI:
                    return await WhoAmIAsync(request, cancellationToken).ConfigureAwait(false);
                case BridgeMessageType.Login:
                    return await LoginAsync(request, notify, cancellationToken).ConfigureAwait(false);
                case BridgeMessageType.OpenSignInPage:
                    return await OpenSignInPageAsync(request).ConfigureAwait(false);
                case BridgeMessageType.Logout:
                    return await LogoutAsync(request, cancellationToken).ConfigureAwait(false);
                case BridgeMessageType.GroupMembers:
                    return await GroupMembersAsync(request, cancellationToken).ConfigureAwait(false);
                case BridgeMessageType.MailboxList:
                    // The Admin API's list, no sidecar (task 056); the session answers when the Admin API cannot.
                    bool shared = Flag(request, "shared");
                    return await ExchangeAsync(request, BridgeMessageType.Mailboxes, (backends, ct) => MailboxLookups.ListAsync(backends, shared, ct), cancellationToken).ConfigureAwait(false);
                case BridgeMessageType.MailboxAccess:
                    return await MailboxAccessAsync(request, cancellationToken).ConfigureAwait(false);
                case BridgeMessageType.Access:
                    return Access(request);
                case BridgeMessageType.UserSearch:
                    return await UserSearchAsync(request, cancellationToken).ConfigureAwait(false);
                case BridgeMessageType.SnapshotDiff:
                    return DiffSnapshots(request);

                case BridgeMessageType.MailboxAccessGrant:
                    return await AccessWriteAsync(request, grant: true, cancellationToken).ConfigureAwait(false);
                case BridgeMessageType.MailboxAccessRevoke:
                    return await AccessWriteAsync(request, grant: false, cancellationToken).ConfigureAwait(false);
                case BridgeMessageType.MailboxForwardingSet:
                    return await ForwardingWriteAsync(request, set: true, cancellationToken).ConfigureAwait(false);
                case BridgeMessageType.MailboxForwardingClear:
                    return await ForwardingWriteAsync(request, set: false, cancellationToken).ConfigureAwait(false);
                case BridgeMessageType.ApplyPlan:
                    return await ApplyPlanAsync(request, cancellationToken).ConfigureAwait(false);
                default:
                    var error = new EnvelopeError("unknown-type", $"no handler for '{request.Type}'");
                    return BridgeProtocol.Reply(request, BridgeMessageType.Error, error);
            }
        }
        catch (AegisException exception)
        {
            return Error(request, exception.Failure);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The tool broke: the page gets the internal error, the stack trace goes to the log.
            return Error(request, InternalError.ToFailure(exception, _services.LogInternalError(exception)));
        }
    }

    /// <summary>The <c>error</c> reply of a failure, its hint in the window's words where the core's names a command (task 057).</summary>
    private BridgeMessage Error(BridgeMessage request, AegisFailure failure) =>
        BridgeProtocol.Error(request, _services.Auth.Hints.ForSurface(failure));

    private async Task<BridgeMessage> WhoAmIAsync(BridgeMessage request, CancellationToken cancellationToken)
    {
        try
        {
            SignInInfo info = await _services.Auth.WhoAmIAsync(_services.Profile, cancellationToken).ConfigureAwait(false);
            return BridgeProtocol.Reply(request, BridgeMessageType.SignedIn, info);
        }
        catch (AegisException exception) when (exception.Failure.Kind == FailureKind.Auth)
        {
            // Signed out is a state of the page, not an error: the profile it was tried for, why, and what to do.
            TenantProfile? profile = TryResolveProfile();
            var signedOut = new SignedOutInfo(profile?.Name, profile?.Account, exception.Failure.Message, exception.Failure.Hint);
            return BridgeProtocol.Reply(request, BridgeMessageType.SignedOut, signedOut);
        }
    }

    private async Task<BridgeMessage> LoginAsync(BridgeMessage request, Func<BridgeMessage, Task> notify, CancellationToken cancellationToken)
    {
        // The window's profile only: writing a profile (--tenant, --client-id, --account) stays on the command line.
        var options = new LoginOptions(_services.Profile, null, null, null, Flag(request, "deviceCode"));
        // A plan was read with the account signed in before: a sign-in drops every plan waiting for a confirmation.
        _plans.Clear();
        try
        {
            SignInInfo info = await _services.Auth.LoginAsync(
                options,
                prompt =>
                {
                    // The one address the window may hand to the browser while this sign-in waits (task 047).
                    _signInPage = prompt.VerificationUri;
                    return notify(BridgeProtocol.Notification(BridgeMessageType.DeviceCode, prompt));
                },
                cancellationToken).ConfigureAwait(false);
            return BridgeProtocol.Reply(request, BridgeMessageType.SignedIn, info);
        }
        finally
        {
            _signInPage = null;
        }
    }

    /// <summary>
    /// Opens the page of the device-code sign-in in flight in the default browser: the address the sign-in service
    /// named in the device-code answer, and only on a Microsoft sign-in host over https (<see cref="SignInPage"/>);
    /// without a sign-in waiting, or without a browser to hand it to, nothing is opened.
    /// </summary>
    private async Task<BridgeMessage> OpenSignInPageAsync(BridgeMessage request)
    {
        Uri? page = _signInPage;
        if (page is null)
        {
            return Error(request, new AegisFailure(
                FailureKind.Usage,
                "no device code sign-in is waiting for its browser step",
                "choose Sign in with a code first; the card shows the page and the code"));
        }

        if (!SignInPage.IsAllowed(page))
        {
            return Error(request, new AegisFailure(
                FailureKind.ServiceError,
                $"the sign-in page '{page.GetLeftPart(UriPartial.Authority)}' is not one of Microsoft's sign-in hosts, so the window does not open it",
                "open the page the card shows in a browser yourself only if you trust it, or sign in with the account picker"));
        }

        if (_services.OpenInBrowser is not { } open)
        {
            return Error(request, new AegisFailure(
                FailureKind.ServiceError,
                "this window cannot open a browser",
                "open the page the card shows in a browser and enter the code"));
        }

        await open(page).ConfigureAwait(false);
        return BridgeProtocol.Reply(request, BridgeMessageType.SignInPageOpened, new SignInPageOpenedInfo(page));
    }

    private async Task<BridgeMessage> LogoutAsync(BridgeMessage request, CancellationToken cancellationToken)
    {
        SignOutInfo info = await _services.Auth.LogoutAsync(_services.Profile, cancellationToken).ConfigureAwait(false);
        // The plans waiting for a confirmation go with the account they were read for.
        _plans.Clear();
        if (_services.Exchange is { } exchange)
        {
            // The sidecar is connected with the account's token: it goes with the account.
            await exchange.ResetAsync().ConfigureAwait(false);
        }

        return BridgeProtocol.Reply(request, BridgeMessageType.LoggedOut, info);
    }

    private async Task<BridgeMessage> GroupMembersAsync(BridgeMessage request, CancellationToken cancellationToken)
    {
        string? group = Text(request, "group");
        if (group is null)
        {
            return Error(request, new AegisFailure(
                FailureKind.Usage,
                "the group is missing",
                "give the exact display name, the address, the object id, or a word of the name"));
        }

        GraphClient graph = _services.Graph(new RequestLog());
        CommandResult<GroupMembers> result = await GroupLookups.MembersAsync(graph, group, transitive: false, cancellationToken).ConfigureAwait(false);
        return Answer(request, BridgeMessageType.Members, result);
    }

    private Task<BridgeMessage> MailboxAccessAsync(BridgeMessage request, CancellationToken cancellationToken)
    {
        string? mailbox = Text(request, "mailbox");
        string? user = Text(request, "user");
        if ((mailbox is null) == (user is null))
        {
            return Task.FromResult(Error(request, new AegisFailure(
                FailureKind.Usage,
                "give a mailbox or a user, not both and not neither",
                "a mailbox lists who can open or send as it; a user lists the mailboxes the user can open or send as")));
        }

        // A mailbox reads as `mailbox access <mailbox>` does (task 063): the mailbox and its Send on Behalf delegates by
        // name from the Admin API beside FullAccess and Send As from the warm session, the session alone when the
        // window has no Admin API or it cannot answer. A user reads as `mailbox access --user <upn>` does (task 077):
        // the FullAccess entries of every mailbox split across sidecars (task 043), the first share on the warm
        // session, the others on sidecars started for this request.
        return user is not null
            ? ExchangeAsync(
                request,
                BridgeMessageType.UserGrants,
                (backends, ct) => MailboxLookups.AccessForUserAsync(backends, user, new UserAccessOptions(), ct),
                cancellationToken,
                DescribeSidecars)
            : ExchangeAsync(
                request,
                BridgeMessageType.MailboxHolders,
                (backends, ct) => MailboxLookups.AccessAsync(backends, mailbox!, new MailboxAccessOptions(), ct),
                cancellationToken,
                DescribeLayers);
    }

    /// <summary>
    /// <c>access</c> (task 102): what one user reaches, from the newest snapshot of the window's profile, as
    /// <c>access &lt;user&gt;</c> reads it (task 084): the snapshot file only, opened read-only, with no sign-in and no
    /// request to any service, the snapshot's age counted on the window's clock. A profile without a snapshot is answered
    /// with <c>no-snapshot</c>, a state of the card; a user the snapshot does not know is the <c>not-found</c> error, its
    /// hint the window's.
    /// </summary>
    private BridgeMessage Access(BridgeMessage request)
    {
        string? user = Text(request, "user");
        if (user is null)
        {
            return Error(request, new AegisFailure(
                FailureKind.Usage,
                "no user was named",
                "give the user's user principal name, address or object id"));
        }

        return FromSnapshot(request, (home, profile) =>
            BridgeProtocol.Reply(request, BridgeMessageType.AccessReport, SnapshotAccess.ForUser(home, profile, user, _services.Clock.GetUtcNow())));
    }

    /// <summary>
    /// <c>user-search</c> (task 112): the users whose name, a word of it, user principal name or address starts with the
    /// text, for the access cards' user picker. The directory answers first, live, through the window's Graph client
    /// and sign-in (<see cref="Groups.UserSearch"/>, silently: no sign-in window opens); when it cannot (no sign-in, the
    /// permission not consented, the network), the newest snapshot of the window's profile answers instead, and
    /// <c>no-snapshot</c> when the profile has none. The reply names the source that answered.
    /// </summary>
    private async Task<BridgeMessage> UserSearchAsync(BridgeMessage request, CancellationToken cancellationToken)
    {
        string? text = Text(request, "text");
        if (text is null)
        {
            return Error(request, new AegisFailure(FailureKind.Usage, "no text to search for", "type the start of a name or an address"));
        }

        try
        {
            UserMatches live = await Groups.UserSearch.FindAsync(_services.Graph(new RequestLog()), text, cancellationToken).ConfigureAwait(false);
            return BridgeProtocol.Reply(request, BridgeMessageType.UserMatches, live);
        }
        catch (AegisException exception) when (exception.Failure.Kind != FailureKind.Usage)
        {
            // The directory cannot answer now: the snapshot on this computer does, and the reply says so.
        }

        return FromSnapshot(request, (home, profile) =>
            BridgeProtocol.Reply(request, BridgeMessageType.UserMatches, SnapshotAccess.FindUsers(home, profile, text)));
    }

    /// <summary>
    /// A reply read from the newest snapshot of the window's profile, or <c>no-snapshot</c> when the profile has none: a
    /// state of the card, not an error.
    /// </summary>
    private BridgeMessage FromSnapshot(BridgeMessage request, Func<string, string, BridgeMessage> read)
    {
        string home = _services.Auth.Home;
        string profile = _services.Auth.Profiles.Resolve(_services.Profile).Name;
        try
        {
            _ = SnapshotStore.Newest(home, profile);
        }
        catch (AegisException exception) when (exception.Failure.Kind == FailureKind.NotFound)
        {
            return BridgeProtocol.Reply(request, BridgeMessageType.NoSnapshot, new NoSnapshotInfo(profile, exception.Failure.Message, NoSnapshotHint));
        }

        return read(home, profile);
    }

    /// <summary>What the card says to do without a snapshot: the snapshot is taken on the command line, by the administrator.</summary>
    internal const string NoSnapshotHint =
        "ask the administrator to take a snapshot on this computer, or to schedule one every morning";

    /// <summary>
    /// <c>snapshot-diff</c> (task 115): what appeared and disappeared between two snapshots of the window's profile, as
    /// <c>snapshot diff</c> reads it (task 098), for one user when the payload names one (task 114): the newest snapshot
    /// and the readable one before it unless the payload names them, the files only, the ages on the window's clock.
    /// Fewer than two snapshots (or none before the one named) is <c>too-few-snapshots</c>, a state of the card; a pick
    /// or a user that finds nothing is the error of its kind, its hint the window's.
    /// </summary>
    private BridgeMessage DiffSnapshots(BridgeMessage request)
    {
        string home = _services.Auth.Home;
        string profile = _services.Auth.Profiles.Resolve(_services.Profile).Name;
        SnapshotDiffReport? report = Snapshots.SnapshotDiff.CompareWithThePrevious(
            home, profile, Text(request, "from"), Text(request, "to"), Text(request, "user"), _services.Clock.GetUtcNow());
        if (report is not null)
        {
            return BridgeProtocol.Reply(request, BridgeMessageType.SnapshotDiffReport, report);
        }

        int count = SnapshotStore.List(home, profile).Snapshots.Count;
        string message = count switch
        {
            0 => $"no snapshot of the profile '{profile}' yet",
            1 => $"only one snapshot of the profile '{profile}' so far",
            _ => $"no snapshot of the profile '{profile}' before the one picked",
        };
        return BridgeProtocol.Reply(request, BridgeMessageType.TooFewSnapshots, new TooFewSnapshotsInfo(profile, count, message, TooFewSnapshotsHint));
    }

    /// <summary>What the diff card says to do with fewer than two snapshots: a snapshot every morning gives one to compare with.</summary>
    internal const string TooFewSnapshotsHint =
        "the card compares two snapshots: ask the administrator to take one every morning, so there is always the day before to compare with";

    /// <summary>
    /// <c>mailbox-access-grant</c> and <c>mailbox-access-revoke</c> (task 082): the rows of the arguments as
    /// <c>mailbox access grant|revoke</c> makes them, in the card's words where the command line's name a flag, planned
    /// over the window's layers as the command does.
    /// </summary>
    private Task<BridgeMessage> AccessWriteAsync(BridgeMessage request, bool grant, CancellationToken cancellationToken)
    {
        List<string> users = Texts(request, "users");
        List<string> rights = Texts(request, "rights");
        bool noAutoMapping = grant && Flag(request, "noAutoMapping");
        if (users.Count == 0)
        {
            return Task.FromResult(Error(request, new AegisFailure(
                FailureKind.Usage,
                "no user was named",
                "give each user's user principal name or address, or a mail-enabled security group's address")));
        }

        if (rights.Count == 0)
        {
            return Task.FromResult(Error(request, new AegisFailure(FailureKind.Usage, "no right was chosen", "choose FullAccess, Send As or Send on Behalf")));
        }

        if (noAutoMapping && !rights.Any(right => MailboxLookups.ParseRight(right) == MailboxRight.FullAccess))
        {
            return Task.FromResult(Error(request, new AegisFailure(
                FailureKind.Usage,
                "without automapping goes with FullAccess",
                "automapping is how Outlook adds a mailbox its FullAccess holder can open; choose FullAccess, or clear Without automapping")));
        }

        // The mailbox and each right are checked as the command line checks them, in words that fit the card too.
        IReadOnlyList<AccessChangeRow> rows = MailboxLookups.AccessRows(Text(request, "mailbox"), users, rights, autoMapping: !noAutoMapping);
        List<string> arguments = ["mailbox", "access", grant ? "grant" : "revoke", rows[0].Mailbox];
        arguments.AddRange(users.SelectMany(user => new[] { "--user", user }));
        arguments.AddRange(rows.Select(row => row.Right).Distinct().SelectMany(right => new[] { "--right", right.ToString() }));
        if (noAutoMapping)
        {
            arguments.Add("--no-automapping");
        }

        return PlanWriteAsync(request, arguments, (backends, ct) => MailboxLookups.PlanAccessAsync(backends, grant, rows, ct), cancellationToken);
    }

    /// <summary>
    /// <c>mailbox-forwarding-set</c> and <c>mailbox-forwarding-clear</c> (task 082): the row of the arguments as
    /// <c>mailbox forwarding set|clear</c> makes it, planned over the window's layers as the command does.
    /// </summary>
    private Task<BridgeMessage> ForwardingWriteAsync(BridgeMessage request, bool set, CancellationToken cancellationToken)
    {
        string? to = set ? Text(request, "to") : null;
        bool keepCopy = set && Flag(request, "keepCopy");
        if (set && to is null)
        {
            return Task.FromResult(Error(request, new AegisFailure(
                FailureKind.Usage,
                "the forwarding address is missing",
                "give the address to forward to, such as bob@fabrikam.com")));
        }

        IReadOnlyList<ForwardingRow> rows = MailboxLookups.ForwardingRows(Text(request, "mailbox"), to, keepCopy, set, "the forwarding address");
        List<string> arguments = ["mailbox", "forwarding", set ? "set" : "clear", rows[0].Mailbox];
        if (set)
        {
            arguments.AddRange(["--to", rows[0].To!]);
        }

        if (keepCopy)
        {
            arguments.Add("--keep-copy");
        }

        return PlanWriteAsync(request, arguments, (backends, ct) => MailboxLookups.PlanForwardingAsync(backends, rows, ct), cancellationToken);
    }

    /// <summary>
    /// A write request's plan over the window's layers (task 082): the core's plan, printed by the write guard as
    /// <c>--dry-run</c> prints it (nothing is sent, nothing is recorded), and held under a new id for an
    /// <c>apply-plan</c> with the confirmation, with the store its record goes to: the home folder's <c>plans</c>, the
    /// window as the source, the window's profile and the command line that makes the same plan
    /// (<paramref name="arguments"/> and <c>--yes</c>, which the confirmation stands for), so <c>plan show</c> reads the
    /// record and <c>--resume</c> continues it. A plan with nothing to change gets no id.
    /// </summary>
    private Task<BridgeMessage> PlanWriteAsync(
        BridgeMessage request,
        List<string> arguments,
        Func<ExchangeBackends, CancellationToken, Task<WritePlan>> plan,
        CancellationToken cancellationToken) =>
        ExchangeAsync(
            request,
            BridgeMessageType.Plan,
            async (backends, ct) =>
            {
                WritePlan write = await plan(backends, ct).ConfigureAwait(false);
                var store = new PlanStore(_services.Auth.Home, new PlanRecordContext(RecordSource, _services.Profile, [.. arguments, "--yes"]), _services.Clock);
                Plan shown = (await PlanGuard.RunAsync(write, WriteMode.DryRun, store, ct).ConfigureAwait(false)).Data!;
                string? id = shown.Mode == PlanMode.UpToDate ? null : Hold(write, store);
                return CommandResult.Success(new PlanReady(id, shown));
            },
            cancellationToken,
            (_, ready) => DescribePlan(ready.Plan));

    /// <summary>What a plan holds, for the window's <c>window:</c> log line: counts only, never a name.</summary>
    internal static string DescribePlan(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return string.Create(CultureInfo.InvariantCulture,
            $"{plan.Changes.Count} change(s) planned, {plan.Unchanged.Count} unchanged, {plan.Warnings.Count} warning(s)");
    }

    /// <summary>What an apply did, for the window's <c>window:</c> log line: the write and the counts, never a name.</summary>
    internal static string DescribeApply(Plan applied)
    {
        ArgumentNullException.ThrowIfNull(applied);
        int done = applied.Changes.Count(change => change.Result!.Status == ChangeStatus.Applied);
        int failed = applied.Changes.Count(change => change.Result!.Status == ChangeStatus.Failed);
        int skipped = applied.Changes.Count(change => change.Result!.Status == ChangeStatus.Skipped);
        return string.Create(CultureInfo.InvariantCulture, $"{applied.Operation}: {done} applied, {failed} failed, {skipped} skipped");
    }

    /// <summary>Holds a plan for its confirmation under a new id, and drops the plans whose time ran out.</summary>
    private string Hold(WritePlan write, PlanStore store)
    {
        DateTimeOffset now = _services.Clock.GetUtcNow();
        // The dictionary's enumeration is safe while it changes.
        foreach ((string key, HeldPlan held) in _plans)
        {
            if (IsStale(held, now))
            {
                _plans.TryRemove(key, out _);
            }
        }

        string id = Guid.NewGuid().ToString("N");
        _plans[id] = new HeldPlan(write, store, now);
        return id;
    }

    private static bool IsStale(HeldPlan held, DateTimeOffset now) => now - held.Planned > PlanLifetime;

    /// <summary>
    /// <c>apply-plan</c> (task 082): the write guard's apply of a plan this window holds, as <c>--yes</c> applies it on
    /// the command line, with the record under the home folder's <c>plans</c>, over the window's warm session. Refused,
    /// with nothing sent: a plan the window does not hold (never planned, applied already, dropped by a sign-in or a
    /// sign-out), one older than <see cref="PlanLifetime"/>, and one without <c>"confirm": true</c>, which stays held for
    /// the confirmation. A plan is applied once: the confirmed apply takes it before anything is sent.
    /// </summary>
    private async Task<BridgeMessage> ApplyPlanAsync(BridgeMessage request, CancellationToken cancellationToken)
    {
        string? id = Text(request, "planId");
        bool confirmed = Flag(request, "confirm");
        HeldPlan? held = id is null ? null : confirmed ? Take(id) : _plans.GetValueOrDefault(id);
        if (held is null)
        {
            return Error(request, new AegisFailure(
                FailureKind.Usage,
                "no plan with this id waits for a confirmation in this window; nothing was sent",
                "see the plan again on the card: a plan is applied once, and a sign-in or a sign-out drops it"));
        }

        if (IsStale(held, _services.Clock.GetUtcNow()))
        {
            _plans.TryRemove(id!, out _);
            return Error(request, new AegisFailure(
                FailureKind.Usage,
                string.Create(CultureInfo.InvariantCulture, $"the plan is older than {PlanLifetime.TotalMinutes} minutes; nothing was sent"),
                "see the plan again on the card: what it read may have changed since"));
        }

        if (!confirmed)
        {
            // The window's hint of the kind: confirm the change in the window.
            return Error(request, new AegisFailure(
                FailureKind.RefusedWithoutYes,
                string.Create(CultureInfo.InvariantCulture, $"{held.Write.Plan.Changes.Count} change(s) planned; nothing was sent"),
                null));
        }

        var elapsed = Stopwatch.StartNew();
        CommandResult<Plan>? result = null;
        try
        {
            result = await PlanGuard.RunAsync(held.Write, WriteMode.Apply, held.Store, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // An apply that threw (a cancellation) has its record finished by the guard; the line says it stopped.
            string outcome = result is null ? "the apply stopped" : DescribeApply(result.Data!);
            LogSink.Verbose?.Write("window", string.Create(
                CultureInfo.InvariantCulture,
                $"{request.Type} answered in {elapsed.ElapsedMilliseconds} ms ({outcome}); exchange {_services.Exchange!.Describe()}"));
        }

        Plan applied = result.Data!;
        EnvelopeError? error = result.Failure is { } failure ? _services.Auth.Hints.ForSurface(failure).ToError() : null;
        return BridgeProtocol.Reply(request, BridgeMessageType.PlanApplied, new PlanApplied(ForSurface(applied), error));
    }

    /// <summary>Takes a held plan out, so it is applied once.</summary>
    private HeldPlan? Take(string id)
    {
        _plans.TryRemove(id, out HeldPlan? held);
        return held;
    }

    /// <summary>
    /// The applied plan as the window shows it: each failed change's hint in the window's words where the core's names a
    /// command (task 057), as an <c>error</c> reply's; the record keeps the core's own.
    /// </summary>
    private Plan ForSurface(Plan applied) => applied with
    {
        Changes = applied.Changes
            .Select(change => change.Result!.Error is { } error ? change with { Result = change.Result with { Error = ForSurface(error) } } : change)
            .ToList(),
    };

    /// <summary>A change's error in the window's words: every error of an applied plan names a kind of the contract.</summary>
    private EnvelopeError ForSurface(EnvelopeError error)
    {
        FailureKind kind = Enum.GetValues<FailureKind>().First(candidate => candidate.ToCode() == error.Code);
        return _services.Auth.Hints.ForSurface(new AegisFailure(kind, error.Message, error.Hint)).ToError();
    }

    /// <summary>A plan waiting for its confirmation: the plan and its steps, the store of its record, when it was made.</summary>
    private sealed record HeldPlan(WritePlan Write, PlanStore Store, DateTimeOffset Planned);

    /// <summary>
    /// Which layer read what of a mailbox's access, for the window's <c>window:</c> log line (task 063): the counts and
    /// the layers, never a name. <paramref name="hasAdminApi"/> tells a window without an Admin API from one whose
    /// Admin API could not answer.
    /// </summary>
    internal static string DescribeLayers(bool hasAdminApi, MailboxAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);
        int onBehalf = access.Holders.Count(holder => holder.Right == MailboxRight.SendOnBehalf);
        int rights = access.Holders.Count - onBehalf;
        if (access.MailboxLayer == Backend.AdminApi)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"the mailbox and {onBehalf} Send on Behalf delegate(s) from {Backend.AdminApi}, {rights} FullAccess and Send As grant(s) from {Backend.Pwsh}");
        }

        string why = hasAdminApi ? "the Admin API could not answer" : "no Admin API in this window";
        return string.Create(CultureInfo.InvariantCulture,
            $"{why}: the mailbox, {onBehalf} Send on Behalf delegate(s) and {rights} FullAccess and Send As grant(s) from {Backend.Pwsh}");
    }

    private static string DescribeLayers(ExchangeBackends backends, MailboxAccess access) =>
        DescribeLayers(backends.HasAdminApi, access);

    /// <summary>
    /// How a user's reverse lookup read the FullAccess entries, for the window's <c>window:</c> log line (task 077): the
    /// mailboxes, the method and the sidecars it ran on, the warm session counted with the
    /// <paramref name="started"/> ones started for the request; counts only, never a name.
    /// </summary>
    internal static string DescribeSidecars(int started, UserAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);
        return string.Create(CultureInfo.InvariantCulture,
            $"FullAccess of {access.MailboxesScanned} mailbox(es) read {access.Method} over {started + 1} sidecar(s): the window's session and {started} started for this request");
    }

    private static string DescribeSidecars(ExchangeBackends backends, UserAccess access) =>
        DescribeSidecars(backends.SidecarsStarted, access);

    /// <summary>
    /// A mailbox lookup over the window's layers (task 056: the session, the Admin API over the same token, Graph): the
    /// data as the reply, a failure as <c>error</c>, and a partial answer (exit code 6 on the command line) as the reply,
    /// whose own list says what could not be read. Under <c>--verbose</c> the log gets the time, the layers the request
    /// used, what each layer read when <paramref name="describe"/> tells it (task 063), and what the session did (the
    /// starts, the reconnects, the token acquisitions).
    /// </summary>
    private async Task<BridgeMessage> ExchangeAsync<T>(
        BridgeMessage request,
        string replyType,
        Func<ExchangeBackends, CancellationToken, Task<CommandResult<T>>> lookup,
        CancellationToken cancellationToken,
        Func<ExchangeBackends, T, string?>? describe = null)
        where T : class
    {
        if (_services.Exchange is not { } exchange)
        {
            return Error(request, new AegisFailure(
                FailureKind.SidecarUnavailable,
                "this window has no Exchange Online session",
                "run the mailbox commands of `m365-aegis` instead"));
        }

        var elapsed = Stopwatch.StartNew();
        CommandResult<T>? result = null;
        await using ExchangeBackends backends = exchange.Backends(() => _services.Graph(new RequestLog()));
        try
        {
            result = await lookup(backends, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // A lookup that threw has no result, and only an answer with its data has layers to describe.
            string? layers = result?.Data is { } data && describe is not null ? describe(backends, data) : null;
            LogSink.Verbose?.Write("window", string.Create(
                CultureInfo.InvariantCulture,
                $"{request.Type} answered in {elapsed.ElapsedMilliseconds} ms through {backends.Backend}{(layers is null ? "" : $" ({layers})")}; exchange {exchange.Describe()}"));
        }

        return Answer(request, replyType, result);
    }

    /// <summary>
    /// The reply of a lookup's result: the data, or <c>error</c> for a failure; a partial answer that carries its data
    /// (exit code 6 on the command line) is the reply, whose own list says what could not be read.
    /// </summary>
    internal BridgeMessage Answer<T>(BridgeMessage request, string replyType, CommandResult<T> result) =>
        result.Failure is { } failure && (failure.Kind != FailureKind.Partial || result.Data is null)
            ? Error(request, failure)
            : BridgeProtocol.Reply(request, replyType, result.Data);

    /// <summary>A text field of the payload, trimmed; null when it is missing, not a string, or blank.</summary>
    private static string? Text(BridgeMessage request, string name)
    {
        string? text = request.Data is JsonObject payload && payload[name] is JsonValue value && value.TryGetValue(out string? raw)
            ? raw.Trim()
            : null;
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>The texts of a list of the payload, trimmed, without the blank ones and the ones that are not strings; empty when there is no list.</summary>
    private static List<string> Texts(BridgeMessage request, string name) =>
        request.Data is JsonObject payload && payload[name] is JsonArray items
            ? items.Select(item => item is JsonValue value && value.TryGetValue(out string? text) ? text.Trim() : "").Where(text => text.Length > 0).ToList()
            : [];

    /// <summary>A flag of the payload; false when it is missing or not a boolean.</summary>
    private static bool Flag(BridgeMessage request, string name) =>
        request.Data is JsonObject payload && payload[name] is JsonValue value && value.TryGetValue(out bool flag) && flag;

    private TenantProfile? TryResolveProfile()
    {
        try
        {
            return _services.Auth.Profiles.Resolve(_services.Profile);
        }
        catch (AegisException)
        {
            return null;
        }
    }
}
