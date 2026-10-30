using System.Text.RegularExpressions;
using M365Aegis.Core.Output;

namespace M365Aegis.Core.Auth;

/// <summary>
/// What a failed sign-in tells the person to do, per surface (task 046): the command line names its commands, the
/// window names the buttons of its sign-in card and leaves the tenant profile, which only the command line writes,
/// to the administrator. The sign-in (<see cref="AuthService"/>, <see cref="SignInFlow"/>) and the profiles
/// (<see cref="Config.ProfileStore"/>) take the set of their surface; the failure's kind and sentence are the same on
/// both, only the hint differs. The failures that reach a surface after the sign-in (Graph's, the Admin API's, the
/// sidecar's; task 057) keep their kind and sentence too, and <see cref="ForSurface"/> gives one whose hint names a
/// command the surface's own hint of its kind (<see cref="Failures"/>).
/// </summary>
public sealed partial record SignInHints
{
    /// <summary>No silent sign-in (<c>whoami</c>, a lookup's token).</summary>
    public required string SignInAgain { get; init; }

    /// <summary>The account picker was closed.</summary>
    public required string PickerCancelled { get; init; }

    /// <summary>The account picker failed for another reason.</summary>
    public required string PickerFailed { get; init; }

    /// <summary>The device code sign-in failed (the code expired, the browser step was refused).</summary>
    public required string DeviceCodeFailed { get; init; }

    /// <summary>No tenant profile, or none of the given name.</summary>
    public required string NoProfile { get; init; }

    /// <summary>Several profiles and none of them the default.</summary>
    public required string NoDefaultProfile { get; init; }

    /// <summary>The profile file does not parse.</summary>
    public required string BrokenProfileFile { get; init; }

    /// <summary>A profile in the file lacks its tenant, client id or account.</summary>
    public required string IncompleteProfile { get; init; }

    /// <summary>
    /// The hint of each kind of failure that reaches the surface after the sign-in (task 057), used where the failure's
    /// own hint names a command (<see cref="NamesACommand"/>) or there is none. Empty on the command line, whose
    /// failures carry hints written for it; every kind on the window.
    /// </summary>
    public IReadOnlyDictionary<FailureKind, string> Failures { get; init; } = new Dictionary<FailureKind, string>();

    /// <summary>
    /// A failure as this surface shows it (task 057): the same kind and sentence, and the hint of its kind from
    /// <see cref="Failures"/> when its own hint names a command or is missing; a hint written for a person (the sign-in's
    /// own, a "give the exact display name" of a lookup, the log file of an internal error) stays as it is.
    /// </summary>
    public AegisFailure ForSurface(AegisFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return (failure.Hint is null || NamesACommand(failure.Hint)) && Failures.TryGetValue(failure.Kind, out string? hint)
            ? failure with { Hint = hint }
            : failure;
    }

    /// <summary>
    /// Whether a hint names something only a shell runs: a command in backticks (<c>`m365-aegis doctor`</c>), a flag
    /// (<c>--device-code</c>, <c>-Scope CurrentUser</c>), a PowerShell or winget line, an environment variable of the
    /// tool, or "run the command".
    /// </summary>
    public static bool NamesACommand(string? hint) => hint is not null && CommandWords().IsMatch(hint);

    [GeneratedRegex(@"`|--[A-Za-z]|(^|\s)-[A-Za-z]|\bInstall-Module\b|\bwinget\b|\bM365AEGIS_|\brun the command\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CommandWords();

    /// <summary>The command line's hints: the commands that fix each failure.</summary>
    public static SignInHints CommandLine { get; } = new()
    {
        SignInAgain = "run `m365-aegis login`",
        PickerCancelled = "run `m365-aegis login` again",
        PickerFailed = "run `m365-aegis login --device-code` to sign in through a browser instead",
        DeviceCodeFailed = "run `m365-aegis login --device-code` again and finish the browser step before the code expires",
        NoProfile = Config.ProfileStore.LoginHint,
        NoDefaultProfile = "name one with --profile, or set \"default\" in the file",
        BrokenProfileFile = "fix the file, or delete it and run `m365-aegis login` again",
        IncompleteProfile = "fix the file, or remove the profile with `m365-aegis profile remove`",
    };

    /// <summary>The window's hints: the sign-in card's buttons, and the administrator for the tenant profile.</summary>
    public static SignInHints Window { get; } = new()
    {
        SignInAgain = "choose Sign in on the sign-in card to sign in again",
        PickerCancelled = "choose Sign in again and pick the account in the Windows sign-in window",
        PickerFailed = "choose Sign in with a code to sign in through a browser instead",
        DeviceCodeFailed = "choose Sign in with a code again and finish the browser step before the code expires",
        NoProfile = "ask the administrator to set up the tenant profile on this computer; the window signs in with it",
        NoDefaultProfile = "ask the administrator to make one of the tenant profiles on this computer the default",
        BrokenProfileFile = "ask the administrator to repair the tenant profile file on this computer",
        IncompleteProfile = "ask the administrator to complete the tenant profile on this computer",
        Failures = new Dictionary<FailureKind, string>
        {
            [FailureKind.Usage] = "check what the card asks for and try again",
            [FailureKind.Auth] = "choose Sign in on the sign-in card to sign in again",
            [FailureKind.NotFound] = "check the name or the address and try again",
            [FailureKind.Throttled] = "Microsoft 365 is busy; try again in a few minutes",
            [FailureKind.Partial] = "the answer lists what could be read; try again for the rest",
            [FailureKind.RefusedWithoutYes] = "nothing was changed; confirm the change in the window to apply it",
            [FailureKind.DoctorFailed] = "the setup of this computer needs a check; ask the administrator",
            [FailureKind.Internal] = "the window hit an unexpected error; tell the administrator, who finds the details in the log of this computer",
            [FailureKind.Forbidden] = "your account or the app lacks a permission or an Exchange Online role for this; ask the administrator",
            [FailureKind.ServiceError] = "Microsoft 365 did not answer as expected; try again later, and tell the administrator if it persists",
            [FailureKind.SidecarUnavailable] = "the Exchange Online tools on this computer need a check; ask the administrator",
            [FailureKind.CmdletNotAllowed] = "the window does not run this Exchange Online request; ask the administrator",
            [FailureKind.CmdletFailed] = "Exchange Online could not do this; try again, and tell the administrator if it persists",
        },
    };
}
