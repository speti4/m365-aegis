using M365Aegis.Core.Exchange.AdminApi;
using M365Aegis.Core.Exchange.Sidecar;
using M365Aegis.Core.Graph;

namespace M365Aegis.Core.Exchange;

/// <summary>
/// The three layers of D-004 one mailbox command may read through (task 042): the sidecar, the Exchange Online Admin
/// API (null in a run that has none, the sidecar-only offline tests) and Graph, each made on first use, and more
/// sidecars on demand for a bulk read split across several processes (task 043). A lookup picks the layer per read;
/// <see cref="Backend"/> names the layers it used, for the envelope's meta. The extra sidecars end with the object.
/// </summary>
public sealed class ExchangeBackends : IAsyncDisposable
{
    private readonly IExoCmdletRunner _sidecar;
    private readonly Lazy<GraphClient>? _graph;
    private readonly Func<IExoCmdletRunner>? _newSidecar;
    private readonly List<IExoCmdletRunner> _extra = [];
    private readonly List<string> _used = [];
    private readonly Lock _lock = new();
    private IExoCmdletRunner? _adminApiInner;
    private IExoCmdletRunner? _adminApi;
    private int _started;
    private long _sidecarCalls;
    private long _adminApiCalls;

    /// <summary>The layers of a command.</summary>
    /// <param name="sidecar">The sidecar (one per command line run, started on its first cmdlet).</param>
    /// <param name="adminApi">The Admin API, or null when the run has none.</param>
    /// <param name="graph">Makes the Graph client, or null when the run has none.</param>
    /// <param name="newSidecar">Makes one more sidecar for a split bulk read, or null when the run cannot.</param>
    public ExchangeBackends(IExoCmdletRunner sidecar, IExoCmdletRunner? adminApi = null, Func<GraphClient>? graph = null, Func<IExoCmdletRunner>? newSidecar = null)
    {
        ArgumentNullException.ThrowIfNull(sidecar);
        _sidecar = new Counted(sidecar, this, adminApi: false);
        _adminApiInner = adminApi;
        _adminApi = adminApi is null ? null : new Counted(adminApi, this, adminApi: true);
        _graph = graph is null ? null : new Lazy<GraphClient>(graph);
        _newSidecar = newSidecar;
    }

    /// <summary>The sidecar.</summary>
    public IExoCmdletRunner Sidecar
    {
        get
        {
            Use(Output.Backend.Pwsh);
            return _sidecar;
        }
    }

    /// <summary>The Admin API, or null when the run has none.</summary>
    public IExoCmdletRunner? AdminApi
    {
        get
        {
            if (_adminApi is not null)
            {
                Use(Output.Backend.AdminApi);
            }

            return _adminApi;
        }
    }

    /// <summary>Whether the run has an Admin API.</summary>
    public bool HasAdminApi => _adminApi is not null;

    /// <summary>Whether the run has Graph.</summary>
    public bool HasGraph => _graph is not null;

    /// <summary>The Graph client.</summary>
    /// <exception cref="InvalidOperationException">The run has no Graph (a test that gave none).</exception>
    public GraphClient Graph
    {
        get
        {
            if (_graph is null)
            {
                throw new InvalidOperationException("this run has no Graph client");
            }

            Use(Output.Backend.Graph);
            return _graph.Value;
        }
    }

    /// <summary>Whether more sidecars can be started for a split bulk read.</summary>
    public bool CanStartSidecars => _newSidecar is not null;

    /// <summary>One more sidecar, ended with this object.</summary>
    /// <exception cref="InvalidOperationException">The run cannot start more.</exception>
    public IExoCmdletRunner StartSidecar()
    {
        if (_newSidecar is null)
        {
            throw new InvalidOperationException("this run cannot start more sidecars");
        }

        IExoCmdletRunner sidecar = _newSidecar();
        lock (_lock)
        {
            _extra.Add(sidecar);
            _started++;
        }

        Use(Output.Backend.Pwsh);
        return new Counted(sidecar, this, adminApi: false);
    }

    /// <summary>
    /// How many sidecars <see cref="StartSidecar"/> started for this object (a split read's, task 043), the command's
    /// own <see cref="Sidecar"/> not counted; the count stays after the dispose ends them.
    /// </summary>
    public int SidecarsStarted
    {
        get
        {
            lock (_lock)
            {
                return _started;
            }
        }
    }

    /// <summary>The cmdlets sent to a sidecar so far, the command's own and those started for a split read (the snapshot's per-domain counts, task 097).</summary>
    public long SidecarCalls => Interlocked.Read(ref _sidecarCalls);

    /// <summary>The cmdlets sent to the Admin API so far.</summary>
    public long AdminApiCalls => Interlocked.Read(ref _adminApiCalls);

    /// <summary>How long the Admin API client has waited on throttling so far; zero without one (or with a test's stand-in).</summary>
    public TimeSpan AdminApiWaited => (_adminApiInner as AdminApiClient)?.Waited ?? TimeSpan.Zero;

    /// <summary>
    /// Gives the Admin API the take's throttling budget (task 117): the client is replaced by its
    /// <see cref="AdminApiClient.WithBudget"/> copy under the same counting wrapper, so the cmdlet count and the waits go
    /// on from where they are. Nothing happens without an <see cref="AdminApiClient"/> (a run
    /// without the Admin API, a test's stand-in).
    /// </summary>
    internal void BudgetAdminApi(int maxRetries, TimeSpan maxTotalDelay)
    {
        lock (_lock)
        {
            if (_adminApiInner is AdminApiClient client)
            {
                _adminApiInner = client.WithBudget(maxRetries, maxTotalDelay);
                _adminApi = new Counted(_adminApiInner, this, adminApi: true);
            }
        }
    }

    /// <summary>The layers used so far, in the order first used, joined with <c>+</c> (<c>admin-api+pwsh</c>); <c>pwsh</c> before any.</summary>
    public string Backend
    {
        get
        {
            lock (_lock)
            {
                return _used.Count == 0 ? Output.Backend.Pwsh : string.Join("+", _used);
            }
        }
    }

    /// <summary>
    /// Drops a layer from <see cref="Backend"/>: one that was asked and refused, and whose part the answer does without
    /// (task 096), so the backend names the layers the answer comes from.
    /// </summary>
    internal void Forget(string backend)
    {
        lock (_lock)
        {
            _used.Remove(backend);
        }
    }

    /// <summary>Ends the extra sidecars.</summary>
    public async ValueTask DisposeAsync()
    {
        List<IExoCmdletRunner> extra;
        lock (_lock)
        {
            extra = [.. _extra];
            _extra.Clear();
        }

        foreach (IAsyncDisposable sidecar in extra.OfType<IAsyncDisposable>())
        {
            await sidecar.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void Use(string backend)
    {
        lock (_lock)
        {
            if (!_used.Contains(backend, StringComparer.Ordinal))
            {
                _used.Add(backend);
            }
        }
    }

    /// <summary>A layer's runner that counts the cmdlets sent through it.</summary>
    private sealed class Counted(IExoCmdletRunner inner, ExchangeBackends owner, bool adminApi) : IExoCmdletRunner
    {
        public Task<ExoResult> InvokeAsync(ExoCommand command, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref adminApi ? ref owner._adminApiCalls : ref owner._sidecarCalls);
            return inner.InvokeAsync(command, cancellationToken);
        }
    }
}
