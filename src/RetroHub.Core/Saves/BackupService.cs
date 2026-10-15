using System.IO.Compression;
using RetroHub.Core.Library;
using RetroHub.Core.Manifests;

namespace RetroHub.Core.Saves;

public sealed record BackupResult(string ZipPath, int Files, long Bytes);

/// <summary>
/// Copies memory cards, saves and save states of all installed emulators into a dated .zip.
/// Configurable destination (e.g. a OneDrive/Google Drive folder to keep it in the cloud). Keeps the 10 most recent.
/// </summary>
public sealed class BackupService(AppPaths paths, ManifestRepository manifests, LibraryDatabase db)
{
    public const string DirSetting = "backup_dir";
    public const string LastSetting = "backup_last";
    public const string AutoSetting = "backup_auto";
    private const int Keep = 10;

    public string Destination => db.GetSetting(DirSetting) is { Length: > 0 } dir ? dir : Path.Combine(paths.Root, "backups");

    public DateTimeOffset? LastBackup =>
        long.TryParse(db.GetSetting(LastSetting), out var unix) ? DateTimeOffset.FromUnixTimeSeconds(unix) : null;

    /// <summary>Automatic backup is on by default.</summary>
    public bool AutoEnabled => db.GetSetting(AutoSetting) != "0";

    public bool IsDue => AutoEnabled && (LastBackup is null || DateTimeOffset.UtcNow - LastBackup > TimeSpan.FromDays(1));

    public async Task<BackupResult?> CreateAsync(CancellationToken ct = default)
    {
        var sources = new List<(string EmulatorId, string Dir)>();
        foreach (var installed in db.GetInstalledEmulators())
        {
            if (manifests.GetEmulator(installed.Id)?.Saves is not { } saves) continue;
            foreach (var relative in saves.Backup)
            {
                var dir = Path.Combine(installed.InstallDir, relative);
                if (Directory.Exists(dir)) sources.Add((installed.Id, dir));
            }
        }

        if (sources.Count == 0)
            return null;

        var destination = Destination;
        Directory.CreateDirectory(destination);
        var zipPath = Path.Combine(destination, $"RetroHub-saves-{DateTime.Now:yyyy-MM-dd_HH-mm}.zip");
        var partial = zipPath + ".part";
        int files = 0;
        long bytes = 0;

        await Task.Run(() =>
        {
            using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
            {
                foreach (var (emulatorId, dir) in sources)
                {
                    var installDir = db.GetInstalledEmulators().First(e => e.Id == emulatorId).InstallDir;
                    foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        var entryName = Path.Combine(emulatorId, Path.GetRelativePath(installDir, file)).Replace('\\', '/');
                        try
                        {
                            // FileShare.ReadWrite: the emulator may be open with the memory card.
                            using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            using var output = zip.CreateEntry(entryName, CompressionLevel.Optimal).Open();
                            input.CopyTo(output);
                            files++;
                            bytes += input.Length;
                        }
                        catch (IOException) { /* locked file: left for the next backup */ }
                    }
                }
            }
            File.Move(partial, zipPath, overwrite: true);
        }, ct);

        db.SetSetting(LastSetting, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        Prune(destination);
        return new BackupResult(zipPath, files, bytes);
    }

    private static void Prune(string destination)
    {
        foreach (var old in Directory.GetFiles(destination, "RetroHub-saves-*.zip").OrderByDescending(f => f).Skip(Keep))
        {
            try { File.Delete(old); } catch (IOException) { }
        }
    }
}
