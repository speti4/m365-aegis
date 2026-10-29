using System.Text.Json.Nodes;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Config;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Groups;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Bridge;

/// <summary>
/// What the window's requests run on: the same sign-in and Graph client the command line uses, held for the life
/// of the window. <see cref="ForWindow"/> wires the product; the tests give a sign-in over a scripted broker and a
/// client over the fixtures.
/// </summary>
/// <param name="Auth">The sign-in: <c>whoami</c> and the Graph token, silently.</param>
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
    /// The window's services over a home folder: MSAL through the Windows account manager with the window as the
    /// account picker's parent, the cache, the profiles and the log of that home folder (shared with the command
    /// line), and the Graph client of <see cref="GraphConnection"/> (the fixtures when
    /// <see cref="GraphConnection.FixturesEnvironmentVariable"/> is set).
    /// </summary>
    public static BridgeServices ForWindow(string home, string? profile, Func<IntPtr> parentWindow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        ArgumentNullException.ThrowIfNull(parentWindow);
        var auth = new AuthService(home, tenantProfile => MsalTokenBroker.Create(tenantProfile, home, parentWindow));
        return new BridgeServices(
            auth,
            profile,
            requestLog => GraphConnection.Create(profile, requestLog, () => auth),
            exception => InternalError.WriteToLog(exception, home));
    }
}

/// <summary>
/// Routes a front-end request to the core and makes the reply: <c>ping</c>, the sign-in state (<c>whoami</c>) and
/// a group's members (<c>group-members</c>), each answered with its reply type or with <c>error</c>. A failure of
/// the core becomes the <c>error</c> reply with the contract's code, message and hint (D-006); an unexpected
/// exception becomes the <c>internal</c> error with the stack trace in the log, as on the command line. Every later
/// verb (the task cards of M4) registers here, and every write goes through a plan and a confirmation the front end
/// shows.
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
    public async Task<BridgeMessage?> DispatchAsync(BridgeMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
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
                case BridgeMessageType.GroupMembers:
                    return await GroupMembersAsync(request, cancellationToken).ConfigureAwait(false);
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

    private async Task<BridgeMessage> GroupMembersAsync(BridgeMessage request, CancellationToken cancellationToken)
    {
        string? group = request.Data is JsonObject payload && payload["group"] is JsonValue value && value.TryGetValue(out string? text)
            ? text.Trim()
            : null;
        if (string.IsNullOrEmpty(group))
        {
            return BridgeProtocol.Error(request, new AegisFailure(
                FailureKind.Usage,
                "the group is missing",
                "give the exact display name, the address, the object id, or a word of the name"));
        }

        GraphClient graph = _services.Graph(new RequestLog());
        CommandResult<GroupMembers> result = await GroupLookups.MembersAsync(graph, group, cancellationToken).ConfigureAwait(false);
        return result.Failure is { } failure
            ? BridgeProtocol.Error(request, failure)
            : BridgeProtocol.Reply(request, BridgeMessageType.Members, result.Data);
    }

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
