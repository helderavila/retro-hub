using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace RetroHub.Mac.Helpers;

/// <summary>
/// Development aid: RETROHUB_SNAPSHOT=/path/out.png renders the window to a PNG a few seconds after it opens and quits
/// (RETROHUB_SNAPSHOT_TAB=emulators picks the page; RETROHUB_SNAPSHOT_SCAN=1 scans the ROM folders first).
/// Lets the UI be checked without screen captures.
/// </summary>
public static class Snapshot
{
    public static bool Requested => Environment.GetEnvironmentVariable("RETROHUB_SNAPSHOT") is { Length: > 0 };

    public static void Schedule(Window window, Action<string>? selectTab, Action? scan, Action quit)
    {
        var path = Environment.GetEnvironmentVariable("RETROHUB_SNAPSHOT")!;
        if (Environment.GetEnvironmentVariable("RETROHUB_SNAPSHOT_TAB") is { Length: > 0 } tab)
            selectTab?.Invoke(tab);
        if (Environment.GetEnvironmentVariable("RETROHUB_SNAPSHOT_SCAN") == "1")
            scan?.Invoke();
        DispatcherTimer.RunOnce(() =>
        {
            var size = new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height);
            using var bitmap = new RenderTargetBitmap(size);
            bitmap.Render(window);
            bitmap.Save(path);
            quit();
        }, TimeSpan.FromSeconds(int.TryParse(Environment.GetEnvironmentVariable("RETROHUB_SNAPSHOT_DELAY"), out var s) ? s : 4));
    }
}
