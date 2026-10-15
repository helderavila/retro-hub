using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RetroHub.Core.Library;
using RetroHub.Core.Localization;
using RetroHub.Core.Models;

namespace RetroHub.Core.Input;

/// <summary>
/// Writes the manifest's controller mapping into the emulator configuration.
/// Automatic: once per emulator (and again if the manifest or the controller changes) — if the user remaps in the emulator, it is not undone.
/// Forced: "Configure controller" button.
/// Placeholders resolved on the fly: {guid0}/{port0} (player 1) and {guid1}/{port1} (player 2) — SDL identifier of the XInput controller
/// connected to each slot. Keys that depend on a player without a controller are skipped.
/// </summary>
public sealed class ControllerConfigurator(LibraryDatabase db)
{
    private const string SettingPrefix = "controller_configured:";

    /// <summary>Reads the connected controllers. Replaceable in tests.</summary>
    public Func<IReadOnlyList<XInputDeviceInfo>> Devices { get; init; } = XInputDevices.List;

    /// <summary>Applies if not yet applied with this mapping version. Returns true if it wrote.</summary>
    public bool EnsureConfigured(EmulatorManifest manifest, InstalledEmulator installed)
    {
        if (manifest.Controller is not { } config || Resolve(config) is not { } settings)
            return false;
        if (db.GetSetting(SettingPrefix + manifest.Id) == Fingerprint(settings))
            return false;

        Write(manifest, installed, config, settings);
        return true;
    }

    /// <summary>
    /// Writes the mapping (overwriting only the mapping keys). Returns the changed file,
    /// or null if there is no mapping — or if it depends on a controller and none is connected.
    /// </summary>
    public string? Apply(EmulatorManifest manifest, InstalledEmulator installed)
    {
        if (manifest.Controller is not { } config || Resolve(config) is not { } settings)
            return null;
        return Write(manifest, installed, config, settings);
    }

    /// <summary>true if this emulator's mapping needs a connected controller (e.g. emulators that store the controller's SDL identifier).</summary>
    public static bool NeedsConnectedController(ControllerConfig config) =>
        config.Settings.Values.SelectMany(s => s.Values).Any(v => v.Contains("{guid"));

    private string Write(EmulatorManifest manifest, InstalledEmulator installed, ControllerConfig config,
        Dictionary<string, Dictionary<string, string>> settings)
    {
        var root = Path.GetFullPath(installed.InstallDir) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(installed.InstallDir, config.File));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L.F("{0}: controller file outside the emulator folder ({1}).", manifest.Id, config.File));

        IniMerger.Merge(path, config.Format, settings);
        db.SetSetting(SettingPrefix + manifest.Id, Fingerprint(settings));
        return path;
    }

    /// <summary>Replaces the placeholders with the connected controllers; null if player 1 is needed and there is no controller.</summary>
    internal Dictionary<string, Dictionary<string, string>>? Resolve(ControllerConfig config)
    {
        if (!NeedsConnectedController(config))
            return config.Settings;

        var devices = Devices().OrderBy(d => d.Slot).ToList();
        if (devices.Count == 0)
            return null;

        var values = new Dictionary<string, string>();
        for (var player = 0; player < Math.Min(devices.Count, 2); player++)
        {
            var guid = devices[player].SdlGuid;
            // Two identical controllers share the same GUID: SDL tells them apart by "port" (order among equals).
            var port = devices.Take(player).Count(d => d.SdlGuid == guid);
            values[$"{{guid{player}}}"] = guid;
            values[$"{{port{player}}}"] = port.ToString();
        }

        var resolved = new Dictionary<string, Dictionary<string, string>>();
        foreach (var (section, keys) in config.Settings)
        {
            var target = resolved[section] = new Dictionary<string, string>();
            foreach (var (key, value) in keys)
            {
                var playerToken = PlayerOf(key) ?? PlayerOf(value);
                if (playerToken is { } p && p >= devices.Count)
                    continue; // player without a controller: leave as is
                var text = value;
                foreach (var (token, replacement) in values)
                    text = text.Replace(token, replacement);
                if (!text.Contains("{guid") && !text.Contains("{port"))
                    target[key] = text;
            }
        }
        return resolved;
    }

    /// <summary>Player a key/value refers to: "player_1_…" or "{guid1}" → 1.</summary>
    private static int? PlayerOf(string text)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text, @"player_(\d)_|\{(?:guid|port)(\d)\}");
        if (!m.Success) return null;
        return int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
    }

    private static string Fingerprint(Dictionary<string, Dictionary<string, string>> settings) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings))))[..16];
}
