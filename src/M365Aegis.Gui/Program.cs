using System.Globalization;
using M365Aegis.Core.Bridge;
using M365Aegis.Core.Config;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Gui.Host;

namespace M365Aegis.Gui;

/// <summary>
/// The m365-aegis-gui executable. Loads the built front end from <c>wwwroot/</c> next to the exe, or a Vite dev
/// server with <c>--dev-url http://localhost:5173</c>, and answers the page's requests through the Core's
/// <see cref="BridgeDispatcher"/> with the sign-in, the Graph client and the Exchange Online session the command line
/// uses (the default tenant profile of the home folder): the broker's account picker over this window, started on its
/// UI thread (task 040), and one sidecar for the window's lifetime, started on the first mailbox request and ended
/// with the window (task 045). <c>--verbose</c> writes MSAL's log, without PII and scrubbed, into the home
/// folder's <c>logs</c> folder, as the command line's option does (task 027). <c>--smoke</c> exits 0 as soon as the
/// front end reports <c>ready</c> (CI's check that the window, the page and the bridge work) and 1 after a minute
/// without it, and prints the time to <c>ready</c> and whether the window handle is a top-level window (the
/// broker's parent); a smoke run answers no request, so it never reaches the sign-in, the network or a sidecar.
/// </summary>
internal static class Program
{
    private const int SmokeTimeoutMs = 60_000;

    [STAThread]
    internal static int Main(string[] args)
    {
        bool smoke = args.Contains("--smoke", StringComparer.Ordinal);
        Uri page = ResolvePage(args);
        string home = AegisHome.Resolve();
        if (args.Contains("--verbose", StringComparer.Ordinal))
        {
            // Before the dispatcher builds any MSAL client, so every sign-in of the window logs into the sink.
            LogSink.EnableVerbose(home);
        }

        IUiHost host = CreateHost();
        BridgeServices services = BridgeServices.ForWindow(home, profile: null, () => host.WindowHandle, host.InvokeOnUiThreadAsync);
        var dispatcher = new BridgeDispatcher(services);
        var started = System.Diagnostics.Stopwatch.StartNew();
        int exitCode = smoke ? 1 : 0;
        using var timeout = new CancellationTokenSource();
        using var closing = new CancellationTokenSource();

        host.OnMessage(async text =>
        {
            BridgeMessage? message = BridgeProtocol.TryParse(text);
            if (message is null)
            {
                return null;
            }

            if (smoke)
            {
                if (message.Type == BridgeMessageType.Ready)
                {
                    nint window = host.WindowHandle;
                    Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "smoke: ready after {0} ms, window 0x{1:x} ({2})", started.ElapsedMilliseconds, window,
                        WindowCheck.Describe(window)));
                    exitCode = 0;
                    await timeout.CancelAsync().ConfigureAwait(true);
                    host.Close();
                }

                return null;
            }

            BridgeMessage? reply = await dispatcher.DispatchAsync(
                message,
                notification =>
                {
                    host.Post(BridgeProtocol.Write(notification));
                    return Task.CompletedTask;
                },
                closing.Token).ConfigureAwait(true);
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
        closing.Cancel();
        if (services.Exchange is { } exchange)
        {
            // The window's sidecar disconnects and exits with the window. Off the UI thread's context, which is gone.
            Task.Run(() => exchange.DisposeAsync().AsTask()).GetAwaiter().GetResult();
        }

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
