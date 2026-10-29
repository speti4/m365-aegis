using System.Runtime.InteropServices;

namespace M365Aegis.Gui.Host;

/// <summary>
/// What the smoke run says about the host's window handle: the broker's account picker needs a top-level window as
/// its parent (task 013), so the handle must name a live window that is its own root.
/// </summary>
internal static class WindowCheck
{
    private const uint RootAncestor = 2; // GA_ROOT

    internal static string Describe(nint window)
    {
        if (window == 0)
        {
            return "no handle";
        }

        if (!IsWindow(window))
        {
            return "not a window";
        }

        return GetAncestor(window, RootAncestor) == window ? "top-level" : "child window";
    }

    // DllImport rather than LibraryImport: two blittable calls do not justify AllowUnsafeBlocks for the generator.
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint GetAncestor(nint window, uint flags);
}
