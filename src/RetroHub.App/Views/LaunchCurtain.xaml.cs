using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Animation;
using RetroHub.App.Helpers;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Views;

/// <summary>
/// Loading screen over everything from "Play" until the emulator's window is on screen: the user sees the game's art and
/// then the game, never the emulator's main window, its menus or the switch into full screen.
/// </summary>
public partial class LaunchCurtain : Window
{
    /// <summary>Longest wait for the emulator's window before stepping aside anyway.</summary>
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(15);

    /// <summary>After the window shows: time for the emulator to go full screen and draw its first frame.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(600);

    private bool _closing;

    private LaunchCurtain()
    {
        InitializeComponent();
        Grain.Fill = NoiseBrush.Create((int)FindResource("Grain.MaxAlpha"));
        Loaded += (_, _) =>
        {
            ((Storyboard)FindResource("Pulse")).Begin(this, true);
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
        };
    }

    public static LaunchCurtain Show(GameItemViewModel item)
    {
        var curtain = new LaunchCurtain { DataContext = item };
        curtain.Show();
        return curtain;
    }

    /// <summary>Steps aside once the emulator's window is up (or it quit, or it took too long).</summary>
    public async void Watch(Process emulator)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < MaxWait)
        {
            try
            {
                if (emulator.HasExited) break;
                emulator.Refresh();
                if (emulator.MainWindowHandle != IntPtr.Zero && IsWindowVisible(emulator.MainWindowHandle)) break;
            }
            catch (InvalidOperationException) { break; }
            await Task.Delay(100);
        }
        await Task.Delay(Settle);
        Dismiss();
    }

    /// <summary>Fades out and closes (safe to call more than once, from any thread).</summary>
    public void Dismiss() => Dispatcher.BeginInvoke(() =>
    {
        if (_closing) return;
        _closing = true;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    });

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);
}
