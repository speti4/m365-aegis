using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using M365Aegis.Core.Config;

namespace M365Aegis.Core.Diagnostics;

/// <summary>
/// The tool's log: one file a day under the home folder's <c>logs</c> folder (D-005), appended to by every process
/// that has something to say. Two things write here: an unexpected internal error (its stack trace, task 021, always)
/// and MSAL's own log of a sign-in (task 025, only under <c>--verbose</c>). Every line is scrubbed before it is
/// written: the values registered with <see cref="Redact"/> (the profile's tenant, client id and account), anything
/// shaped like a token, an authorization code or a user principal name, every GUID and every
/// <c>*.onmicrosoft.com</c> name, so a log can be attached to a bug report as it is. Nothing is created until the
/// first line: a run that logs nothing leaves no folder and no file behind.
/// </summary>
public sealed partial class LogSink
{
    /// <summary>The folder under the home folder.</summary>
    public const string FolderName = "logs";

    private readonly Lock _gate = new();
    private readonly HashSet<string> _literals = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _time;

    /// <summary>A sink writing into <paramref name="folder"/> (created on the first line).</summary>
    public LogSink(string folder, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Folder = folder;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// The sink of a <c>--verbose</c> run, which MSAL logs into; null without the flag, so nothing is logged. The
    /// command line sets it once at start-up (<see cref="EnableVerbose"/>), before any command runs.
    /// </summary>
    public static LogSink? Verbose => Scoped.Value ?? ProcessVerbose;

    /// <summary>The sink of this process, set by <see cref="EnableVerbose"/>.</summary>
    private static LogSink? ProcessVerbose;

    /// <summary>A sink for one flow of calls (<see cref="UseVerbose"/>), which wins over the process's own.</summary>
    private static readonly AsyncLocal<LogSink?> Scoped = new();

    /// <summary>The folder the sink writes into.</summary>
    public string Folder { get; }

    /// <summary>Today's file: <c>m365-aegis-yyyy-MM-dd.log</c> in local time.</summary>
    public string CurrentFile =>
        Path.Combine(Folder, string.Format(CultureInfo.InvariantCulture, "{0}-{1:yyyy-MM-dd}.log", ProductInfo.Name, _time.GetLocalNow()));

    /// <summary>The sink of a home folder: its <c>logs</c> folder.</summary>
    public static LogSink ForHome(string home)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        return new LogSink(Path.Combine(home, FolderName));
    }

    /// <summary>Turns on the verbose log of this process, into the home folder's <c>logs</c> folder.</summary>
    public static void EnableVerbose(string home) => ProcessVerbose = ForHome(home);

    /// <summary>
    /// Makes <paramref name="sink"/> the verbose sink of the calls that follow in this flow (an <see cref="AsyncLocal{T}"/>,
    /// so parallel tests do not see each other's) until the returned scope is disposed, which restores the one before.
    /// </summary>
    internal static IDisposable UseVerbose(LogSink? sink)
    {
        LogSink? previous = Scoped.Value;
        Scoped.Value = sink;
        return new Restore(() => Scoped.Value = previous);
    }

    /// <summary>A value that must never reach the file (a tenant, a client id, an account); it is written as <c>&lt;redacted&gt;</c>.</summary>
    public void Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 3)
        {
            return;
        }

        lock (_gate)
        {
            _literals.Add(value.Trim());
        }
    }

    /// <summary>
    /// Appends one entry, scrubbed, with the time, the process id and the category; a multi-line message (a stack
    /// trace) keeps its lines, indented. Never throws: a log that cannot be written must not break the command, so
    /// the answer is whether the entry reached the file.
    /// </summary>
    public bool Write(string category, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(message);

        string[] lines = Scrub(message).ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        var entry = new StringBuilder();
        entry.Append(CultureInfo.InvariantCulture, $"{_time.GetLocalNow():yyyy-MM-ddTHH:mm:ss.fffzzz} [{Environment.ProcessId}] {category}: {lines[0]}\n");
        foreach (string line in lines.Skip(1))
        {
            entry.Append("    ").Append(line).Append('\n');
        }

        byte[] bytes = Encoding.UTF8.GetBytes(entry.ToString());
        try
        {
            lock (_gate)
            {
                // The home first, with its access list (task 075), then the logs folder under it.
                AegisHome.Create(Path.GetDirectoryName(Folder) ?? Folder);
                Directory.CreateDirectory(Folder);
                // Several processes may append to the same day's file (the command line and the window share the home).
                using var stream = new FileStream(CurrentFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                stream.Write(bytes);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The text as it would reach the file: the registered values first, then the patterns of <see cref="ScrubPatterns"/>.</summary>
    public string Scrub(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] literals;
        lock (_gate)
        {
            // The longest first, so a value that contains another one is replaced whole.
            literals = [.. _literals.OrderByDescending(literal => literal.Length)];
        }

        foreach (string literal in literals)
        {
            text = text.Replace(literal, "<redacted>", StringComparison.OrdinalIgnoreCase);
        }

        return ScrubPatterns(text);
    }

    /// <summary>
    /// The patterns no log line keeps: a JWT (an access, id or refresh token in that shape), a bearer header, the
    /// value of an OAuth parameter that carries a secret or an identity (<c>code=</c>, <c>access_token=</c>,
    /// <c>login_hint=</c>, …) in a query string or a JSON body, an e-mail-shaped name (a user principal name), a
    /// GUID (a tenant, an object, a client id) and a <c>*.onmicrosoft.com</c> tenant name.
    /// </summary>
    public static string ScrubPatterns(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        text = BearerHeader().Replace(text, "Bearer <redacted>");
        text = Jwt().Replace(text, "<token>");
        text = QueryParameter().Replace(text, "${name}=<redacted>");
        text = JsonProperty().Replace(text, "\"${name}\":\"<redacted>\"");
        text = UserPrincipalName().Replace(text, "<upn>");
        text = GuidPattern().Replace(text, "<guid>");
        return TenantName().Replace(text, "<tenant>.onmicrosoft.com");
    }

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_\-]+(?:\.[A-Za-z0-9_\-]*)*")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9._~+/=\-]+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerHeader();

    [GeneratedRegex(@"\b(?<name>code|access_token|refresh_token|id_token|client_info|assertion|login_hint|username|password|device_code|user_code)=[^&\s""']+", RegexOptions.IgnoreCase)]
    private static partial Regex QueryParameter();

    [GeneratedRegex(@"""(?<name>code|access_token|refresh_token|id_token|client_info|assertion|login_hint|username|password|device_code|user_code)""\s*:\s*""[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex JsonProperty();

    [GeneratedRegex(@"[A-Za-z0-9._%+'\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)+")]
    private static partial Regex UserPrincipalName();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b")]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"\b[A-Za-z0-9\-]+\.onmicrosoft\.com\b", RegexOptions.IgnoreCase)]
    private static partial Regex TenantName();

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
