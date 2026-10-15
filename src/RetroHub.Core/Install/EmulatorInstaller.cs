using System.IO.Compression;
using RetroHub.Core.Library;
using RetroHub.Core.Localization;
using RetroHub.Core.Manifests;
using RetroHub.Core.Models;
using RetroHub.Core.Util;

namespace RetroHub.Core.Install;

/// <summary>
/// Pipeline: resolve version → dependencies (winget) → download → verify SHA-256 →
/// extract to staging → copy to emulators/{id} → post-install (portable mode) → cores → register.
/// </summary>
public sealed class EmulatorInstaller(
    AppPaths paths,
    HttpClient http,
    DownloadSourceResolver resolver,
    DependencyInstaller dependencies,
    ManifestRepository manifests,
    LibraryDatabase db)
{
    public async Task<InstalledEmulator> InstallAsync(EmulatorManifest manifest, IProgress<InstallUpdate> progress, CancellationToken ct)
    {
        if (manifest.Source.Type == "local")
            return RegisterLocal(manifest, progress);

        progress.Report(new InstallUpdate(L.T("Resolving version"), null, L.F("→ {0}: querying {1}", manifest.Name, DescribeSource(manifest.Source))));
        var resolved = await resolver.ResolveAsync(manifest.Source, ct);
        progress.Report(InstallUpdate.Line(L.F("  version {0} · {1}", resolved.Version, resolved.FileName)));

        var deps = manifest.Dependencies.Select(manifests.GetDependency).OfType<DependencyDefinition>().ToList();
        await dependencies.EnsureAsync(deps, progress, ct);

        var archive = Path.Combine(paths.Downloads, manifest.Id, resolved.FileName);
        progress.Report(new InstallUpdate(L.T("Downloading"), 0, $"↓ {resolved.Url}"));
        await DownloadAsync(resolved.Url, archive, progress, ct);

        progress.Report(new InstallUpdate(L.T("Checking hash"), null, null));
        var actualSha = await FileHasher.Sha256Async(archive, ct);
        var verified = false;
        if (resolved.Sha256 is { } expected)
        {
            if (!string.Equals(actualSha, expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(archive);
                throw new InvalidDataException(L.F("SHA-256 mismatch.\n  expected {0}\n  got      {1}", expected, actualSha));
            }
            verified = true;
            progress.Report(InstallUpdate.Line($"✓ sha256 {actualSha}"));
        }
        else
        {
            progress.Report(InstallUpdate.Line(L.F("! the source does not publish a hash — recorded sha256 {0}", actualSha)));
        }

        var target = paths.EmulatorDir(manifest.Id);
        var staging = Path.Combine(paths.Staging, $"{manifest.Id}-{Guid.NewGuid():N}");
        progress.Report(new InstallUpdate(L.T("Extracting"), null, L.F("→ extracting to {0}", target)));
        try
        {
            await Task.Run(() => ArchiveExtractor.Extract(archive, staging, manifest.Extract.StripRoot), ct);
            await Task.Run(() => ArchiveExtractor.CopyDirectory(staging, target, manifest.Preserve), ct);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }

        progress.Report(new InstallUpdate(L.T("Configuring"), null, null));
        RunPostInstall(manifest, target, progress);
        if (manifest.BiosDir is not null)
            Directory.CreateDirectory(Path.Combine(target, manifest.BiosDir));

        var exe = FindExecutable(target, manifest.Executable)
            ?? throw new FileNotFoundException(L.F("Executable '{0}' not found in {1}.", manifest.Executable, target));

        if (manifest.Cores is not null)
        {
            var cores = manifest.Launch.Select(p => p.Core).OfType<string>().Distinct().ToList();
            await InstallCoresAsync(manifest, cores, target, progress, ct);
        }

        var record = new InstalledEmulator
        {
            Id = manifest.Id,
            Version = resolved.Version,
            InstallDir = target,
            ExecutablePath = exe,
            Sha256 = actualSha,
            Verified = verified,
            InstalledAt = DateTimeOffset.UtcNow,
        };
        db.SaveInstalledEmulator(record);

        progress.Report(new InstallUpdate(L.T("Done"), 1, L.F("✓ {0} {1} ready", manifest.Name, resolved.Version)));
        return record;
    }

    /// <summary>
    /// Emulator already on the computer: only records where it is. Nothing is downloaded, copied or changed in its folder
    /// (controller and graphics settings, if the manifest asks, are written by the Emulators screen like any other).
    /// </summary>
    private InstalledEmulator RegisterLocal(EmulatorManifest manifest, IProgress<InstallUpdate> progress)
    {
        var path = manifest.Source.Path ?? throw new InvalidDataException(L.F("{0}: local source without 'path'.", manifest.Name));
        string exe;
        if (File.Exists(path))
            exe = path;
        else
            exe = FindExecutable(path, manifest.Executable)
                ?? throw new FileNotFoundException(L.F("Executable '{0}' not found in {1}.", manifest.Executable, path));

        var record = new InstalledEmulator
        {
            Id = manifest.Id,
            Version = "local",
            InstallDir = Path.GetDirectoryName(exe)!,
            ExecutablePath = exe,
            Sha256 = "",
            Verified = false,
            InstalledAt = DateTimeOffset.UtcNow,
        };
        db.SaveInstalledEmulator(record);
        progress.Report(new InstallUpdate(L.T("Done"), 1, L.F("✓ {0} registered: {1}", manifest.Name, exe)));
        return record;
    }

    /// <summary>Newest available version, if different from the installed one. "url" sources (pinned version) only change with the manifest.</summary>
    public async Task<string?> CheckUpdateAsync(EmulatorManifest manifest, InstalledEmulator installed, CancellationToken ct = default)
    {
        if (manifest.Source.Type == "local")
            return null; // the user updates it, in their own folder
        var latest = await resolver.ResolveAsync(manifest.Source, ct);
        return string.Equals(latest.Version, installed.Version, StringComparison.OrdinalIgnoreCase) ? null : latest.Version;
    }

    /// <summary>Removes the installation. A "local" emulator (user's folder) is only unregistered: its files stay.</summary>
    public void Uninstall(EmulatorManifest manifest)
    {
        if (manifest.Source.Type != "local")
            TryDeleteDirectory(paths.EmulatorDir(manifest.Id));
        db.RemoveInstalledEmulator(manifest.Id);
    }

    public async Task InstallCoresAsync(EmulatorManifest manifest, IReadOnlyList<string> cores, string installDir,
        IProgress<InstallUpdate> progress, CancellationToken ct)
    {
        if (manifest.Cores is null || cores.Count == 0)
            return;

        var coreDir = Path.Combine(installDir, manifest.Cores.TargetDir);
        Directory.CreateDirectory(coreDir);

        for (var i = 0; i < cores.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var core = cores[i];
            progress.Report(new InstallUpdate(L.F("Core {0}/{1}: {2}", i + 1, cores.Count, core), (double)i / cores.Count, null));

            var url = manifest.Cores.UrlTemplate.Replace("{core}", core);
            var zip = Path.Combine(paths.Downloads, manifest.Id, "cores", $"{core}.zip");
            try
            {
                await DownloadAsync(url, zip, progress: null, ct);
                ZipFile.ExtractToDirectory(zip, coreDir, overwriteFiles: true);
                progress.Report(InstallUpdate.Line($"✓ core {core}"));
            }
            catch (HttpRequestException ex)
            {
                progress.Report(InstallUpdate.Line($"✗ core {core}: {ex.Message}"));
            }
        }
    }

    private async Task DownloadAsync(string url, string destination, IProgress<InstallUpdate>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;

        var partial = destination + ".part";
        long received = 0;
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
        {
            var buffer = new byte[1 << 16];
            var lastReport = 0L;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                received += read;
                if (progress is not null && total > 0 && Environment.TickCount64 - lastReport > 100)
                {
                    lastReport = Environment.TickCount64;
                    progress.Report(new InstallUpdate(null, (double)received / total.Value, null));
                }
            }
        }

        File.Move(partial, destination, overwrite: true);
        progress?.Report(new InstallUpdate(null, 1, L.F("  {0} downloaded", FormatSize(received))));
    }

    private static void RunPostInstall(EmulatorManifest manifest, string target, IProgress<InstallUpdate> progress)
    {
        var root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
        foreach (var action in manifest.PostInstall)
        {
            var path = Path.GetFullPath(Path.Combine(target, action.Path));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(L.F("postInstall outside the emulator folder: {0}", action.Path));

            switch (action.Action)
            {
                case "createFile":
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    if (!File.Exists(path))
                        File.WriteAllText(path, action.Content ?? "");
                    progress.Report(InstallUpdate.Line($"  + {action.Path}"));
                    break;
                case "createDir":
                    Directory.CreateDirectory(path);
                    progress.Report(InstallUpdate.Line($"  + {action.Path}{Path.DirectorySeparatorChar}"));
                    break;
                default:
                    progress.Report(InstallUpdate.Line(L.F("! unknown action ignored: {0}", action.Action)));
                    break;
            }
        }
    }

    internal static string? FindExecutable(string installDir, string pattern)
    {
        if (!Directory.Exists(installDir))
            return null;

        if (!Glob.HasWildcards(pattern))
        {
            var direct = Path.Combine(installDir, pattern);
            if (File.Exists(direct))
                return direct;
        }

        var name = Path.GetFileName(pattern);
        return Directory.EnumerateFiles(installDir, name, SearchOption.AllDirectories)
            .OrderBy(p => p.Length) // prefer the shallowest
            .FirstOrDefault();
    }

    private static string DescribeSource(SourceSpec s) =>
        s.Type switch
        {
            "github" => $"github.com/{s.Repo}",
            "forgejo" => $"{new Uri(s.Api ?? "about:blank").Host}/{s.Repo}",
            "local" => s.Path ?? L.T("local folder"),
            _ => new Uri(s.Url ?? "about:blank").Host,
        };

    private static string FormatSize(long bytes) =>
        bytes >= 1 << 20 ? $"{bytes / (double)(1 << 20):0.0} MB" : $"{bytes / 1024.0:0} KB";

    private static void TryDeleteDirectory(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
