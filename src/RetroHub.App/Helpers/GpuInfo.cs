using System.Text.RegularExpressions;
using Microsoft.Win32;
using RetroHub.Core.Input;

namespace RetroHub.App.Helpers;

/// <summary>Graphics card and the image quality level recommended for it.</summary>
public static partial class GpuInfo
{
    // "Display adapters" device class.
    private const string DisplayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    [GeneratedRegex(@"RTX\s*(3|4|5)\d{3}|RX\s*(6|7|9)\d{3}|Arc\s*(A7|B5|B7)", RegexOptions.IgnoreCase)]
    private static partial Regex HighEnd();

    [GeneratedRegex(@"NVIDIA|GeForce|Radeon\s*RX|Arc", RegexOptions.IgnoreCase)]
    private static partial Regex Dedicated();

    private static readonly Lazy<IReadOnlyList<string>> Adapters = new(ReadAdapters);

    /// <summary>Name of the best card (the dedicated one when there is integrated + dedicated).</summary>
    public static string? BestAdapter =>
        Adapters.Value.FirstOrDefault(a => HighEnd().IsMatch(a))
        ?? Adapters.Value.FirstOrDefault(a => Dedicated().IsMatch(a))
        ?? Adapters.Value.FirstOrDefault();

    /// <summary>RTX 30/40/50, RX 6000+ → Maximum · other dedicated → Enhanced · integrated only → Original.</summary>
    public static string RecommendedPreset => BestAdapter switch
    {
        { } name when HighEnd().IsMatch(name) => EmulatorSettings.Maximum,
        { } name when Dedicated().IsMatch(name) => EmulatorSettings.Enhanced,
        _ => EmulatorSettings.Original,
    };

    private static IReadOnlyList<string> ReadAdapters()
    {
        var names = new List<string>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(DisplayClass);
            foreach (var sub in key?.GetSubKeyNames() ?? [])
            {
                if (!int.TryParse(sub, out _)) continue; // "0000", "0001"…
                using var adapter = key!.OpenSubKey(sub);
                if (adapter?.GetValue("DriverDesc") is string desc && !string.IsNullOrWhiteSpace(desc))
                    names.Add(desc);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { }
        return names.Distinct().ToList();
    }
}
