using System.Globalization;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Bridge;

/// <summary>
/// The window's one Exchange Online session (task 045): no sidecar until the first mailbox request, then one
/// <see cref="ExchangeSession"/> for the window's lifetime, so every later lookup answers in the cmdlet's own time
/// instead of paying pwsh, the module's import and the connect again. The session itself renews its token on the
/// next cmdlet five minutes before the expiry and replaces a sidecar that died; this holder keeps it warm between
/// requests as well: a timer reconnects with a fresh token a little before the session would, so a request after an
/// idle hour does not wait for the reconnect. A sign-out ends the session (<see cref="ResetAsync"/>); the window's
/// close disposes it, which ends the sidecar.
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

    private readonly Func<ExchangeSession> _create;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
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
        _create = create;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>The current session, or null before the first mailbox request and after a sign-out.</summary>
    public ExchangeSession? Session => _session;

    /// <summary>How many sessions the window made (more than one after a sign-out and a new request).</summary>
    public int Sessions { get; private set; }

    /// <summary>How many times the keep-warm timer reconnected with a fresh token.</summary>
    public int KeptWarm { get; private set; }

    /// <inheritdoc/>
    public async Task<ExoResult> InvokeAsync(ExoCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ExoCmdlets.EnsureAllowed(command.Cmdlet);
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
        if (session is null)
        {
            return "no sidecar";
        }

        SidecarConnected? connection = session.Connection;
        return string.Create(CultureInfo.InvariantCulture,
            $"session {Sessions}, sidecar starts {session.Starts}, renewals {session.Renewals}, last connect {(connection is null ? "none" : $"{connection.ConnectMs} ms")}");
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
                _session = _create();
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
    }
}
