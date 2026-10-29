namespace M365Aegis.Gui.Host;

/// <summary>
/// The window the front end runs in, as the rest of the GUI sees it: load a page, exchange string messages, close.
/// A WinForms window with the WebView2 control implements it (<see cref="WinFormsWebView2Host"/>, D-009); the bridge
/// and the front end do not depend on the host.
/// </summary>
internal interface IUiHost
{
    /// <summary>
    /// The native handle of the top-level window, the parent window the broker's account picker needs (task 013);
    /// zero before the window exists.
    /// </summary>
    nint WindowHandle { get; }

    /// <summary>
    /// Called with every message the front end sends; the reply the task ends with, if any, is sent back. Several
    /// requests may be in flight at once; each reply carries its request's id.
    /// </summary>
    void OnMessage(Func<string, Task<string?>> handler);

    /// <summary>
    /// Sends a message the front end did not ask for (a notification, such as the device code of a sign-in in
    /// flight), from any thread; dropped when the page is not loaded or the window is closing.
    /// </summary>
    void Post(string message);

    /// <summary>
    /// Starts <paramref name="work"/> on the window's UI thread and completes when it does (MSAL starts the broker's
    /// account picker there, task 040); runs it where it is called when there is no window.
    /// </summary>
    Task InvokeOnUiThreadAsync(Func<Task> work);

    /// <summary>Opens the window on the given page and blocks until it is closed.</summary>
    void Run(Uri page);

    /// <summary>Closes the window from another thread.</summary>
    void Close();
}
