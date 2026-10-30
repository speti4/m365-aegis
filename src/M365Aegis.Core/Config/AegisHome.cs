using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace M365Aegis.Core.Config;

/// <summary>
/// Where the tool keeps what is not in the repo: the tenant profiles (config.json), the MSAL cache, the WebView2
/// data folder, the logs and the plan records (D-005). <c>%LOCALAPPDATA%\m365-aegis\</c> by default;
/// <c>M365AEGIS_HOME</c> overrides it, which the tests use to point it at a temporary folder.
/// </summary>
public static class AegisHome
{
    /// <summary>The environment variable that overrides the home folder.</summary>
    public const string EnvironmentVariable = "M365AEGIS_HOME";

    /// <summary>The home folder, resolved but not created.</summary>
    public static string Resolve() => ResolveFrom(Environment.GetEnvironmentVariable(EnvironmentVariable));

    /// <summary>The home folder for the value of <see cref="EnvironmentVariable"/> (null or blank: the default).</summary>
    internal static string ResolveFrom(string? overridden)
    {
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return Path.GetFullPath(overridden);
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, ProductInfo.Name);
    }

    /// <summary>The path of the tenant profiles file under the home folder.</summary>
    public static string ConfigPath(string home) => Path.Combine(home, "config.json");

    /// <summary>
    /// Creates the home folder when it does not exist yet (task 075), which every writer under it calls first (the
    /// profiles, the MSAL cache, the log, the plan records, the window's data folder). On Windows the folder gets an
    /// explicit access list instead of its parent's: full control for the current user, SYSTEM and Administrators,
    /// inherited by every folder and file under it, and nothing inherited from above, so a home that
    /// <c>M365AEGIS_HOME</c> puts under a folder other users may read (a <c>Users: read</c> entry on <c>C:\Data</c>)
    /// stays the user's own, as <c>%LOCALAPPDATA%</c> is. A folder that exists already is left as it is: it is the
    /// user's, and a home created by this rule keeps it.
    /// </summary>
    /// <param name="home">The home folder (<see cref="Resolve"/>).</param>
    /// <returns>The home folder.</returns>
    public static string Create(string home)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        string full = Path.GetFullPath(home);
        if (!Directory.Exists(full))
        {
            CreateNew(full);
        }

        return full;
    }

    /// <summary>The home above a folder of it (the logs, the plans): its parent, or the folder itself when it is a root.</summary>
    internal static string CreateAbove(string folder) => Create(Path.GetDirectoryName(Path.GetFullPath(folder)) ?? folder);

    /// <summary>Creates the folder: with the access list of <see cref="UserOnly"/> on Windows, a plain folder elsewhere.</summary>
    [ExcludeFromCodeCoverage(Justification = "one side per operating system; the Windows side is checked by the home folder's access list tests, which run in the windows job (docs/development.md, Coverage)")]
    private static void CreateNew(string full)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(full);
            return;
        }

        if (Path.GetDirectoryName(full) is { } parent)
        {
            Directory.CreateDirectory(parent);
        }

        new DirectoryInfo(full).Create(UserOnly());
    }

    /// <summary>The access list of a home folder on Windows: the user, SYSTEM and Administrators, inherited below, protected above.</summary>
    [SupportedOSPlatform("windows")]
    [ExcludeFromCodeCoverage(Justification = "Windows only: the access list API; the home folder's access list tests check it in the windows job (docs/development.md, Coverage)")]
    internal static DirectorySecurity UserOnly()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        SecurityIdentifier user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("the process has no user to own the home folder");
        foreach (SecurityIdentifier sid in new[] { user, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        security.SetOwner(user);
        return security;
    }
}
