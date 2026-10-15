using System.Diagnostics;
using System.Text;

namespace RetroHub.Core.Util;

public static class ProcessRunner
{
    /// <summary>Roda um processo sem janela, repassando stdout/stderr linha a linha.</summary>
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
            // winget desenha spinners ("-", "\", "|", "/") e barras; ignora linhas curtas demais.
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
            try { process.Kill(entireProcessTree: true); } catch { /* já saiu */ }
            throw;
        }

        return process.ExitCode;
    }
}
