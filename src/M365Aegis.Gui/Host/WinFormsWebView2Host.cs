using M365Aegis.Core;
using M365Aegis.Core.Config;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace M365Aegis.Gui.Host;

/// <summary>
/// A WinForms window with Microsoft's WebView2 control. The shipped front end is served from <c>wwwroot/</c> under a
/// virtual host (<c>https://m365-aegis.example/</c>, <c>SetVirtualHostNameToFolderMapping</c>), so the page has an
/// https origin and its ES modules load without any file access flag; a <c>--dev-url</c> page is loaded as given.
/// The bridge is WebView2's own: the page posts with <c>chrome.webview.postMessage</c> and listens on
/// <c>chrome.webview</c>'s <c>message</c> event (task 030), the host answers with <c>PostWebMessageAsString</c>; no
/// script is injected. Messages are taken from the page's own origin only, and navigation away from it is cancelled.
/// </summary>
internal sealed class WinFormsWebView2Host : IUiHost
{
    /// <summary>The virtual host of the shipped front end; <c>.example</c> is reserved (RFC 6761), never a real site.</summary>
    internal const string AppHostName = "m365-aegis.example";

    private Func<string, Task<string?>>? _handler;
    private Form? _form;
    private CoreWebView2? _core;
    private string? _origin;
    private volatile bool _closeRequested;

    public nint WindowHandle { get; private set; }

    public void OnMessage(Func<string, Task<string?>> handler) => _handler = handler;

    public void Post(string message)
    {
        Form? form = _form;
        if (form is null || !form.IsHandleCreated)
        {
            return;
        }

        try
        {
            // CoreWebView2 belongs to the UI thread: the post is marshalled there.
            form.BeginInvoke(() => PostOnUiThread(message));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            // The window closed in between: nobody is left to read the message.
        }
    }

    public Task InvokeOnUiThreadAsync(Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        Form? form = _form;
        if (form is null || !form.IsHandleCreated || !form.InvokeRequired)
        {
            return work();
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        form.BeginInvoke(() =>
        {
            Task task;
            try
            {
                task = work();
            }
            catch (Exception exception)
            {
                done.TrySetException(exception);
                return;
            }

            task.ContinueWith(
                finished =>
                {
                    if (finished.IsCanceled)
                    {
                        done.TrySetCanceled();
                    }
                    else if (finished.Exception is { } failure)
                    {
                        done.TrySetException(failure.InnerExceptions);
                    }
                    else
                    {
                        done.TrySetResult();
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        });
        return done.Task;
    }

    public void Run(Uri page)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var form = new Form
        {
            Text = ProductInfo.Name,
            AutoScaleDimensions = new SizeF(96F, 96F),
            AutoScaleMode = AutoScaleMode.Dpi,
            ClientSize = new Size(1100, 760),
            StartPosition = FormStartPosition.CenterScreen,
        };
        using var webView = new WebView2 { Dock = DockStyle.Fill };
        form.Controls.Add(webView);
        form.HandleCreated += (_, _) => WindowHandle = form.Handle;
        form.Load += async (_, _) =>
        {
            try
            {
                await InitializeAsync(webView, page);
            }
            catch (Exception exception) when (exception is WebView2RuntimeNotFoundException or InvalidOperationException
                                              or UnauthorizedAccessException or IOException
                                              or System.Runtime.InteropServices.COMException)
            {
                Console.Error.WriteLine($"winforms host: WebView2 failed to start: {exception.Message}");
                form.Close();
            }
        };
        _form = form;
        Application.Run(form);
        _form = null;
        _core = null;
    }

    public void Close()
    {
        Form? form = _form;
        if (form is null || !form.IsHandleCreated)
        {
            _closeRequested = true;
            return;
        }

        // Posted, never called inline: Close may come from inside a WebView2 event or from another thread.
        form.BeginInvoke(form.Close);
    }

    private async Task InitializeAsync(WebView2 webView, Uri page)
    {
        string userDataFolder = Path.Combine(AegisHome.Resolve(), "webview2");
        CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await webView.EnsureCoreWebView2Async(environment);
        CoreWebView2 core = webView.CoreWebView2;
        _core = core;

        // The developer tools and the browser's context menu only for a dev server page.
        bool development = !page.IsFile;
        core.Settings.AreDevToolsEnabled = development;
        core.Settings.AreDefaultContextMenusEnabled = development;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsStatusBarEnabled = false;

        Uri start = page;
        if (page.IsFile)
        {
            string folder = Path.GetDirectoryName(page.LocalPath) ?? AppContext.BaseDirectory;
            core.SetVirtualHostNameToFolderMapping(AppHostName, folder, CoreWebView2HostResourceAccessKind.Deny);
            start = new Uri($"https://{AppHostName}/{Path.GetFileName(page.LocalPath)}");
        }

        _origin = start.GetLeftPart(UriPartial.Authority);
        core.WebMessageReceived += OnWebMessageReceived;
        core.NavigationStarting += (_, e) =>
        {
            if (!IsOwnOrigin(e.Uri))
            {
                e.Cancel = true;
            }
        };
        core.NewWindowRequested += (_, e) => e.Handled = true;

        if (_closeRequested)
        {
            _form?.BeginInvoke(_form.Close);
            return;
        }

        core.Navigate(start.AbsoluteUri);
    }

    // An event handler, so async void: the handler's task runs off the UI thread where it waits on a service, and the
    // await comes back to the UI thread, where the reply is posted (CoreWebView2 belongs to that thread).
    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsOwnOrigin(e.Source) || sender is not CoreWebView2 core || _handler is not { } handler)
        {
            return;
        }

        string text;
        try
        {
            text = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            return; // not a string message: not of this protocol
        }

        string? reply;
        try
        {
            reply = await handler(text).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return; // the window is closing
        }

        if (reply is null || _form is null)
        {
            return;
        }

        try
        {
            core.PostWebMessageAsString(reply);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException
                                          or System.Runtime.InteropServices.COMException)
        {
            // The window closed while the request was in flight: nobody is left to read the reply.
        }
    }

    private void PostOnUiThread(string message)
    {
        if (_core is not { } core || _form is null)
        {
            return;
        }

        try
        {
            core.PostWebMessageAsString(message);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException
                                          or System.Runtime.InteropServices.COMException)
        {
            // The window closed while the message was on its way.
        }
    }

    private bool IsOwnOrigin(string uri) =>
        _origin is not null && Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed)
        && string.Equals(parsed.GetLeftPart(UriPartial.Authority), _origin, StringComparison.OrdinalIgnoreCase);
}
