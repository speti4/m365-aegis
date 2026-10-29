using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Config;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Exchange;
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
    /// The window's one Exchange Online session (task 045), or null where the services have none (then a mailbox
    /// request answers exit code 11's failure). The window disposes it when it closes.
    /// </summary>
    public WindowExchange? Exchange { get; init; }

    /// <summary>
    /// The window's services over a home folder: MSAL through the Windows account manager with the window as the
    /// account picker's parent and the picker started on the window's thread (<paramref name="onWindowThread"/>),
    /// the cache, the profiles and the log of that home folder (shared with the command line), the Graph client of
    /// <see cref="GraphConnection"/> (the fixtures when <see cref="GraphConnection.FixturesEnvironmentVariable"/> is
    /// set), and one Exchange Online session for the window's lifetime over the same sign-in (the offline tests'
    /// organization when <see cref="ExchangeConnection.TestOrganizationVariable"/> is set, as on the command line).
    /// </summary>
    public static BridgeServices ForWindow(string home, string? profile, Func<IntPtr> parentWindow, Func<Func<Task>, Task> onWindowThread)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        ArgumentNullException.ThrowIfNull(parentWindow);
        ArgumentNullException.ThrowIfNull(onWindowThread);
        var auth = new AuthService(home, tenantProfile => MsalTokenBroker.Create(tenantProfile, home, parentWindow, onWindowThread));
        return new BridgeServices(
            auth,
            profile,
            requestLog => GraphConnection.Create(profile, requestLog, () => auth),
            exception => InternalError.WriteToLog(exception, home))
        {
            Exchange = new WindowExchange(() => new ExchangeSession(WindowSessionOptions(auth, profile))),
        };
    }

    /// <summary>The parts of the window's session: the command line's, over the window's own sign-in.</summary>
    private static ExchangeSessionOptions WindowSessionOptions(AuthService auth, string? profile)
    {
        // The session's one Graph call (the organization's .onmicrosoft.com name) logs its request id here; the window
        // shows no meta, so nothing reads it.
        var requestLog = new RequestLog();
        string? testOrganization = Environment.GetEnvironmentVariable(ExchangeConnection.TestOrganizationVariable);
        return string.IsNullOrWhiteSpace(testOrganization)
            ? ExchangeConnection.Options(
                auth,
                profile,
                requestLog,
                ExchangeConnection.ParseConnectWith(Environment.GetEnvironmentVariable(ExchangeConnection.ConnectWithVariable)),
                ExchangeConnection.StartAsync)
            : ExchangeConnection.Options(profile, requestLog);
    }
}

