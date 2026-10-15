using System.IO;

namespace RetroHub.App.Helpers;

/// <summary>Atalho .lnk na área de trabalho que abre um jogo direto (RetroHub.exe --play &lt;id&gt;).</summary>
public static class Shortcuts
{
    public static string CreateForGame(long gameId, string title, string? iconPath)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Executável do Retro Hub não encontrado.");
        var safe = string.Concat(title.Split(Path.GetInvalidFileNameChars())).Trim();
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{safe}.lnk");

        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell indisponível.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(path);
            link.TargetPath = exe;
            link.Arguments = $"--play {gameId}";
            link.WorkingDirectory = Path.GetDirectoryName(exe);
            link.Description = $"Jogar {title} no Retro Hub";
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
