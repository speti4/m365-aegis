using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Exchange.Sidecar;

/// <summary>How to start a sidecar: pwsh, the script, and environment variables to add.</summary>
/// <param name="PwshPath">The pwsh executable.</param>
/// <param name="ScriptPath">The sidecar script.</param>
public sealed record SidecarStartInfo(string PwshPath, string ScriptPath)
{
    /// <summary>Environment variables set for the child on top of this process's own.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    /// <summary>How long the start and the <c>hello</c> may take (a cold pwsh takes about a second).</summary>
    public TimeSpan HelloTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>How long a connect may take (the module's import and the service's answer).</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>How long one cmdlet run may take (a bulk run over every mailbox of a tenant included).</summary>
    public TimeSpan InvokeTimeout { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>How long the child may take to end after <c>exit</c> before it is killed (the tests shorten it).</summary>
    internal TimeSpan ExitTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>The sidecar's answer to <c>hello</c>, and how long the start took.</summary>
/// <param name="Pwsh">The pwsh version the script runs on.</param>
/// <param name="Module">The ExchangeOnlineManagement version it found, or null when it found none.</param>
/// <param name="AllowedCmdlets">The script's own allow-list.</param>
/// <param name="StartMs">From starting the process to the answer, in milliseconds.</param>
/// <param name="ModuleBase">The module's folder, or null (the measurement's OneDrive check; never printed).</param>
/// <param name="ModulePath">The child's <c>PSModulePath</c>, or null (the same).</param>
public sealed record SidecarHello(string Pwsh, string? Module, IReadOnlyList<string> AllowedCmdlets, long StartMs, string? ModuleBase = null, string? ModulePath = null);

/// <summary>What the sidecar was asked to connect with. The token stays in the core and the child: never logged.</summary>
/// <param name="AccessToken">The bearer token for <c>outlook.office365.com</c>.</param>
/// <param name="Organization">The <c>.onmicrosoft.com</c> name for <c>-Organization</c>, or null.</param>
/// <param name="UserPrincipalName">The account for <c>-UserPrincipalName</c>, or null.</param>
public sealed record SidecarConnect(string AccessToken, string? Organization, string? UserPrincipalName)
{
    /// <summary><c>-SkipLoadingFormatData</c>; on unless the measurement of task 011 turns it off.</summary>
    public bool SkipLoadingFormatData { get; init; } = true;

    /// <summary><c>-CommandName</c> with the REST cmdlets of the allow-list; on unless the measurement turns it off.</summary>
    public bool CommandName { get; init; } = true;

    /// <summary>Whether the reply names the allowed cmdlets the session does not answer (the measurement's check).</summary>
    public bool CheckCommands { get; init; }

    /// <inheritdoc/>
    public override string ToString() =>
        $"{nameof(SidecarConnect)} {{ Organization = {Organization}, UserPrincipalName = {UserPrincipalName} }}";
}

/// <summary>The sidecar's answer to <c>connect</c> and <c>reconnect</c>.</summary>
/// <param name="Organization">The organization it connected to, or null.</param>
/// <param name="UserPrincipalName">The account it connected as, or null.</param>
/// <param name="Module">The module version.</param>
/// <param name="ImportMs">The module import, in milliseconds.</param>
/// <param name="ConnectMs">The <c>Connect-ExchangeOnline</c> call, in milliseconds.</param>
/// <param name="Missing">With <see cref="SidecarConnect.CheckCommands"/>: the allowed cmdlets the session does not answer.</param>
public sealed record SidecarConnected(string? Organization, string? UserPrincipalName, string? Module, long ImportMs, long ConnectMs, IReadOnlyList<string>? Missing = null);

/// <summary>An error reply of the sidecar: its code, its message and, for a cmdlet error, PowerShell's ids.</summary>
/// <param name="Code">The protocol's error code (<c>cmdlet-failed</c>, <c>connect-failed</c>, …).</param>
/// <param name="Message">The message, with any token already scrubbed by the script.</param>
/// <param name="ErrorId">The error record's <c>FullyQualifiedErrorId</c>, or null.</param>
/// <param name="Category">The error record's category reason, or null.</param>
public sealed record SidecarError(string Code, string Message, string? ErrorId, string? Category);

/// <summary>
/// The Exchange Online sidecar as a child process and the client of its protocol (D-004, task 015): pwsh runs
/// <c>aegis-exo-sidecar.ps1</c>, and this class writes one JSON request per line to its stdin and reads the reply
/// with the same id from its stdout; its stderr is kept, the last lines only, for the message of a failure. One
/// request is in flight at a time. An error reply becomes the failure of the contract
/// (<see cref="SidecarFailures"/>); a process that died or stopped answering is
/// <see cref="FailureKind.SidecarUnavailable"/>. <see cref="ExchangeSession"/> holds one for a command or for the
/// window's lifetime.
/// </summary>
public sealed class SidecarClient : IAsyncDisposable
{
    private const int ProtocolVersion = 1;
    private const int StderrLines = 20;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Process _process;
    private readonly SidecarStartInfo _start;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<string> _stderr = new();
    private readonly Lock _stderrLock = new();
    private readonly Task _stderrPump;
    private int _nextId;
    private bool _disposed;

    private SidecarClient(Process process, SidecarStartInfo start)
    {
        _process = process;
        _start = start;
        _stderrPump = Task.Run(PumpStderrAsync);
    }

    /// <summary>The answer to the <c>hello</c> of the start.</summary>
    public SidecarHello Hello { get; private set; } = null!;

    /// <summary>Whether the child has ended.</summary>
    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    /// <summary>Starts pwsh with the script and waits for the answer to <c>hello</c>.</summary>
    public static async Task<SidecarClient> StartAsync(SidecarStartInfo start, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        var stopwatch = Stopwatch.StartNew();
        var startInfo = new ProcessStartInfo(start.PwshPath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-NoLogo", "-File", start.ScriptPath })
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach ((string name, string value) in start.Environment)
        {
            startInfo.Environment[name] = value;
        }

        Process process;
        try
        {
            // Without the shell, Process.Start starts a process or throws; it returns null only for a shell reuse.
            process = Process.Start(startInfo)!;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            throw new AegisException(
                FailureKind.SidecarUnavailable,
                $"the Exchange Online sidecar did not start ({start.PwshPath}: {exception.Message})",
                "run `m365-aegis doctor`: it checks pwsh, the module and the sidecar script",
                exception);
        }

        var client = new SidecarClient(process, start);
        try
        {
            JsonElement hello = await client.SendAsync("hello", null, start.HelloTimeout, cancellationToken).ConfigureAwait(false);
            client.Hello = new SidecarHello(
                Text(hello, "pwsh") ?? "?",
                Text(hello, "module"),
                hello.TryGetProperty("allowedCmdlets", out JsonElement allowed) && allowed.ValueKind == JsonValueKind.Array
                    ? allowed.EnumerateArray().Select(item => item.GetString() ?? "").ToList()
                    : [],
                stopwatch.ElapsedMilliseconds,
                Text(hello, "moduleBase"),
                Text(hello, "modulePath"));
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary><c>connect</c>, or <c>reconnect</c> with <paramref name="again"/>: the session with the given token.</summary>
    public async Task<SidecarConnected> ConnectAsync(SidecarConnect connect, bool again, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connect);
        var data = new Dictionary<string, object?> { ["accessToken"] = connect.AccessToken };
        if (connect.Organization is not null)
        {
            data["organization"] = connect.Organization;
        }

        if (connect.UserPrincipalName is not null)
        {
            data["userPrincipalName"] = connect.UserPrincipalName;
        }

        if (!connect.SkipLoadingFormatData)
        {
            data["skipLoadingFormatData"] = false;
        }

        if (!connect.CommandName)
        {
            data["commandName"] = false;
        }

        if (connect.CheckCommands)
        {
            data["checkCommands"] = true;
        }

        JsonElement reply = await SendAsync(again ? "reconnect" : "connect", data, _start.ConnectTimeout, cancellationToken).ConfigureAwait(false);
        return new SidecarConnected(
            Text(reply, "organization"),
            Text(reply, "userPrincipalName"),
            Text(reply, "module"),
            Number(reply, "importMs"),
            Number(reply, "connectMs"),
            reply.TryGetProperty("missing", out JsonElement missing) && missing.ValueKind == JsonValueKind.Array
                ? missing.EnumerateArray().Select(item => item.GetString() ?? "").ToList()
                : null);
    }

    /// <summary><c>invoke</c>: runs one cmdlet; the caller has checked it against <see cref="ExoCmdlets"/>.</summary>
    public async Task<ExoResult> InvokeAsync(ExoCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        JsonElement reply = await SendAsync("invoke", command.ToMessageData(), _start.InvokeTimeout, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ExoObject> items = reply.TryGetProperty("items", out JsonElement array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(item => new ExoObject(item)).ToList()
            : [];
        IReadOnlyList<string> errors = reply.TryGetProperty("errors", out JsonElement list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(item => item.GetString() ?? "").ToList()
            : [];
        return new ExoResult(items, errors);
    }

    /// <summary>
    /// Sends a message and returns the <c>data</c> of its reply; an <c>error</c> reply throws the failure
    /// <see cref="SidecarFailures"/> maps it to. Internal so the tests can send what the session never would.
    /// </summary>
    internal async Task<JsonElement> SendAsync(string type, object? data, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string id = Interlocked.Increment(ref _nextId).ToString(CultureInfo.InvariantCulture);
            var message = new Dictionary<string, object?> { ["aegis"] = ProtocolVersion, ["id"] = id, ["type"] = type };
            if (data is not null)
            {
                message["data"] = data;
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await _process.StandardInput.WriteAsync(JsonSerializer.Serialize(message).AsMemory(), timeoutSource.Token).ConfigureAwait(false);
                await _process.StandardInput.WriteAsync("\n".AsMemory(), timeoutSource.Token).ConfigureAwait(false);
                await _process.StandardInput.FlushAsync(timeoutSource.Token).ConfigureAwait(false);
                while (true)
                {
                    string? line = await _process.StandardOutput.ReadLineAsync(timeoutSource.Token).ConfigureAwait(false);
                    if (line is null)
                    {
                        throw Unavailable($"the Exchange Online sidecar stopped while answering '{type}'");
                    }

                    if (TryReadReply(line, id, out JsonElement reply))
                    {
                        return Answer(type, reply);
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw Unavailable($"the Exchange Online sidecar did not answer '{type}' within {timeout.TotalSeconds:0} s");
            }
            catch (IOException exception)
            {
                throw Unavailable($"the Exchange Online sidecar stopped while answering '{type}' ({exception.Message})");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Asks the child to disconnect and end; it is killed when it does not within ten seconds.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (!HasExited)
            {
                await _process.StandardInput.WriteAsync("{\"aegis\":1,\"type\":\"exit\"}\n").ConfigureAwait(false);
                await _process.StandardInput.FlushAsync().ConfigureAwait(false);
                _process.StandardInput.Close();
                using var wait = new CancellationTokenSource(_start.ExitTimeout);
                await _process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or InvalidOperationException)
        {
            KillQuietly(_process);
        }

        await AwaitQuietly(_stderrPump).ConfigureAwait(false);
        _process.Dispose();
        _gate.Dispose();
    }

    /// <summary>Kills the child and its tree; a child already gone, or one the system will not let go, is left as it is.</summary>
    [ExcludeFromCodeCoverage(Justification = "The catch runs only when the operating system refuses the kill or the child ends between the check and the call, a race no test can stage.")]
    private static void KillQuietly(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
    }

    /// <summary>Waits for the stderr pump; a stream that broke with the process is not a failure of the dispose.</summary>
    [ExcludeFromCodeCoverage(Justification = "The catch runs only when the pipe breaks under the read, which .NET reports as the end of the stream on every runner; no test can stage it.")]
    private static async Task AwaitQuietly(Task pump)
    {
        try
        {
            await pump.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // The stream closed with the process.
        }
    }

    /// <summary>Ends the child at once, as a crash would (the tests' dead sidecar).</summary>
    internal void Kill()
    {
        _process.Kill(entireProcessTree: true);
        _process.WaitForExit();
    }

    /// <summary>The last lines the child wrote to stderr, scrubbed like a log line.</summary>
    internal string StderrTail()
    {
        lock (_stderrLock)
        {
            return LogSink.ScrubPatterns(string.Join(" | ", _stderr));
        }
    }

    private AegisException Unavailable(string message)
    {
        string tail = StderrTail();
        return new AegisException(
            FailureKind.SidecarUnavailable,
            tail.Length == 0 ? message : $"{message}; its last words: {tail}",
            "run `m365-aegis doctor`: it checks pwsh, the module and the sidecar script");
    }

    private static bool TryReadReply(string line, string id, out JsonElement reply)
    {
        reply = default;
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("aegis", out JsonElement version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != ProtocolVersion
                || !root.TryGetProperty("id", out JsonElement replyId) || replyId.GetString() != id)
            {
                // A reply to a request that timed out before, or a line that is not ours: not this answer.
                return false;
            }

            reply = root.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private JsonElement Answer(string type, JsonElement reply)
    {
        string replyType = Text(reply, "type") ?? "";
        JsonElement data = reply.TryGetProperty("data", out JsonElement value) ? value : default;
        if (replyType != "error")
        {
            if (data.ValueKind == JsonValueKind.Object)
            {
                return data;
            }

            // No data, or data that is no object: the answer is empty, so a reader of it finds no value instead of failing.

            using JsonDocument empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }

        var error = new SidecarError(
            Text(data, "code") ?? "unknown",
            Text(data, "message") ?? "the sidecar reported an error without a message",
            Text(data, "errorId"),
            Text(data, "category"));
        throw SidecarFailures.For(type, error, StderrTail());
    }

    private async Task PumpStderrAsync()
    {
        while (await _process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            lock (_stderrLock)
            {
                _stderr.Enqueue(line);
                while (_stderr.Count > StderrLines)
                {
                    _stderr.Dequeue();
                }
            }
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>A whole number of an answer's data, which <see cref="Answer"/> always makes an object; 0 when missing.</summary>
    private static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : 0;
}