/// <summary>
/// Routes a front-end request to the core and makes the reply: <c>ping</c>, the sign-in (<c>whoami</c>,
/// <c>login</c>, <c>logout</c>), a group's members (<c>group-members</c>) and the mailbox lookups over the window's
/// Exchange Online session (<c>mailbox-list</c>, <c>mailbox-access</c>), each answered with its reply type or with
/// <c>error</c>. A failure of the core becomes the <c>error</c> reply with the contract's code, message and hint
/// (D-006); an unexpected exception becomes the <c>internal</c> error with the stack trace in the log, as on the
/// command line. Every later verb (the task cards of M4) registers here, and every write goes through a plan and a
/// confirmation the front end shows.
/// </summary>
public sealed class BridgeDispatcher
{
    private readonly BridgeServices _services;

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
                case BridgeMessageType.Logout:
                    return await LogoutAsync(request, cancellationToken).ConfigureAwait(false);
                case BridgeMessageType.GroupMembers:
                    return await GroupMembersAsync(request, cancellationToken).ConfigureAwait(false);
                case BridgeMessageType.MailboxList:
                    bool shared = Flag(request, "shared");
                    return await ExchangeAsync(request, BridgeMessageType.Mailboxes, (exo, ct) => MailboxLookups.ListAsync(exo, shared, ct), cancellationToken).ConfigureAwait(false);
                case BridgeMessageType.MailboxAccess:
                    return await MailboxAccessAsync(request, cancellationToken).ConfigureAwait(false);
                default:
                    var error = new EnvelopeError("unknown-type", $"no handler for '{request.Type}'");
                    return BridgeProtocol.Reply(request, BridgeMessageType.Error, error);
            }
        }
        catch (AegisException exception)
        {
            return BridgeProtocol.Error(request, exception.Failure);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The tool broke: the page gets the internal error, the stack trace goes to the log.
            return BridgeProtocol.Error(request, InternalError.ToFailure(exception, _services.LogInternalError(exception)));
        }
    }

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
        SignInInfo info = await _services.Auth.LoginAsync(
            options,
            prompt => notify(BridgeProtocol.Notification(BridgeMessageType.DeviceCode, prompt)),
            cancellationToken).ConfigureAwait(false);
        return BridgeProtocol.Reply(request, BridgeMessageType.SignedIn, info);
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
            return BridgeProtocol.Error(request, new AegisFailure(
                FailureKind.Usage,
                "the group is missing",
                "give the exact display name, the address, the object id, or a word of the name"));
        }

        GraphClient graph = _services.Graph(new RequestLog());
        CommandResult<GroupMembers> result = await GroupLookups.MembersAsync(graph, group, transitive: false, cancellationToken).ConfigureAwait(false);
        return result.Failure is { } failure
            ? BridgeProtocol.Error(request, failure)
            : BridgeProtocol.Reply(request, BridgeMessageType.Members, result.Data);
    }

    private Task<BridgeMessage> MailboxAccessAsync(BridgeMessage request, CancellationToken cancellationToken)
    {
        string? mailbox = Text(request, "mailbox");
        string? user = Text(request, "user");
        if ((mailbox is null) == (user is null))
        {
            return Task.FromResult(BridgeProtocol.Error(request, new AegisFailure(
                FailureKind.Usage,
                "give a mailbox or a user, not both and not neither",
                "a mailbox lists who can open or send as it; a user lists the mailboxes the user can open or send as")));
        }

        return user is not null
            ? ExchangeAsync(request, BridgeMessageType.UserGrants, (exo, ct) => MailboxLookups.AccessForUserAsync(exo, user, ct), cancellationToken)
            : ExchangeAsync(request, BridgeMessageType.MailboxHolders, (exo, ct) => MailboxLookups.AccessAsync(exo, mailbox!, ct), cancellationToken);
    }

    /// <summary>
    /// A mailbox lookup over the window's session: the data as the reply, a failure as <c>error</c>, and a partial
    /// answer (exit code 6 on the command line) as the reply, whose own list says what could not be read. Under
    /// <c>--verbose</c> the log gets the time and what the session did (the starts and the reconnects).
    /// </summary>
    private async Task<BridgeMessage> ExchangeAsync<T>(
        BridgeMessage request,
        string replyType,
        Func<IExoCmdletRunner, CancellationToken, Task<CommandResult<T>>> lookup,
        CancellationToken cancellationToken)
    {
        if (_services.Exchange is not { } exchange)
        {
            return BridgeProtocol.Error(request, new AegisFailure(
                FailureKind.SidecarUnavailable,
                "this window has no Exchange Online session",
                "run the mailbox commands of `m365-aegis` instead"));
        }

        var elapsed = Stopwatch.StartNew();
        CommandResult<T> result;
        try
        {
            result = await lookup(exchange, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            LogSink.Verbose?.Write("window", string.Create(
                CultureInfo.InvariantCulture,
                $"{request.Type} answered in {elapsed.ElapsedMilliseconds} ms; exchange {exchange.Describe()}"));
        }

        return result.Failure is { } failure && (failure.Kind != FailureKind.Partial || result.Data is null)
            ? BridgeProtocol.Error(request, failure)
            : BridgeProtocol.Reply(request, replyType, result.Data);
    }

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
