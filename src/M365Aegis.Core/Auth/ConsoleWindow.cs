using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace M365Aegis.Core.Auth;

/// <summary>
/// The window the account picker attaches to when the caller is a console: the root owner of the console window
/// (Windows Terminal owns its console windows, so the console's own handle is not enough; task 013). Zero where
/// there is no console window (a redirected shell, a scheduled task) or off Windows, which makes the broker
/// answer <c>window_handle_required</c> and the flow fall back to device code.
/// </summary>
public static partial class ConsoleWindow
{
    /// <summary><c>GA_ROOTOWNER</c> of <c>GetAncestor</c>.</summary>
    private const uint RootOwnerFlag = 3;

    /// <summary>The root owner of the console window, the console window itself, or zero.</summary>
    public static IntPtr RootOwner()
    {
        if (!OperatingSystem.IsWindows())
        {
            return IntPtr.Zero;
        }

        IntPtr console = GetConsoleWindow();
        if (console == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr owner = GetAncestor(console, RootOwnerFlag);
        return owner == IntPtr.Zero ? console : owner;
    }

    [SupportedOSPlatform("windows")]
    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetConsoleWindow();

    [SupportedOSPlatform("windows")]
    [LibraryImport("user32.dll")]
    private static partial IntPtr GetAncestor(IntPtr window, uint flags);
}
