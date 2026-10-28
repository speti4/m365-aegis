using System.Globalization;
using M365Aegis.Core.Bridge;
using M365Aegis.Gui.Host;

namespace M365Aegis.Gui;

/// <summary>
/// The m365-aegis-gui executable. Loads the built front end from <c>wwwroot/</c> next to the exe, or a Vite dev
/// server with <c>--dev-url http://localhost:5173</c>; <c>--smoke</c> exits 0 as soon as the front end reports
/// <c>ready</c> (CI's check that the window, the page and the bridge work) and 1 after a minute without it.
/// </summary>
internal static class Program
{
    private const int SmokeTimeoutMs = 60_000;

    [STAThread]
    internal static int Main(string[] args)
    {
        bool smoke = args.Contains("--smoke", StringComparer.Ordinal);
        Uri page = ResolvePage(args);

        IUiHost host = CreateHost();
        int exitCode = smoke ? 1 : 0;
        using var timeout = new CancellationTokenSource();

        host.OnMessage(text =>
        {
            BridgeMessage? message = BridgeProtocol.TryParse(text);
            if (message is null)
            {
                return null;
            }

            if (smoke && message.Type == "ready")
            {
                exitCode = 0;
                timeout.Cancel();
                host.Close();
                return null;
            }

            BridgeMessage? reply = BridgeDispatcher.Dispatch(message);
            return reply is null ? null : BridgeProtocol.Write(reply);
        });

        if (smoke)
        {
            _ = Task.Delay(SmokeTimeoutMs, timeout.Token).ContinueWith(
                _ =>
                {
                    Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture, "smoke: no ready message within {0} ms", SmokeTimeoutMs));
                    host.Close();
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }

        host.Run(page);
        return exitCode;
    }

    // The one place the host implementation is named; task 014 may put a WinForms + WebView2 host here.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance",
        Justification = "The interface is the seam a second host implementation plugs into (task 014).")]
    private static IUiHost CreateHost() => new PhotinoHost();

    private static Uri ResolvePage(string[] args)
    {
        int index = Array.IndexOf(args, "--dev-url");
        if (index >= 0 && index + 1 < args.Length && Uri.TryCreate(args[index + 1], UriKind.Absolute, out Uri? devUrl))
        {
            return devUrl;
        }

        string indexHtml = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        return new Uri(indexHtml);
    }
}
