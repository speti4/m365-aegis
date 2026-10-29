using System.Diagnostics;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli.Output;

/// <summary>
/// What every command does around its work: time it, turn an <see cref="AegisException"/> from the core into
/// the failed result, turn any other exception into the <see cref="FailureKind.Internal"/> failure (exit code 70,
/// the stack trace into the home folder's log, task 021), render the result through <see cref="Renderer"/> on the
/// console, and return the exit code. A command never writes to the console itself.
/// </summary>
internal static class CommandRunner
{
    /// <summary>
    /// The fault injection of the tests: with this variable set to <c>internal-error</c>, the work of every command
    /// throws before it starts, so a child-process test sees the internal error the way a shell would.
    /// </summary>
    internal const string FaultVariable = "M365AEGIS_TEST_FAULT";

    /// <summary>Runs the work of a command and renders its result; the return value is the process exit code.</summary>
    /// <param name="mode">The rendering the caller asked for.</param>
    /// <param name="backend">Which backend answers (<see cref="Backend"/>), for the envelope's meta.</param>
    /// <param name="work">The command's work: a result, or an <see cref="AegisException"/> from the core.</param>
    /// <param name="view">The table view of the result type.</param>
    internal static int Run<T>(OutputMode mode, string backend, Func<CommandResult<T>> work, ITableView<T> view)
    {
        ArgumentNullException.ThrowIfNull(work);
        return RunAsync(mode, backend, () => Task.FromResult(work()), view).GetAwaiter().GetResult();
    }

    /// <summary>The asynchronous form of <see cref="Run{T}"/>, for the work that waits on a service.</summary>
    internal static Task<int> RunAsync<T>(OutputMode mode, string backend, Func<Task<CommandResult<T>>> work, ITableView<T> view) =>
        RunAsync(mode, backend, work, view, Console.Out, Console.Error, LogInternalError);

    /// <summary>
    /// The form for the work that calls a service: the work gets the command's <see cref="RequestLog"/> (the Graph
    /// client writes every answer's request id into it), and the envelope's meta carries the ids, a failed
    /// command's included.
    /// </summary>
    internal static Task<int> RunAsync<T>(OutputMode mode, string backend, Func<RequestLog, Task<CommandResult<T>>> work, ITableView<T> view) =>
        RunAsync(mode, backend, work, view, Console.Out, Console.Error, LogInternalError);

    /// <summary>The runner with its writers and its internal-error log given (the tests render in process).</summary>
    /// <param name="mode">The rendering the caller asked for.</param>
    /// <param name="backend">Which backend answers, for the envelope's meta.</param>
    /// <param name="work">The command's work.</param>
    /// <param name="view">The table view of the result type.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <param name="logInternalError">Writes an unexpected exception to the log and returns the file, or null when it could not.</param>
    internal static Task<int> RunAsync<T>(
        OutputMode mode,
        string backend,
        Func<Task<CommandResult<T>>> work,
        ITableView<T> view,
        TextWriter output,
        TextWriter error,
        Func<Exception, string?> logInternalError)
    {
        ArgumentNullException.ThrowIfNull(work);
        return RunAsync(mode, backend, _ => work(), view, output, error, logInternalError);
    }

    /// <summary>The runner with the request log, its writers and its internal-error log given.</summary>
    /// <param name="mode">The rendering the caller asked for.</param>
    /// <param name="backend">Which backend answers, for the envelope's meta.</param>
    /// <param name="work">The command's work, given the request log the envelope's meta reads.</param>
    /// <param name="view">The table view of the result type.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <param name="logInternalError">Writes an unexpected exception to the log and returns the file, or null when it could not.</param>
    internal static async Task<int> RunAsync<T>(
        OutputMode mode,
        string backend,
        Func<RequestLog, Task<CommandResult<T>>> work,
        ITableView<T> view,
        TextWriter output,
        TextWriter error,
        Func<Exception, string?> logInternalError)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(logInternalError);

        var stopwatch = Stopwatch.StartNew();
        var requestLog = new RequestLog();
        CommandResult<T> result;
        try
        {
            if (Environment.GetEnvironmentVariable(FaultVariable) == "internal-error")
            {
                throw new InvalidOperationException($"a fault injected through {FaultVariable}");
            }

            result = await work(requestLog).ConfigureAwait(false);
        }
        catch (AegisException exception)
        {
            result = CommandResult.Failure<T>(exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The tool broke: the answer stays an envelope or an error line, the stack trace goes to the log.
            result = CommandResult.Failure<T>(InternalFailure(exception, logInternalError(exception)));
        }

        stopwatch.Stop();
        var meta = new EnvelopeMeta(requestLog.Ids, stopwatch.ElapsedMilliseconds, backend);
        return (int)Renderer.Render(result, meta, view, mode, output, error);
    }

    /// <summary>Renders a failure that happened before any command ran (a usage error) and returns its exit code.</summary>
    internal static int Fail(OutputMode mode, AegisFailure failure) =>
        Run(mode, Backend.Local, () => CommandResult.Failure<object>(failure), NoTableView<object>.Instance);

    /// <summary>The failure of an unexpected exception (<see cref="InternalError.ToFailure"/>, shared with the window).</summary>
    internal static AegisFailure InternalFailure(Exception exception, string? logFile) => InternalError.ToFailure(exception, logFile);

    /// <summary>Writes the exception with its stack trace to the home folder's log; the file, or null when it could not.</summary>
    internal static string? LogInternalError(Exception exception) => InternalError.WriteToLog(exception);
}
