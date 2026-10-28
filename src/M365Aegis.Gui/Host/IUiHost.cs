namespace M365Aegis.Gui.Host;

/// <summary>
/// The window the front end runs in, as the rest of the GUI sees it: load a page, exchange string messages, close.
/// Photino implements it today (<see cref="PhotinoHost"/>); a WinForms + WebView2 host can replace it without
/// touching the bridge or the front end (task 014).
/// </summary>
internal interface IUiHost
{
    /// <summary>Called with every message the front end sends; the return value, if any, is sent back.</summary>
    void OnMessage(Func<string, string?> handler);

    /// <summary>Opens the window on the given page and blocks until it is closed.</summary>
    void Run(Uri page);

    /// <summary>Closes the window from another thread.</summary>
    void Close();
}
