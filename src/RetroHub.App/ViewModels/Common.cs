using System.Diagnostics;
using System.IO;

namespace RetroHub.App.ViewModels;

/// <summary>Aviso inline. Kind: "ok" | "warn" | "error".</summary>
public sealed record Notice(string Kind, string Message)
{
    public static Notice Ok(string message) => new("ok", message);
    public static Notice Warn(string message) => new("warn", message);
    public static Notice Error(string message) => new("error", message);
}

/// <summary>Páginas que querem atualizar dados ao serem exibidas.</summary>
public interface IActivatable
{
    void OnActivated();
}

internal static class Shell
{
    public static void OpenFolder(string path)
    {
        if (Directory.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    public static string FormatPlaytime(long seconds) => seconds switch
    {
        <= 0 => "—",
        < 60 => "< 1 min",
        < 3600 => $"{seconds / 60} min",
        _ => $"{seconds / 3600}h {seconds % 3600 / 60:00}m",
    };
}
