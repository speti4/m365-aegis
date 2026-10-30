namespace M365Aegis.Core.Auth;

/// <summary>
/// What a failed sign-in tells the person to do, per surface (task 046): the command line names its commands, the
/// window names the buttons of its sign-in card and leaves the tenant profile, which only the command line writes,
/// to the administrator. The sign-in (<see cref="AuthService"/>, <see cref="SignInFlow"/>) and the profiles
/// (<see cref="Config.ProfileStore"/>) take the set of their surface; the failure's kind and sentence are the same on
/// both, only the hint differs.
/// </summary>
public sealed record SignInHints
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
    };
}
