namespace M365Aegis.Core.Output;

/// <summary>
/// The exit codes of the command line (D-006). They are a contract for shells and agents: a code never changes its
/// meaning, a new condition gets a new number.
/// </summary>
public enum ExitCode
{
    /// <summary>The command did what it was asked.</summary>
    Ok = 0,

    /// <summary>A usage error: an unknown command or option, a missing argument. Nothing was called.</summary>
    Usage = 2,

    /// <summary>No usable sign-in: the silent token failed and the interactive flow was declined or impossible.</summary>
    Auth = 3,

    /// <summary>The named object (user, group, mailbox, site) does not exist in the tenant.</summary>
    NotFound = 4,

    /// <summary>The service throttled the request and the retries ran out (429 or 503 with Retry-After).</summary>
    Throttled = 5,

    /// <summary>Some items of a bulk operation succeeded and some failed; the envelope lists both.</summary>
    Partial = 6,

    /// <summary>A write was asked for without --yes and without a confirmation; the plan was printed instead.</summary>
    RefusedWithoutYes = 7,

    /// <summary>A required doctor check failed; the report names it.</summary>
    DoctorFailed = 10,

    /// <summary>
    /// The tool broke: an unexpected internal error stopped the command (task 021). The envelope says so in one
    /// sentence, the stack trace goes to the log under the home folder's <c>logs</c> folder. The number is
    /// <c>EX_SOFTWARE</c> of BSD's sysexits.h ("internal software error"), well apart from the codes that say the
    /// request failed.
    /// </summary>
    Internal = 70,
}
