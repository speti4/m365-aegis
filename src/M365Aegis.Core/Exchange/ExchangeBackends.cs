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
    private readonly IExoCmdletRunner? _adminApi;
    private readonly Lazy<GraphClient>? _graph;
    private readonly Func<IExoCmdletRunner>? _newSidecar;
    private readonly List<IExoCmdletRunner> _extra = [];
    private readonly List<string> _used = [];
    private readonly Lock _lock = new();

    /// <summary>The layers of a command.</summary>
    /// <param name="sidecar">The sidecar (one per command line run, started on its first cmdlet).</param>
    /// <param name="adminApi">The Admin API, or null when the run has none.</param>
    /// <param name="graph">Makes the Graph client, or null when the run has none.</param>
    /// <param name="newSidecar">Makes one more sidecar for a split bulk read, or null when the run cannot.</param>
    public ExchangeBackends(IExoCmdletRunner sidecar, IExoCmdletRunner? adminApi = null, Func<GraphClient>? graph = null, Func<IExoCmdletRunner>? newSidecar = null)
    {
        ArgumentNullException.ThrowIfNull(sidecar);
        _sidecar = sidecar;
        _adminApi = adminApi;
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
        }

        Use(Output.Backend.Pwsh);
        return sidecar;
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
}
