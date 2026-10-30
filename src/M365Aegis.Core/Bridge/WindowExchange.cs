using System.Globalization;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Exchange;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Bridge;

/// <summary>
/// The window's one Exchange Online session (task 045): no sidecar until the first mailbox request, then one
/// <see cref="ExchangeSession"/> for the window's lifetime, so every later lookup answers in the cmdlet's own time
/// instead of paying pwsh, the module's import and the connect again. The session itself renews its token on the
/// next cmdlet five minutes before the expiry and replaces a sidecar that died; this holder keeps it warm between
/// requests as well: a timer reconnects with a fresh token a little before the session would, so a request after an
/// idle hour does not wait for the reconnect. A sign-out ends the session (<see cref="ResetAsync"/>); the window's
/// close disposes it, which ends the sidecar. Since task 056 the holder also gives each request the command line's
/// layers (<see cref="Backends"/>): its session as the sidecar and an Admin API client, both over one
/// <see cref="ExchangeTokenSource"/> for the window (task 049), so a read the Admin API answers starts no sidecar and
/// the session's connect, its renewals and the Admin API's calls share one token. The sign-out drops the source with
/// the session, so the next request asks the sign-in again.
/// </summary>
public sealed class WindowExchange : IExoCmdletRunner, IAsyncDisposable
{
    /// <summary>How often the keep-warm timer looks at the token.</summary>
    public static readonly TimeSpan KeepWarmPeriod = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long before the token's expiry the timer reconnects: before the session's own five minutes, plus one
    /// period of the timer, so a request never meets the session's renewal.
    /// </summary>
    public static readonly TimeSpan RenewAhead = TimeSpan.FromMinutes(7);

    private readonly Func<ExchangeTokenSource?, ExchangeSession> _create;
    private readonly Func<ExchangeTokenSource>? _newTokens;
    private readonly Func<ExchangeTokenSource, IExoCmdletRunner?>? _adminApi;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _tokensLock = new();
    private ExchangeTokenSource? _tokens;
    private ExchangeSession? _session;
    private ITimer? _timer;
    private int _ticking;
    private bool _disposed;

