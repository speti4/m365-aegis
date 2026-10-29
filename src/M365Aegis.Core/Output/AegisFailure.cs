namespace M365Aegis.Core.Output;

/// <summary>
/// The ways an operation of the core fails, as the contract names them (D-006). Each kind has one exit code and one
/// stable error code; a new condition gets a new kind, a new code and a new number, never a reused one.
/// </summary>
public enum FailureKind
{
    /// <summary>The command line was used wrongly: an unknown command or option, a missing argument.</summary>
    Usage,

    /// <summary>No usable sign-in.</summary>
    Auth,

    /// <summary>The named object does not exist in the tenant.</summary>
    NotFound,

    /// <summary>The service throttled the request and the retries ran out.</summary>
    Throttled,

    /// <summary>Some items of a bulk operation succeeded and some failed; the data lists both.</summary>
    Partial,

    /// <summary>A write was asked for without --yes and without a confirmation; the plan is the data.</summary>
    RefusedWithoutYes,

    /// <summary>A required doctor check failed; the report is the data.</summary>
    DoctorFailed,

    /// <summary>An unexpected internal error stopped the command; the stack trace is in the log, not in the answer.</summary>
    Internal,
    /// <summary>The service refused the account or the app: a permission is not consented, or a role is missing.</summary>
    Forbidden,

    /// <summary>The service answered with an error no other kind covers, or could not be reached.</summary>
    ServiceError,
}

/// <summary>The exit code and the error code of each failure kind: the mapping the tests hold fixed.</summary>
public static class FailureKinds
{
    /// <summary>The exit code the command line returns for this kind of failure.</summary>
    public static ExitCode ToExitCode(this FailureKind kind) => kind switch
    {
        FailureKind.Usage => ExitCode.Usage,
        FailureKind.Auth => ExitCode.Auth,
        FailureKind.NotFound => ExitCode.NotFound,
        FailureKind.Throttled => ExitCode.Throttled,
        FailureKind.Partial => ExitCode.Partial,
        FailureKind.RefusedWithoutYes => ExitCode.RefusedWithoutYes,
        FailureKind.DoctorFailed => ExitCode.DoctorFailed,
        FailureKind.Internal => ExitCode.Internal,
        FailureKind.Forbidden => ExitCode.Forbidden,
        FailureKind.ServiceError => ExitCode.ServiceError,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "a failure kind without an exit code"),
    };

    /// <summary>The stable, kebab-case error code of the envelope for this kind of failure.</summary>
    public static string ToCode(this FailureKind kind) => kind switch
    {
        FailureKind.Usage => "usage",
        FailureKind.Auth => "auth",
        FailureKind.NotFound => "not-found",
        FailureKind.Throttled => "throttled",
        FailureKind.Partial => "partial",
        FailureKind.RefusedWithoutYes => "refused-without-yes",
        FailureKind.DoctorFailed => "doctor-failed",
        FailureKind.Internal => "internal",
        FailureKind.Forbidden => "forbidden",
        FailureKind.ServiceError => "service-error",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "a failure kind without an error code"),
    };
}

/// <summary>
/// A failure as the core reports it: the kind decides the exit code and the error code, the message is one plain
/// sentence, the hint says what to do about it. A failure may sit next to partial data in a
/// <see cref="CommandResult{T}"/>.
/// </summary>
/// <param name="Kind">What went wrong, in the contract's terms.</param>
/// <param name="Message">One plain sentence.</param>
/// <param name="Hint">What to do about it, or null.</param>
public sealed record AegisFailure(FailureKind Kind, string Message, string? Hint = null)
{
    /// <summary>The stable error code of the envelope.</summary>
    public string Code => Kind.ToCode();

    /// <summary>The exit code of the command line.</summary>
    public ExitCode ExitCode => Kind.ToExitCode();

    /// <summary>The failure as the error part of an envelope.</summary>
    public EnvelopeError ToError() => new(Code, Message, Hint);
}

/// <summary>
/// Thrown inside the core when an operation cannot finish (no sign-in, no such object, throttled out). The command
/// runner turns it into a <see cref="CommandResult{T}"/>, so the envelope and the exit code follow from the
/// failure it carries, never from the exception type.
/// </summary>
public sealed class AegisException : Exception
{
    /// <summary>A failure of the given kind.</summary>
    public AegisException(FailureKind kind, string message, string? hint = null)
        : this(new AegisFailure(kind, message, hint))
    {
    }

    /// <summary>A failure of the given kind, with the exception that caused it.</summary>
    public AegisException(FailureKind kind, string message, string? hint, Exception innerException)
        : base(message, innerException)
    {
        Failure = new AegisFailure(kind, message, hint);
    }

    /// <summary>Carries an existing failure.</summary>
    public AegisException(AegisFailure failure)
        : base(failure?.Message)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Failure = failure;
    }

    /// <summary>The failure, as the contract reports it.</summary>
    public AegisFailure Failure { get; }
}
