using System.Diagnostics;

namespace RetroHub.Mac.Helpers;

/// <summary>Finder and the default browser, through /usr/bin/open (Explorer's role on Windows).</summary>
public static class MacShell
{
    public static void OpenFolder(string path)
    {
        if (Directory.Exists(path))
            Open(path);
    }

    /// <summary>Finder window with the file selected.</summary>
    public static void Reveal(string file) => Open("-R", file);

    public static void OpenUrl(string url) => Open(url);

    /// <summary>Moves a file to the Trash (recoverable), like Finder's "Move to Trash".</summary>
    public static void MoveToTrash(string file)
    {
        // macOS 15+ ships /usr/bin/trash, which handles every volume's own trash.
        if (File.Exists("/usr/bin/trash"))
        {
            var psi = new ProcessStartInfo("/usr/bin/trash") { UseShellExecute = false, RedirectStandardError = true };
            psi.ArgumentList.Add(file);
            using var process = Process.Start(psi) ?? throw new IOException("trash did not start");
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new IOException(error.Trim());
            return;
        }

        var trash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash");
        var target = Path.Combine(trash, Path.GetFileName(file));
        for (var i = 2; File.Exists(target) || Directory.Exists(target); i++)
            target = Path.Combine(trash, $"{Path.GetFileNameWithoutExtension(file)} {i}{Path.GetExtension(file)}");
        File.Move(file, target);
    }

    private static void Open(params string[] args)
    {
        var psi = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        Process.Start(psi)?.Dispose();
    }
}
