using System.Runtime.InteropServices;

namespace RetroHub.Mac.Helpers;

/// <summary>
/// Hides the menu bar and the Dock while Retro Hub is the active app (NSApplication presentationOptions), for TV mode.
/// macOS restores them by itself when another app (e.g. the emulator) comes to the front.
/// </summary>
public static class MacPresentation
{
    private const nuint Default = 0;
    private const nuint HideDock = 1 << 1;
    private const nuint HideMenuBar = 1 << 3;

    public static void SetKiosk(bool on)
    {
        if (!OperatingSystem.IsMacOS())
            return;
        try
        {
            var app = objc_msgSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            objc_msgSend(app, sel_registerName("setPresentationOptions:"), on ? HideDock | HideMenuBar : Default);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern void objc_msgSend(IntPtr receiver, IntPtr selector, nuint value);
}
