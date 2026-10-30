namespace M365Aegis.Core.Bridge;

/// <summary>
/// The one kind of address the window hands to the default browser (task 047): the page of a device-code sign-in,
/// as the sign-in service named it, on one of Microsoft's sign-in hosts over https. The window itself never
/// navigates to another origin; the host opens this page with the shell, outside the WebView2, and only the address
/// the device-code answer carried, never a string from the front end.
/// </summary>
public static class SignInPage
{
    /// <summary>
    /// The hosts a device-code page may be on: <c>microsoft.com/devicelogin</c>, the identity platform's
    /// <c>login.microsoftonline.com</c> and <c>login.microsoft.com</c> (https://learn.microsoft.com/entra/identity-platform/v2-oauth2-device-code).
    /// </summary>
    public static IReadOnlyList<string> AllowedHosts { get; } =
        ["microsoft.com", "www.microsoft.com", "login.microsoftonline.com", "login.microsoft.com"];

    /// <summary>Whether the address may be opened: absolute, https on the default port, no user info, an allowed host.</summary>
    public static bool IsAllowed(Uri? page) =>
        page is { IsAbsoluteUri: true }
        && page.Scheme == Uri.UriSchemeHttps
        && page.IsDefaultPort
        && string.IsNullOrEmpty(page.UserInfo)
        && AllowedHosts.Contains(page.IdnHost, StringComparer.OrdinalIgnoreCase);
}
