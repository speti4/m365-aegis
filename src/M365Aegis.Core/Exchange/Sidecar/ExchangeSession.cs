using M365Aegis.Core.Output;

namespace M365Aegis.Core.Exchange.Sidecar;

/// <summary>Runs Exchange Online cmdlets: the seam the mailbox lookups are written against (the sidecar, or a test's fake).</summary>
public interface IExoCmdletRunner
{
    /// <summary>Runs one cmdlet of <see cref="ExoCmdlets.Allowed"/>; any other is refused before anything is sent.</summary>
    Task<ExoResult> InvokeAsync(ExoCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// A token for Exchange Online as the session uses it: the bearer token, when it expires, and what it carries. The
/// record's <c>ToString</c> leaves the token out, so it cannot reach a log by accident.
/// </summary>
/// <param name="AccessToken">The bearer token for <c>https://outlook.office365.com</c>.</param>
/// <param name="ExpiresOn">When it expires (about an hour after it was issued).</param>
/// <param name="Permissions">The delegated permissions it carries (<c>Exchange.Manage</c>, <c>Exchange.ManageV2</c>).</param>
/// <param name="Account">The account it was issued to.</param>
/// <param name="TenantId">The tenant that issued it, or null.</param>
public sealed record ExchangeToken(string AccessToken, DateTimeOffset ExpiresOn, IReadOnlyList<string> Permissions, string Account, string? TenantId)
{
    /// <inheritdoc/>
    public override string ToString() => $"{nameof(ExchangeToken)} {{ Account = {Account}, ExpiresOn = {ExpiresOn:u} }}";
}

/// <summary>Which parameters <c>Connect-ExchangeOnline -AccessToken</c> gets besides the token (task 009).</summary>
public enum ExchangeConnectWith
{
    /// <summary><c>-Organization &lt;tenant&gt;.onmicrosoft.com</c>.</summary>
    Organization,

    /// <summary><c>-UserPrincipalName &lt;account&gt;</c>.</summary>
    UserPrincipalName,

    /// <summary>Both.</summary>
    Both,
}

/// <summary>Where the session connects: the organization's <c>.onmicrosoft.com</c> name and the account.</summary>
/// <param name="Organization">The organization, or null when it is not passed.</param>
/// <param name="UserPrincipalName">The account, or null when it is not passed.</param>
public sealed record ExchangeTarget(string? Organization, string? UserPrincipalName);

/// <summary>What an <see cref="ExchangeSession"/> is made of; the factories are seams the tests replace.</summary>
public sealed record ExchangeSessionOptions
{
    /// <summary>Starts a sidecar (pwsh with the script), answering <c>hello</c>.</summary>
    public required Func<CancellationToken, Task<SidecarClient>> Start { get; init; }

    /// <summary>A token for Exchange Online, silently; called for the connect and before each renewal.</summary>
    public required Func<CancellationToken, Task<ExchangeToken>> Token { get; init; }

    /// <summary>
    /// A fresh token for an explicit reconnect (<see cref="ExchangeSession.RenewAsync"/>), whatever the expiry of the
    /// current one; <see cref="Token"/> when null. A shared <see cref="ExchangeTokenSource"/> passes its
    /// <see cref="ExchangeTokenSource.RenewAsync"/>, so the other layers get the fresh token too (task 049).
    /// </summary>
    public Func<CancellationToken, Task<ExchangeToken>>? RenewToken { get; init; }

    /// <summary>The organization and the account to connect with; called once per session.</summary>
    public required Func<CancellationToken, Task<ExchangeTarget>> Target { get; init; }

    /// <summary>The clock the expiry is read against.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>How long before the token's expiry the session reconnects with a fresh one.</summary>
    public TimeSpan RenewBefore { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// One Exchange Online session over the sidecar (D-004, task 015): starts the sidecar on the first cmdlet, connects
/// it with the broker's token (<c>Connect-ExchangeOnline -AccessToken</c>), renews the token with a
/// <c>reconnect</c> before it expires, admits only the cmdlets of <see cref="ExoCmdlets"/>, and starts a new sidecar
/// when the old one died. The command line holds one per run; the window keeps one for its lifetime, which is why
/// it is safe to call from several tasks (one cmdlet runs at a time) and renews on its own.
/// </summary>
public sealed class ExchangeSession : IExoCmdletRunner, IAsyncDisposable
{
    private readonly ExchangeSessionOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SidecarClient? _client;
    private ExchangeTarget? _target;
    private bool _disposed;

    /// <summary>A session that starts nothing until the first cmdlet.</summary>
    public ExchangeSession(ExchangeSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>The sidecar's <c>hello</c> of the current process, or null before the start.</summary>
    public SidecarHello? Hello => _client?.Hello;

    /// <summary>The last connect or reconnect, or null before the first.</summary>
    public SidecarConnected? Connection { get; private set; }

    /// <summary>The token of the current connection, or null before the first.</summary>
    public ExchangeToken? Token { get; private set; }

    /// <summary>How many times the session reconnected with a fresh token.</summary>
    public int Renewals { get; private set; }

    /// <summary>How many sidecars the session started (more than one when a sidecar died under it).</summary>
    public int Starts { get; private set; }

    /// <summary>The current sidecar, for the tests.</summary>
    internal SidecarClient? Client => _client;

    /// <inheritdoc/>
    public async Task<ExoResult> InvokeAsync(ExoCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ExoCmdlets.EnsureAllowed(command.Cmdlet);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SidecarClient client = await ReadyAsync(cancellationToken).ConfigureAwait(false);
            return await client.InvokeAsync(command, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Starts and connects the sidecar when it is not already; the connection.</summary>
    public async Task<SidecarConnected> ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReadyAsync(cancellationToken).ConfigureAwait(false);
            return Connection!;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Reconnects now with a freshly asked token, whatever its expiry (the probe of task 009 times it).</summary>
    public async Task<SidecarConnected> RenewAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SidecarClient client = await ReadyAsync(cancellationToken).ConfigureAwait(false);
            await ReconnectAsync(client, fresh: true, cancellationToken).ConfigureAwait(false);
            return Connection!;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Ends the sidecar (it disconnects first).</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
            _client = null;
        }

        _gate.Dispose();
    }

    /// <summary>The connected sidecar: started, connected, and renewed when its token is about to expire.</summary>
    private async Task<SidecarClient> ReadyAsync(CancellationToken cancellationToken)
    {
        if (_client is not null && _client.HasExited)
        {
            // The window outlived its sidecar (killed, crashed): a new one, connected again.
            await _client.DisposeAsync().ConfigureAwait(false);
            _client = null;
            Connection = null;
        }

        if (_client is null)
        {
            // The sign-in first: without a profile or a token no pwsh is started at all.
            _target ??= await _options.Target(cancellationToken).ConfigureAwait(false);
            ExchangeToken token = await _options.Token(cancellationToken).ConfigureAwait(false);
            SidecarClient client = await _options.Start(cancellationToken).ConfigureAwait(false);
            Starts++;
            try
            {
                Check(client.Hello);
                Connection = await client.ConnectAsync(Connect(token), again: false, cancellationToken).ConfigureAwait(false);
                Token = token;
            }
            catch
            {
                await client.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            _client = client;
            return client;
        }

        if (Token is null || Token.ExpiresOn - _options.Clock.GetUtcNow() <= _options.RenewBefore)
        {
            await ReconnectAsync(_client, fresh: false, cancellationToken).ConfigureAwait(false);
        }

        return _client;
    }

    private async Task ReconnectAsync(SidecarClient client, bool fresh, CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task<ExchangeToken>> source = fresh ? _options.RenewToken ?? _options.Token : _options.Token;
        ExchangeToken token = await source(cancellationToken).ConfigureAwait(false);
        Connection = await client.ConnectAsync(Connect(token), again: true, cancellationToken).ConfigureAwait(false);
        Token = token;
        Renewals++;
    }

    private SidecarConnect Connect(ExchangeToken token) =>
        new(token.AccessToken, _target!.Organization, _target.UserPrincipalName);

    /// <summary>The script's own answer: a module to connect with, and the same allow-list as the core's.</summary>
    private static void Check(SidecarHello hello)
    {
        if (hello.Module is null)
        {
            throw new AegisException(
                FailureKind.SidecarUnavailable,
                "the ExchangeOnlineManagement module is not installed",
                "Install-Module ExchangeOnlineManagement -Scope CurrentUser");
        }

        if (!hello.AllowedCmdlets.SequenceEqual(ExoCmdlets.Allowed, StringComparer.OrdinalIgnoreCase))
        {
            throw new AegisException(
                FailureKind.SidecarUnavailable,
                "the sidecar script's allow-list differs from this build's",
                "reinstall the release zip, or rebuild; `m365-aegis doctor` checks the script");
        }
    }
}