    /// <summary>A holder that makes its session with <paramref name="create"/> on the first cmdlet.</summary>
    /// <param name="create">Makes the session (nothing starts until its first cmdlet).</param>
    /// <param name="clock">The clock of the keep-warm timer and of the expiry it reads.</param>
    public WindowExchange(Func<ExchangeSession> create, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(create);
        _create = _ => create();
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// A holder of the window's layers over one token source (task 056): <paramref name="tokens"/> makes the source on
    /// the first request (and again after a sign-out), <paramref name="create"/> the session over it on the first
    /// cmdlet, and <paramref name="adminApi"/> an Admin API client over it for each request (null where the window has
    /// none: the sidecar-only offline tests).
    /// </summary>
    /// <param name="tokens">Makes the window's token source.</param>
    /// <param name="create">Makes the session over the source (nothing starts until its first cmdlet).</param>
    /// <param name="adminApi">Makes an Admin API client over the source, or returns null when the window has none.</param>
    /// <param name="clock">The clock of the keep-warm timer and of the expiry it reads.</param>
    public WindowExchange(
        Func<ExchangeTokenSource> tokens,
        Func<ExchangeTokenSource, ExchangeSession> create,
        Func<ExchangeTokenSource, IExoCmdletRunner?> adminApi,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(adminApi);
        _newTokens = tokens;
        _create = source => create(source!);
        _adminApi = adminApi;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>The current session, or null before the first mailbox request and after a sign-out.</summary>
    public ExchangeSession? Session => _session;

    /// <summary>How many sessions the window made (more than one after a sign-out and a new request).</summary>
    public int Sessions { get; private set; }

    /// <summary>How many times the keep-warm timer reconnected with a fresh token.</summary>
    public int KeptWarm { get; private set; }

    /// <summary>The window's token source, or null before the first request, after a sign-out, or without one.</summary>
    public ExchangeTokenSource? Tokens
    {
        get
        {
            lock (_tokensLock)
            {
                return _tokens;
            }
        }
    }

    /// <summary>
    /// The layers of one request (task 056): this holder as the sidecar (its session, started on the first cmdlet and
    /// kept warm), an Admin API client over the window's token source (none in a holder made without one), and Graph
    /// when <paramref name="graph"/> is given. A lookup picks the layer per read as on the command line; the object's
    /// <see cref="ExchangeBackends.Backend"/> names the layers the request used. Disposing it leaves the session alone.
    /// </summary>
    public ExchangeBackends Backends(Func<GraphClient>? graph = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IExoCmdletRunner? adminApi = _adminApi is null ? null : _adminApi(CurrentTokens()!);
        return new ExchangeBackends(this, adminApi, graph);
    }

    /// <inheritdoc/>
    public async Task<ExoResult> InvokeAsync(ExoCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ExoCmdlets.EnsureAllowed(command);
        ExchangeSession session = await CurrentAsync(cancellationToken).ConfigureAwait(false);
        return await session.InvokeAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// One tick of the keep-warm timer: when a session is connected and its token expires within
    /// <see cref="RenewAhead"/>, it reconnects now with a fresh token. A failure is left to the next request, which
    /// meets it as its own answer (a sign-in that expired is exit code 3 there). True when it reconnected.
    /// </summary>
    public async Task<bool> RenewIfDueAsync(CancellationToken cancellationToken)
    {
        ExchangeSession? session = _session;
        if (_disposed || session?.Token is not { } token || token.ExpiresOn - _clock.GetUtcNow() > RenewAhead)
        {
            return false;
        }

        try
        {
            await session.RenewAsync(cancellationToken).ConfigureAwait(false);
            KeptWarm++;
            LogSink.Verbose?.Write("window", string.Create(CultureInfo.InvariantCulture,
                $"exchange session kept warm: reconnected with a fresh token in {session.Connection?.ConnectMs} ms (renewal {session.Renewals})"));
            return true;
        }
        catch (Exception exception) when (exception is AegisException or ObjectDisposedException or OperationCanceledException)
        {
            LogSink.Verbose?.Write("window", $"exchange session not renewed: {exception.Message}");
            return false;
        }
    }

    /// <summary>Ends the session (a sign-out): the sidecar disconnects and exits; the next mailbox request starts a new one.</summary>
    public async Task ResetAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await EndAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>What the session did so far, for the window's verbose log: never a token.</summary>
    public string Describe()
    {
        ExchangeSession? session = _session;
        ExchangeTokenSource? tokens = Tokens;
        string acquired = tokens is null ? "" : string.Create(CultureInfo.InvariantCulture, $", token acquisitions {tokens.Acquisitions}");
        if (session is null)
        {
            return $"no sidecar{acquired}";
        }

        SidecarConnected? connection = session.Connection;
        return string.Create(CultureInfo.InvariantCulture,
            $"session {Sessions}, sidecar starts {session.Starts}, renewals {session.Renewals}, last connect {(connection is null ? "none" : $"{connection.ConnectMs} ms")}{acquired}");
    }

    /// <summary>Ends the timer and the session; the sidecar disconnects and exits.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            await EndAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        _gate.Dispose();
    }

    private async Task<ExchangeSession> CurrentAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is null)
            {
                _session = _create(CurrentTokens());
                Sessions++;
                _timer = _clock.CreateTimer(_ => _ = TickAsync(), null, KeepWarmPeriod, KeepWarmPeriod);
            }

            return _session;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The window's token source, made on first use; null in a holder made without one.</summary>
    private ExchangeTokenSource? CurrentTokens()
    {
        if (_newTokens is null)
        {
            return null;
        }

        lock (_tokensLock)
        {
            return _tokens ??= _newTokens();
        }
    }

    private async Task TickAsync()
    {
        // One tick at a time: a renewal that waits for a long cmdlet must not stack up behind it.
        if (Interlocked.Exchange(ref _ticking, 1) == 1)
        {
            return;
        }

        try
        {
            await RenewIfDueAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _ticking, 0);
        }
    }

    private async Task EndAsync()
    {
        if (_timer is not null)
        {
            await _timer.DisposeAsync().ConfigureAwait(false);
            _timer = null;
        }

        if (_session is not null)
        {
            ExchangeSession session = _session;
            _session = null;
            await session.DisposeAsync().ConfigureAwait(false);
        }

        // The token goes with the account: the next request asks the sign-in for a new one.
        lock (_tokensLock)
        {
            _tokens = null;
        }
    }
}
