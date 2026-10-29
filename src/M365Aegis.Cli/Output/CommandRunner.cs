using System.Diagnostics;
using M365Aegis.Core.Output;

namespace M365Aegis.Cli.Output;

/// <summary>
/// What every command does around its work: time it, turn an <see cref="AegisException"/> from the core into
/// the failed result, render the result through <see cref="Renderer"/> on the console, and return the exit code.
/// A command never writes to the console itself.
/// </summary>
internal static class CommandRunner
{
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
    internal static async Task<int> RunAsync<T>(OutputMode mode, string backend, Func<Task<CommandResult<T>>> work, ITableView<T> view)
    {
        ArgumentNullException.ThrowIfNull(work);

        var stopwatch = Stopwatch.StartNew();
        CommandResult<T> result;
        try
        {
            result = await work().ConfigureAwait(false);
        }
        catch (AegisException exception)
        {
            result = CommandResult.Failure<T>(exception);
        }

        stopwatch.Stop();
        var meta = new EnvelopeMeta([], stopwatch.ElapsedMilliseconds, backend);
        return (int)Renderer.Render(result, meta, view, mode, Console.Out, Console.Error);
    }

    /// <summary>Renders a failure that happened before any command ran (a usage error) and returns its exit code.</summary>
    internal static int Fail(OutputMode mode, AegisFailure failure) =>
        Run(mode, Backend.Local, () => CommandResult.Failure<object>(failure), NoTableView<object>.Instance);
}
