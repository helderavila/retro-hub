using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using RetroHub.Mac.Helpers;
using RetroHub.Mac.ViewModels;

namespace RetroHub.Mac.Views;

/// <summary>
/// Loading screen over everything from "Play" until the emulator's window is on screen: the user sees the game's art and
/// then the game, never the emulator's main window, its menus or the switch into full screen.
/// </summary>
public partial class LaunchCurtain : Window
{
    /// <summary>Longest wait for the emulator's window before stepping aside anyway.</summary>
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(15);

    /// <summary>
    /// After the window shows: time for the emulator to slide into its own full-screen Space (macOS animates it, no setting
    /// avoids that; the curtain floats on every Space, so it stays on top meanwhile) and draw its first frame.
    /// </summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(1200);

    private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _closing;

    public LaunchCurtain() => InitializeComponent();

    public static LaunchCurtain Show(GameItemViewModel item)
    {
        // Not activated: Retro Hub is already in front, and opening the curtain later than the emulator would take the focus
        // back from it.
        var curtain = new LaunchCurtain { DataContext = item, ShowActivated = false };
        ScreenCover.Apply(curtain);
        curtain.Opened += (_, _) =>
        {
            MacPresentation.FloatOverEverything(curtain);
            curtain.Root.Opacity = 1;
            curtain._opened.TrySetResult();
        };
        curtain.Show();
        return curtain;
    }

    /// <summary>Steps aside once the emulator's window is up (or it quit, or it took too long).</summary>
    public async void Watch(Process emulator)
    {
        try
        {
            await _opened.Task;
            if (await EmulatorWindow.WaitForAsync(emulator, MaxWait))
                MacPresentation.Activate(emulator.Id); // the emulator goes full screen in front, the curtain steps aside
        }
        catch (InvalidOperationException) { /* process already gone */ }
        await Task.Delay(Settle);
        Dismiss();
    }

    /// <summary>Fades out and closes (safe to call more than once, from any thread).</summary>
    public void Dismiss() => Dispatcher.UIThread.Post(async () =>
    {
        if (_closing) return;
        _closing = true;
        Root.Opacity = 0;
        await Task.Delay(220);
        Close();
    });
}
