using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using M365Aegis.Core.Auth;
using M365Aegis.Core.Config;
using M365Aegis.Core.Diagnostics;
using M365Aegis.Tests;
using Microsoft.Identity.Client.Extensions.Msal;

namespace M365Aegis.Core.Tests.Config;

/// <summary>
/// The home folder's access list (task 075, SECURITY.md "a cache readable by another user"), on Windows only: a home
/// that <c>M365AEGIS_HOME</c> puts under a folder whose access list lets <c>Users</c> and <c>Authenticated Users</c>
/// read everything below it (as a folder made at a drive's root does) is created through the product's own writers
/// (the MSAL cache, <c>config.json</c>, a plan record, the log), and the home and everything in it grant access to the
/// user, SYSTEM and Administrators only; the cache is DPAPI-protected, so its bytes hold no token in clear. The
/// doctor's <c>home-access</c> check (task 078) reads the same lists: a pass on a home the product created, a warning
/// on one that inherits the parent's, whose fix line makes it the user's own.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HomeFolderAclTests
{
    private const string NotWindows = "the home folder's access list is a Windows rule (task 075); a Linux or macOS mode is not in scope";

    [Fact]
    public async Task TheHomeAndEverythingTheProductWritesUnderItAreTheUsersOwn()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), NotWindows);
        string home = Path.Combine(ReadableParent(), "home");
        string marker = $"aegismarker{Guid.NewGuid():N}";

        MsalCacheHelper cache = await MsalTokenBroker.CreateCacheAsync(home);
        cache.SaveUnencryptedTokenCache(Encoding.UTF8.GetBytes($"{{\"AccessToken\":{{\"one\":{{\"secret\":\"{marker}\"}}}}}}"));
        new ProfileStore(home).Save(new TenantProfile("work", "contoso.onmicrosoft.com", "00000000-0000-0000-0000-000000000010", "alice@contoso.com"), makeDefault: true);
        string record = PlanSamples.Store(home).Begin(PlanSamples.GroupWrite([]).Plan);
        Assert.True(LogSink.ForHome(home).Write("test", "a line of the log"));

        string[] files = [Path.Combine(home, MsalTokenBroker.CacheFileName), AegisHome.ConfigPath(home), record, LogSink.ForHome(home).CurrentFile];
        Assert.All(files, file => Assert.True(File.Exists(file), $"{file} was not written"));
        foreach (string path in Directory.GetFileSystemEntries(home, "*", SearchOption.AllDirectories).Prepend(home))
        {
            AssertTheUsersOwn(path);
        }

        // DPAPI: the token is not in the file's bytes, and the product's own reader gets it back.
        byte[] raw = await File.ReadAllBytesAsync(Path.Combine(home, MsalTokenBroker.CacheFileName), TestContext.Current.CancellationToken);
        Assert.Equal(-1, raw.AsSpan().IndexOf(Encoding.UTF8.GetBytes(marker)));
        Assert.Equal(-1, raw.AsSpan().IndexOf(Encoding.Unicode.GetBytes(marker)));
        Assert.Contains(marker, Encoding.UTF8.GetString(cache.LoadUnencryptedTokenCache()), StringComparison.Ordinal);
    }

    [Fact]
    public void TheParentOfTheTestLetsUsersRead()
    {
        // Without this the test above would pass on a temporary folder's user-only access list and prove nothing.
        Assert.SkipUnless(OperatingSystem.IsWindows(), NotWindows);
        string child = Path.Combine(ReadableParent(), "plain");
        Directory.CreateDirectory(child);

        Assert.Contains(Rules(child), rule => rule.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null)) && rule.IsInherited);
    }

    [Fact]
    public void AnExistingHomeIsLeftAsItIs()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), NotWindows);
        string home = Path.Combine(ReadableParent(), "existing");
        Directory.CreateDirectory(home);

        Assert.Equal(home, AegisHome.Create(home));
        Assert.Contains(Rules(home), rule => rule.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null)));
    }

    [Fact]
    public void TheDoctorPassesAHomeTheProductCreated()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), NotWindows);
        string home = AegisHome.Create(Path.Combine(ReadableParent(), "home"));

        DoctorCheck check = Doctor.CheckHomeAccess(DoctorEnvironment.ForThisMachine(home, null));

        Assert.Equal(CheckStatus.Pass, check.Status);
        Assert.Null(check.Hint);
    }

    [Fact]
    public void TheDoctorWarnsOnAHomeThatInheritsUsersReadAndItsFixLineMakesTheHomeTheUsersOwn()
    {
        // Task 078: a home that existed before the product's rule keeps its parent's list, and the doctor says so.
        Assert.SkipUnless(OperatingSystem.IsWindows(), NotWindows);
        string home = Path.Combine(ReadableParent(), "existing");
        Directory.CreateDirectory(home);
        DoctorEnvironment environment = DoctorEnvironment.ForThisMachine(home, null);

        DoctorCheck check = Doctor.CheckHomeAccess(environment);

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Matches(@"(^|\), )Users \(read, inherited\)", check.Detail);
        Assert.Contains("Authenticated Users (read, inherited)", check.Detail, StringComparison.Ordinal);
        Assert.NotNull(check.Hint);

        // The hint is a command line the user runs as it is: icacls takes it, and afterwards the check passes and the
        // home holds the list of a home the product creates.
        RunIcacls(check.Hint);

        Assert.Equal(CheckStatus.Pass, Doctor.CheckHomeAccess(environment).Status);
        AssertTheUsersOwn(home);
    }

    [Fact]
    public void TheDoctorNamesAnotherAccountTheHomeInheritsAndItsFixLineRemovesIt()
    {
        // Task 088: not one of the broad groups but a sandbox group with read and a service account with modify,
        // inherited from the parent; the detail names both with their rights, and the fix line still makes the home
        // the user's own.
        Assert.SkipUnless(OperatingSystem.IsWindows(), NotWindows);
        var packages = new SecurityIdentifier("S-1-15-2-1");
        var networkService = new SecurityIdentifier(WellKnownSidType.NetworkServiceSid, null);
        string home = Path.Combine(Parent((packages, FileSystemRights.ReadAndExecute), (networkService, FileSystemRights.Modify)), "existing");
        Directory.CreateDirectory(home);
        DoctorEnvironment environment = DoctorEnvironment.ForThisMachine(home, null);

        DoctorCheck check = Doctor.CheckHomeAccess(environment);

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains($"{Name(packages)} (read, inherited)", check.Detail, StringComparison.Ordinal);
        Assert.Contains($"{Name(networkService)} (modify, inherited)", check.Detail, StringComparison.Ordinal);
        Assert.Contains("an account other than you, SYSTEM and Administrators", check.Detail, StringComparison.Ordinal);
        RunIcacls(check.Hint!);

        Assert.Equal(CheckStatus.Pass, Doctor.CheckHomeAccess(environment).Status);
        AssertTheUsersOwn(home);
    }

    /// <summary>The fix line of the doctor, run as the user would run it.</summary>
    private static void RunIcacls(string hint)
    {
        Assert.StartsWith("icacls ", hint, StringComparison.Ordinal);
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "icacls.exe"), hint["icacls ".Length..])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        using Process icacls = Process.Start(startInfo)!;
        string output = icacls.StandardOutput.ReadToEnd();
        icacls.WaitForExit();
        Assert.True(icacls.ExitCode == 0, $"icacls exited with {icacls.ExitCode}: {output}");
    }

    /// <summary>
    /// A new temporary folder whose access list adds, for everything below it, read access for <c>Users</c> and
    /// <c>Authenticated Users</c>: the parent a home under <c>M365AEGIS_HOME</c> may have.
    /// </summary>
    private static string ReadableParent() => Parent(
        (new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute),
        (new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), FileSystemRights.ReadAndExecute));

    /// <summary>A new temporary folder whose access list adds the given rights for everything below it.</summary>
    private static string Parent(params (SecurityIdentifier Account, FileSystemRights Rights)[] readers)
    {
        var parent = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "m365-aegis-tests", $"acl-{Guid.NewGuid():N}"));
        parent.Create();
        DirectorySecurity security = parent.GetAccessControl();
        foreach ((SecurityIdentifier account, FileSystemRights rights) in readers)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                account,
                rights,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        parent.SetAccessControl(security);
        return parent.FullName;
    }

    private static List<FileSystemAccessRule> Rules(string path)
    {
        FileSystemSecurity security = Directory.Exists(path) ? new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl();
        return security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
    }

    /// <summary>Every entry of the path's access list is the user's, SYSTEM's or Administrators'; the owner is the user (or Administrators, elevated).</summary>
    private static void AssertTheUsersOwn(string path)
    {
        SecurityIdentifier user = WindowsIdentity.GetCurrent().User!;
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var allowed = new[] { user, system, administrators };

        List<string> others = Rules(path)
            .Where(rule => rule.AccessControlType == AccessControlType.Allow && !allowed.Contains((SecurityIdentifier)rule.IdentityReference))
            .Select(rule => $"{Name(rule.IdentityReference)}: {rule.FileSystemRights}{(rule.IsInherited ? " (inherited)" : "")}")
            .ToList();
        Assert.True(others.Count == 0, $"{path} grants {string.Join(", ", others)}");

        FileSystemSecurity security = Directory.Exists(path) ? new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl();
        var owner = (SecurityIdentifier)security.GetOwner(typeof(SecurityIdentifier))!;
        Assert.True(owner == user || owner == administrators, $"{path} is owned by {Name(owner)}");
    }

    private static string Name(IdentityReference identity)
    {
        try
        {
            return identity.Translate(typeof(NTAccount)).Value;
        }
        catch (IdentityNotMappedException)
        {
            return identity.Value;
        }
    }
}
