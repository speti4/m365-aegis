using System.Globalization;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Exchange.Sidecar;

namespace M365Aegis.Core.Exchange;

/// <summary>
/// The one Exchange Online token of a command (task 049): the sidecar's connect, the extra sidecars of a split read
/// and the Admin API all ask this source, which asks the sign-in once and hands the same token and expiry to each.
/// A token whose expiry is within <see cref="RenewBefore"/> is asked for again, once however many layers ask at the
/// same moment; the sidecar's <c>reconnect</c> reads the same threshold and clock (<see cref="ExchangeSessionOptions"/>),
/// so the renewal of the layers is one renewal. A failed acquisition is not kept: the next caller asks again.
/// Under <c>--verbose</c> each acquisition writes one <c>exchange-online</c> line to the log, never the token.
/// </summary>
public sealed class ExchangeTokenSource
{
    /// <summary>How long before the expiry a token is renewed: the sidecar session's default (task 015).</summary>
    public static readonly TimeSpan DefaultRenewBefore = TimeSpan.FromMinutes(5);

    private readonly Func<CancellationToken, Task<ExchangeToken>> _fetch;
    private readonly Lock _lock = new();
    private Task<ExchangeToken>? _token;
    private int _acquisitions;

    /// <summary>A source over <paramref name="fetch"/>, the silent sign-in for <c>https://outlook.office365.com/.default</c>.</summary>
    /// <param name="fetch">Asks the sign-in for a token; called on the first request and for each renewal.</param>
    /// <param name="clock">The clock the expiry is read against.</param>
    /// <param name="renewBefore">How long before the expiry the token is renewed; <see cref="DefaultRenewBefore"/> when null.</param>
    public ExchangeTokenSource(Func<CancellationToken, Task<ExchangeToken>> fetch, TimeProvider? clock = null, TimeSpan? renewBefore = null)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        _fetch = fetch;
        Clock = clock ?? TimeProvider.System;
        RenewBefore = renewBefore ?? DefaultRenewBefore;
    }

    /// <summary>The clock the expiry is read against.</summary>
    public TimeProvider Clock { get; }

    /// <summary>How long before the expiry the token is renewed.</summary>
    public TimeSpan RenewBefore { get; }

    /// <summary>How many times the source asked the sign-in for a token.</summary>
    public int Acquisitions
    {
        get
        {
            lock (_lock)
            {
                return _acquisitions;
            }
        }
    }

    /// <summary>The source of a profile's sign-in: <see cref="AuthService.GetTokenAsync"/> for Exchange Online, silently.</summary>
    public static ExchangeTokenSource ForProfile(AuthService auth, string? profileName, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(auth);
        return new ExchangeTokenSource(
            async cancellationToken => ExchangeConnection.ToExchangeToken(await auth.GetTokenAsync(Resource.Exchange, profileName, cancellationToken).ConfigureAwait(false)),
            clock);
    }

    /// <summary>Whether a token is due for renewal: its expiry is within <see cref="RenewBefore"/>.</summary>
    public bool IsDue(ExchangeToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return token.ExpiresOn - Clock.GetUtcNow() <= RenewBefore;
    }

    /// <summary>The token: the one already acquired while it is not due, else a fresh one, shared by every caller waiting for it.</summary>
    public Task<ExchangeToken> GetAsync(CancellationToken cancellationToken) => AcquireAsync(force: false, cancellationToken);

    /// <summary>A fresh token now, whatever the expiry of the current one (an explicit reconnect); the others get it too.</summary>
    public Task<ExchangeToken> RenewAsync(CancellationToken cancellationToken) => AcquireAsync(force: true, cancellationToken);

    private async Task<ExchangeToken> AcquireAsync(bool force, CancellationToken cancellationToken)
    {
        Task<ExchangeToken> pending;
        lock (_lock)
        {
            bool reuse = _token is not null
                && !_token.IsFaulted
                && !_token.IsCanceled
                && (!_token.IsCompletedSuccessfully || (!force && !IsDue(_token.Result)));
            if (!reuse)
            {
                _acquisitions++;
                _token = FetchAsync(_acquisitions, cancellationToken);
            }

            pending = _token!;
        }

        return await pending.ConfigureAwait(false);
    }

    private async Task<ExchangeToken> FetchAsync(int acquisition, CancellationToken cancellationToken)
    {
        ExchangeToken token = await _fetch(cancellationToken).ConfigureAwait(false);
        LogSink.Verbose?.Write("exchange-online", string.Create(
            CultureInfo.InvariantCulture,
            $"exchange token acquisition {acquisition}: one token for every layer of the command, valid for {(token.ExpiresOn - Clock.GetUtcNow()).TotalMinutes:0} min"));
        return token;
    }
}
