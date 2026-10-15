using System.IO;
using RetroHub.Core.Localization;

namespace RetroHub.App.Helpers;

/// <summary>Desktop .lnk shortcut that opens a game directly (RetroHub.exe --play &lt;id&gt;).</summary>
public static class Shortcuts
{
    public static string CreateForGame(long gameId, string title, string? iconPath)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException(L.T("Retro Hub executable not found."));
        var safe = string.Concat(title.Split(Path.GetInvalidFileNameChars())).Trim();
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{safe}.lnk");

        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException(L.T("WScript.Shell is unavailable."));
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(path);
            link.TargetPath = exe;
            link.Arguments = $"--play {gameId}";
            link.WorkingDirectory = Path.GetDirectoryName(exe);
            link.Description = L.F("Play {0} on Retro Hub", title);
            link.IconLocation = iconPath is not null && File.Exists(iconPath) && iconPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
                ? iconPath
                : exe + ",0";
            link.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
        return path;
    }
}
