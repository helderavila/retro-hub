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

    // NSWindowCollectionBehavior: on every Space, including another app's full-screen one.
    private const nuint CanJoinAllSpaces = 1 << 0;
    private const nuint Stationary = 1 << 4;
    private const nuint FullScreenAuxiliary = 1 << 8;
    private const nint ScreenSaverLevel = 1000;

    /// <summary>
    /// Keeps a window above everything, on every Space — also over the emulator while it slides into its own
    /// full-screen Space — so the launch curtain really hides the transition.
    /// </summary>
    public static void FloatOverEverything(Avalonia.Controls.Window window)
    {
        if (!OperatingSystem.IsMacOS() || window.TryGetPlatformHandle() is not { HandleDescriptor: "NSWindow" } handle)
            return;
        try
        {
            objc_msgSend(handle.Handle, sel_registerName("setCollectionBehavior:"), CanJoinAllSpaces | Stationary | FullScreenAuxiliary);
            objc_msgSend(handle.Handle, sel_registerName("setLevel:"), (nuint)ScreenSaverLevel);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    /// <summary>
    /// Brings another process to the front. Since macOS 14 an app can only take the focus when the active app hands it over,
    /// so Retro Hub (active, under the launch curtain) passes it to the emulator once its window is up.
    /// </summary>
    public static void Activate(int pid)
    {
        if (!OperatingSystem.IsMacOS())
            return;
        try
        {
            var app = objc_msgSend_int(objc_getClass("NSRunningApplication"), sel_registerName("runningApplicationWithProcessIdentifier:"), pid);
            if (app == IntPtr.Zero)
                return;
            var shared = objc_msgSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            var yield = sel_registerName("yieldActivationToApplication:");
            if (objc_msgSend_bool(shared, sel_registerName("respondsToSelector:"), yield))
                objc_msgSend_ptr(shared, yield, app);
            objc_msgSend_nuint_bool(app, sel_registerName("activateWithOptions:"), 1); // NSApplicationActivateAllWindows
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    /// <summary>
    /// Back to Retro Hub when a game ends, on the Space its window is on. macOS hands the focus back to Retro Hub by itself
    /// but stays on the first desktop once the emulator's full-screen Space closes; ordering the window front again (as the
    /// Window menu does) after that slide switches to its Space.
    /// </summary>
    public static async void ReturnToHub()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            return;
        await Task.Delay(TimeSpan.FromMilliseconds(900)); // the full-screen Space slides away first
        var window = desktop.Windows.LastOrDefault(w => w.IsVisible && w is not Views.LaunchCurtain);
        if (window is null)
            return;
        if (OperatingSystem.IsMacOS() && window.TryGetPlatformHandle() is { HandleDescriptor: "NSWindow" } handle)
        {
            try
            {
                var app = objc_msgSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
                objc_msgSend_nuint_bool(app, sel_registerName("activateIgnoringOtherApps:"), 1);
                objc_msgSend_ptr(handle.Handle, sel_registerName("makeKeyAndOrderFront:"), IntPtr.Zero);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        }
        window.Activate();
    }

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_int(IntPtr receiver, IntPtr selector, int value);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool objc_msgSend_nuint_bool(IntPtr receiver, IntPtr selector, nuint value);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_ptr(IntPtr receiver, IntPtr selector, IntPtr value);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool objc_msgSend_bool(IntPtr receiver, IntPtr selector, IntPtr value);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern void objc_msgSend(IntPtr receiver, IntPtr selector, nuint value);
}
