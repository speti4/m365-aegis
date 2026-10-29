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

    /// <summary>Called with every message the front end sends; the return value, if any, is sent back.</summary>
    void OnMessage(Func<string, string?> handler);

    /// <summary>Opens the window on the given page and blocks until it is closed.</summary>
    void Run(Uri page);

    /// <summary>Closes the window from another thread.</summary>
    void Close();
}
