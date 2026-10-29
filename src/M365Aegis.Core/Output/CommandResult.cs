namespace M365Aegis.Core.Output;

/// <summary>
/// What a command produces before any rendering (D-006): the data, whole on success or partial on a failure, and
/// the failure when there is one. The two renderings of the command line and the window read this one type; the
/// exit code and the envelope follow from it. The meta (elapsed time, backend, request ids) is the runner's and
/// is added when the envelope is made, so a result stays plain data.
/// </summary>
/// <typeparam name="T">The result type of the command.</typeparam>
/// <param name="Data">The payload; null when the failure left nothing to show.</param>
/// <param name="Failure">The failure, or null on success.</param>
public sealed record CommandResult<T>(T? Data, AegisFailure? Failure)
{
    /// <summary>True when there is no failure.</summary>
    public bool Ok => Failure is null;

    /// <summary>The exit code of the command line: 0 on success, else the failure's.</summary>
    public ExitCode ExitCode => Failure?.ExitCode ?? ExitCode.Ok;

    /// <summary>The envelope of this result, with the meta the runner measured.</summary>
    public Envelope<T> ToEnvelope(EnvelopeMeta meta) =>
        Failure is null ? Envelope.Success(Data!, meta) : Envelope.Failure(Failure.ToError(), meta, Data);
}

/// <summary>The ways to make a command result.</summary>
public static class CommandResult
{
    /// <summary>A successful result.</summary>
    public static CommandResult<T> Success<T>(T data) => new(data, null);

    /// <summary>A failed result; <paramref name="data"/> may carry a partial result (a doctor report, a bulk run).</summary>
    public static CommandResult<T> Failure<T>(AegisFailure failure, T? data = default)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new(data, failure);
    }

    /// <summary>A failed result from the exception the core threw.</summary>
    public static CommandResult<T> Failure<T>(AegisException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new(default, exception.Failure);
    }
}
