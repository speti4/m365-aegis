using M365Aegis.Core;
using M365Aegis.Core.Config;
using Photino.NET;

namespace M365Aegis.Gui.Host;

/// <summary>The Photino window: WebView2 on Windows, with its data folder under the tool's home, not Photino's.</summary>
internal sealed class PhotinoHost : IUiHost
{
    private readonly PhotinoWindow _window;

    internal PhotinoHost()
    {
        _window = new PhotinoWindow()
            .SetTitle(ProductInfo.Name)
            .SetUseOsDefaultSize(false)
            .SetSize(1100, 760)
            .Center()
            .SetTemporaryFilesPath(Path.Combine(AegisHome.Resolve(), "webview2"));
    }

    public void OnMessage(Func<string, string?> handler)
    {
        _window.RegisterWebMessageReceivedHandler((sender, message) =>
        {
            string? reply = handler(message);
            if (reply is not null && sender is PhotinoWindow window)
            {
                window.SendWebMessage(reply);
            }
        });
    }

    public void Run(Uri page)
    {
        _window.Load(page);
        _window.WaitForClose();
    }

    public void Close() => _window.Invoke(_window.Close);
}
