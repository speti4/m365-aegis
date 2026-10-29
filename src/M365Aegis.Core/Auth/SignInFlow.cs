using System.Globalization;
using M365Aegis.Core.Config;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Auth;

/// <summary>How far a sign-in may go.</summary>
public enum SignInMode
{
    /// <summary>The silent paths only; <c>whoami</c> and every lookup never prompt.</summary>
    SilentOnly,

    /// <summary>Silent first, then the account picker, then device code when there is no window (<c>login</c>).</summary>
    Interactive,

    /// <summary>Device code straight away (<c>login --device-code</c>).</summary>
    DeviceCode,
}

/// <summary>
/// The fallback order task 013 measured (D-003, note of 2026-10-29): silent as the operating system account, silent
/// as the profile's account chosen by user principal name among the broker's accounts (never the first in the
/// list), the account picker through the broker, device code when the broker answers that it has no window.
/// Pure logic over <see cref="ITokenBroker"/>; every exhausted path is an <see cref="FailureKind.Auth"/> failure
/// with a hint.
/// </summary>
public static class SignInFlow
{
    /// <summary>Runs the flow for one resource.</summary>
    /// <param name="broker">The token source.</param>
    /// <param name="profile">Whose account, in which tenant.</param>
    /// <param name="resource">Which token.</param>
    /// <param name="mode">How far the flow may go.</param>
    /// <param name="prompt">Shows the device code to the user when that path is reached.</param>
    /// <param name="cancellationToken">Cancels the flow.</param>
    /// <exception cref="AegisException">No path produced a token.</exception>
    public static async Task<TokenResult> RunAsync(
        ITokenBroker broker,
        TenantProfile profile,
        Resource resource,
        SignInMode mode,
        Func<DeviceCodePrompt, Task> prompt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(prompt);

        string scope = Scopes.For(resource);
        var attempts = new List<string>();

        if (mode != SignInMode.DeviceCode)
        {
            // Entra-joined machine: the signed-in Windows account answers at once. A registered-only machine has no
            // primary refresh token and fails here in well under a second; that is the normal case, not an error.
            try
            {
                return await broker.AcquireSilentWithOperatingSystemAccountAsync(scope, cancellationToken).ConfigureAwait(false);
            }
            catch (BrokerException exception)
            {
                attempts.Add($"operating system account: {exception.Code}");
            }

            // The broker can list several accounts of one tenant; the profile's user principal name picks one.
            IReadOnlyList<AccountInfo> accounts = await broker.GetAccountsAsync(cancellationToken).ConfigureAwait(false);
            AccountInfo? known = accounts.FirstOrDefault(account =>
                string.Equals(account.Username, profile.Account, StringComparison.OrdinalIgnoreCase));
            if (known is null)
            {
                attempts.Add(string.Format(CultureInfo.InvariantCulture, "{0} is not among the {1} known account(s)", profile.Account, accounts.Count));
            }
            else
            {
                try
                {
                    return await broker.AcquireSilentAsync(scope, known, cancellationToken).ConfigureAwait(false);
                }
                catch (BrokerException exception)
                {
                    attempts.Add($"{profile.Account}: {exception.Code}");
                }
            }

            if (mode == SignInMode.SilentOnly)
            {
                throw new AegisException(
                    FailureKind.Auth,
                    $"no silent sign-in for {profile.Account} ({string.Join("; ", attempts)})",
                    "run `m365-aegis login`");
            }

            try
            {
                return await broker.AcquireInteractiveAsync(scope, profile.Account, cancellationToken).ConfigureAwait(false);
            }
            catch (BrokerException exception) when (exception.Kind == BrokerErrorKind.WindowHandleRequired)
            {
                // No console window (a redirected shell, a scheduled task): the broker refuses at once, device code takes over.
                attempts.Add($"account picker: {exception.Code}");
            }
            catch (BrokerException exception) when (exception.Kind == BrokerErrorKind.Cancelled)
            {
                throw new AegisException(FailureKind.Auth, "the sign-in was cancelled in the account picker", "run `m365-aegis login` again", exception);
            }
            catch (BrokerException exception)
            {
                throw new AegisException(
                    FailureKind.Auth,
                    $"the account picker could not sign in {profile.Account} ({exception.Code}: {exception.Message})",
                    "run `m365-aegis login --device-code` to sign in through a browser instead",
                    exception);
            }
        }

        try
        {
            return await broker.AcquireWithDeviceCodeAsync(scope, prompt, cancellationToken).ConfigureAwait(false);
        }
        catch (BrokerException exception)
        {
            throw new AegisException(
                FailureKind.Auth,
                $"the device code sign-in failed ({exception.Code}: {exception.Message})",
                "run `m365-aegis login --device-code` again and finish the browser step before the code expires",
                exception);
        }
    }
}
