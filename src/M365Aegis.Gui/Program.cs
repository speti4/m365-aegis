using System.Globalization;
using M365Aegis.Core.Bridge;
using M365Aegis.Gui.Host;

namespace M365Aegis.Gui;

/// <summary>
/// The m365-aegis-gui executable. Loads the built front end from <c>wwwroot/</c> next to the exe, or a Vite dev
/// server with <c>--dev-url http://localhost:5173</c>; <c>--smoke</c> exits 0 as soon as the front end reports
/// <c>ready</c> (CI's check that the window, the page and the bridge work) and 1 after a minute without it, and
/// prints the time to <c>ready</c> and whether the window handle is a top-level window (the broker's parent).
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
        var started = System.Diagnostics.Stopwatch.StartNew();
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
                nint window = host.WindowHandle;
                Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "smoke: ready after {0} ms, window 0x{1:x} ({2})", started.ElapsedMilliseconds, window,
                    WindowCheck.Describe(window)));
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

    // The one place the host implementation is named (D-009).
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance",
        Justification = "The interface is the seam between the window and the rest of the GUI (D-009).")]
    private static IUiHost CreateHost() => new WinFormsWebView2Host();

    private static string? OptionValue(string[] args, string option)
    {
        int index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static Uri ResolvePage(string[] args)
    {
        if (Uri.TryCreate(OptionValue(args, "--dev-url"), UriKind.Absolute, out Uri? devUrl))
        {
            return devUrl;
        }

        string indexHtml = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        return new Uri(indexHtml);
    }
}
