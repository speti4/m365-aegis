using M365Aegis.Core.Config;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Diagnostics;

/// <summary>
/// An unexpected exception as the contract reports it (task 021), shared by the command line's runner and the
/// window's bridge: the stack trace goes into the home folder's log, the answer carries the
/// <see cref="FailureKind.Internal"/> failure with the exception's type and message, scrubbed like a log line, and
/// the log file as the hint. The stack trace itself never reaches an answer.
/// </summary>
public static class InternalError
{
    /// <summary>
    /// The failure of an unexpected exception: its type and its message, scrubbed (a message may name an account),
    /// and the log file with the stack trace as the hint, or a hint that says the log could not be written.
    /// </summary>
    public static AegisFailure ToFailure(Exception exception, string? logFile)
    {
        ArgumentNullException.ThrowIfNull(exception);
        string message = $"an unexpected internal error stopped the command ({exception.GetType().Name}: {LogSink.ScrubPatterns(exception.Message)})";
        string hint = logFile is null
            ? "the log could not be written; run the command again, and report it with the command line if it happens again"
            : $"the stack trace is in {logFile}; report it with that log";
        return new AegisFailure(FailureKind.Internal, message, hint);
    }

    /// <summary>
    /// Writes the exception with its stack trace to the log of <paramref name="home"/> (the resolved home folder when
    /// it is null); the file, or null when it could not be written.
    /// </summary>
    public static string? WriteToLog(Exception exception, string? home = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            LogSink sink = LogSink.ForHome(home ?? AegisHome.Resolve());
            return sink.Write("internal-error", exception.ToString()) ? sink.CurrentFile : null;
        }
        catch (Exception logException) when (logException is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
