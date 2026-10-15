using System.Diagnostics;
using System.Text;

namespace RetroHub.Core.Util;

public static class ProcessRunner
{
    /// <summary>Runs a process without a window, forwarding stdout/stderr line by line.</summary>
    public static async Task<int> RunAsync(string fileName, string arguments, Action<string>? onLine, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = psi };
        DataReceivedEventHandler handler = (_, e) =>
        {
            // winget draws spinners ("-", "\", "|", "/") and bars; ignore lines that are too short.
            if (e.Data is { } line && line.Trim().Length > 2)
                onLine?.Invoke(line.TrimEnd());
        };
        process.OutputDataReceived += handler;
        process.ErrorDataReceived += handler;

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
            throw;
        }

        return process.ExitCode;
    }
}
