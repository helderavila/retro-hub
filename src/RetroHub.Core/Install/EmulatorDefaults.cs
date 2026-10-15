using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RetroHub.Core.Input;
using RetroHub.Core.Library;
using RetroHub.Core.Localization;
using RetroHub.Core.Models;

namespace RetroHub.Core.Install;

/// <summary>
/// Writes the manifest's "defaults" (settings an emulator needs to work well, e.g. a renderer) into its config files.
/// Applied on install and before a game opens, once per version of the defaults: an older install gets them on the next game,
/// and if the user changes the setting inside the emulator afterwards it is not undone.
/// </summary>
public sealed class EmulatorDefaults(LibraryDatabase db)
{
    private const string SettingPrefix = "defaults_applied:";

    /// <summary>Applies the defaults if this version was not applied yet. Returns true if it wrote.</summary>
    public bool EnsureApplied(EmulatorManifest manifest, InstalledEmulator installed)
    {
        if (manifest.Defaults.Count == 0)
            return false;
        var fingerprint = Fingerprint(manifest.Defaults);
        if (db.GetSetting(SettingPrefix + manifest.Id) == fingerprint)
            return false;

        var root = Path.GetFullPath(installed.InstallDir) + Path.DirectorySeparatorChar;
        foreach (var file in manifest.Defaults)
        {
            var path = Path.GetFullPath(Path.Combine(installed.InstallDir, file.File));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(L.F("File outside the emulator folder: {0}", file.File));
            IniMerger.Merge(path, file.Format, file.Settings);
        }
        db.SetSetting(SettingPrefix + manifest.Id, fingerprint);
        return true;
    }

    private static string Fingerprint(List<ConfigDefaults> defaults) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(defaults))))[..16];
}
