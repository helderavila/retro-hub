using System.Diagnostics;
using System.Text.RegularExpressions;
using RetroHub.Core.Input;

namespace RetroHub.Mac.Helpers;

/// <summary>Graphics chip and the image quality level recommended for it (same role as the Windows GpuInfo).</summary>
public static partial class GpuInfo
{
    [GeneratedRegex(@"Apple M\d+\s+(Pro|Max|Ultra)", RegexOptions.IgnoreCase)]
    private static partial Regex HighEnd();

    [GeneratedRegex(@"Apple M\d+", RegexOptions.IgnoreCase)]
    private static partial Regex AppleSilicon();

    private static readonly Lazy<string?> Chip = new(ReadChip);

    /// <summary>"Apple M2 Pro", "Intel(R) Core(TM) i7…", or null if unknown.</summary>
    public static string? BestAdapter => Chip.Value;

    /// <summary>M-series Pro/Max/Ultra → Maximum · other M-series → Enhanced · Intel Macs → Original.</summary>
    public static string RecommendedPreset => BestAdapter switch
    {
        { } name when HighEnd().IsMatch(name) => EmulatorSettings.Maximum,
        { } name when AppleSilicon().IsMatch(name) => EmulatorSettings.Enhanced,
        _ => EmulatorSettings.Original,
    };

    private static string? ReadChip()
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/sbin/sysctl", "-n machdep.cpu.brand_string")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(psi);
            var name = process?.StandardOutput.ReadToEnd().Trim();
            process?.WaitForExit();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
