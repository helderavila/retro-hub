using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RetroHub.Mac.Helpers;

/// <summary>Knows when an emulator has opened its window (CoreGraphics window list), so a loading screen can step aside.</summary>
public static class EmulatorWindow
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    // Every window, not only on-screen ones: an emulator that goes full screen while Retro Hub is still in front gets its own
    // Space, which macOS doesn't switch to until the emulator is activated.
    private const uint AllWindows = 0;
    private const int CFNumberSInt64 = 4;
    private const uint Utf8 = 0x08000100;

    /// <summary>Waits until <paramref name="process"/> shows a real window (not a tiny helper one), it exits, or the timeout passes.</summary>
    public static async Task<bool> WaitForAsync(Process process, TimeSpan timeout, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout && !ct.IsCancellationRequested)
        {
            if (process.HasExited)
                return false;
            if (HasVisibleWindow(process.Id))
                return true;
            await Task.Delay(100, ct).ContinueWith(_ => { });
        }
        return false;
    }

    public static bool HasVisibleWindow(int pid)
    {
        if (!OperatingSystem.IsMacOS())
            return false;
        var list = IntPtr.Zero;
        var pidKey = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowOwnerPID", Utf8);
        var layerKey = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowLayer", Utf8);
        var boundsKey = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowBounds", Utf8);
        try
        {
            list = CGWindowListCopyWindowInfo(AllWindows, 0);
            if (list == IntPtr.Zero)
                return false;
            for (long i = 0, n = CFArrayGetCount(list); i < n; i++)
            {
                var window = CFArrayGetValueAtIndex(list, i);
                if (Number(window, pidKey) != pid || Number(window, layerKey) != 0)
                    continue;
                var bounds = CFDictionaryGetValue(window, boundsKey);
                if (bounds != IntPtr.Zero && CGRectMakeWithDictionaryRepresentation(bounds, out var rect) && rect.Width >= 320 && rect.Height >= 200)
                    return true;
            }
            return false;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            if (list != IntPtr.Zero) CFRelease(list);
            CFRelease(pidKey);
            CFRelease(layerKey);
            CFRelease(boundsKey);
        }
    }

    private static long? Number(IntPtr dictionary, IntPtr key)
    {
        var value = CFDictionaryGetValue(dictionary, key);
        return value != IntPtr.Zero && CFNumberGetValue(value, CFNumberSInt64, out var number) ? number : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public double X, Y, Width, Height;
    }

    [DllImport(CoreGraphics)] private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);
    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dictionary, out CGRect rect);
    [DllImport(CoreFoundation)] private static extern long CFArrayGetCount(IntPtr array);
    [DllImport(CoreFoundation)] private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, long index);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFNumberGetValue(IntPtr number, int type, out long value);
    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr value);
}
