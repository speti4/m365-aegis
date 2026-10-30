using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Nodes;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Config;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Output;

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
                tokens => new ExchangeSession(ExchangeConnection.Options(profile, new RequestLog(), () => auth, tokens)),
                tokens => AdminApiConnection.Create(profile, new RequestLog(), () => auth, tokens)),
        };
    }
}

/// <summary>
/// Routes a front-end request to the core and makes the reply: <c>ping</c>, the sign-in (<c>whoami</c>,
/// <c>login</c>, <c>logout</c>), a group's members (<c>group-members</c>) and the mailbox lookups over the window's
/// Exchange Online session (<c>mailbox-list</c>, <c>mailbox-access</c>), each answered with its reply type or with
/// <c>error</c>. A failure of the core becomes the <c>error</c> reply with the contract's code, message and hint
/// (D-006), the hint in the window's words where the core's names a command (task 057,
/// <see cref="SignInHints.ForSurface"/>); an unexpected exception becomes the <c>internal</c> error with the stack
/// trace in the log, as on the command line. Every later verb (the task cards of M4) registers here, and every write goes through a plan and a
/// confirmation the front end shows.
/// </summary>
public sealed class BridgeDispatcher
{
    private readonly BridgeServices _services;

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
        // window has no Admin API or it cannot answer. The reverse lookup is the session's bulk read (task 045).
        return user is not null
            ? ExchangeAsync(request, BridgeMessageType.UserGrants, (backends, ct) => MailboxLookups.AccessForUserAsync(backends.Sidecar, user, ct), cancellationToken)
            : ExchangeAsync(
                request,
                BridgeMessageType.MailboxHolders,
                (backends, ct) => MailboxLookups.AccessAsync(backends, mailbox!, new MailboxAccessOptions(), ct),
                cancellationToken,
                DescribeLayers);
    }

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

    private static string? DescribeLayers(ExchangeBackends backends, MailboxAccess? access) =>
        access is null ? null : DescribeLayers(backends.HasAdminApi, access);

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
        Func<ExchangeBackends, T?, string?>? describe = null)
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
            string? layers = result is null || describe is null ? null : describe(backends, result.Data);
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
