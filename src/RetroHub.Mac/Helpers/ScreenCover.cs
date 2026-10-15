using Avalonia.Controls;

namespace RetroHub.Mac.Helpers;

/// <summary>
/// A borderless window over the whole screen, with the menu bar and Dock hidden while Retro Hub is active
/// (TV mode, the launch curtain). Not macOS native full screen: that one animates into a new Space and takes Esc for itself.
/// </summary>
public static class ScreenCover
{
    public static void Apply(Window window)
    {
        window.Opened += (_, _) => Fill(window);
        window.Activated += (_, _) => MacPresentation.SetKiosk(true);
        window.Deactivated += (_, _) => MacPresentation.SetKiosk(false);
        window.Closed += (_, _) => MacPresentation.SetKiosk(false);
    }

    private static void Fill(Window window)
    {
        if ((window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary) is not { } screen)
            return;
        MacPresentation.SetKiosk(true);
        window.Position = screen.Bounds.Position;
        window.Width = screen.Bounds.Width / screen.Scaling;
        window.Height = screen.Bounds.Height / screen.Scaling;
    }
}
