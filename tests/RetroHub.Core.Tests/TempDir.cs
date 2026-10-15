namespace RetroHub.Core.Tests;

internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "retrohub-tests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string relative, byte[] content)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllBytes(full, content);
        return full;
    }

    public string File(string relative, string content) => File(relative, System.Text.Encoding.UTF8.GetBytes(content));

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

/// <summary>An executable present on every machine, to stand in for an emulator.</summary>
internal static class TestPlatform
{
    public static string Shell => OperatingSystem.IsWindows() ? System.IO.Path.Combine(Environment.SystemDirectory, "cmd.exe") : "/bin/sh";

    /// <summary>Arguments that keep <see cref="Shell"/> running for about a minute.</summary>
    public static string LongRunningArgs => OperatingSystem.IsWindows() ? "/c ping -n 60 127.0.0.1 >nul & rem {rom}" : "-c \"sleep 60\" {rom}";

    /// <summary>Name of the process <see cref="LongRunningArgs"/> leaves running.</summary>
    public static string LongRunningProcess => OperatingSystem.IsWindows() ? "PING" : "sleep";
}
