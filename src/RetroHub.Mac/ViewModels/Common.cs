namespace RetroHub.Mac.ViewModels;

/// <summary>Inline notice. Kind: "ok" | "warn" | "error".</summary>
public sealed record Notice(string Kind, string Message)
{
    public static Notice Ok(string message) => new("ok", message);
    public static Notice Warn(string message) => new("warn", message);
    public static Notice Error(string message) => new("error", message);
}

/// <summary>Pages that want to refresh their data when shown.</summary>
public interface IActivatable
{
    void OnActivated();
}

internal static class Shell
{
    public static void OpenFolder(string path) => Helpers.MacShell.OpenFolder(path);

    public static string FormatPlaytime(long seconds) => seconds switch
    {
        <= 0 => "—",
        < 60 => "< 1 min",
        < 3600 => $"{seconds / 60} min",
        _ => $"{seconds / 3600}h {seconds % 3600 / 60:00}m",
    };
}
