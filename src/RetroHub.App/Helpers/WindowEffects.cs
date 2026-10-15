using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace RetroHub.App.Helpers;

/// <summary>Barra de título escura nativa (Windows 11) com a cor do fundo do app.</summary>
public static class WindowEffects
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void ApplyDarkChrome(Window window, Color caption, Color border)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var on = 1;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref on, sizeof(int));

        // COLORREF = 0x00BBGGRR. Ignorado silenciosamente no Windows 10.
        var captionRef = ToColorRef(caption);
        DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref captionRef, sizeof(int));
        var borderRef = ToColorRef(border);
        DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderRef, sizeof(int));
    }

    private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);
}
