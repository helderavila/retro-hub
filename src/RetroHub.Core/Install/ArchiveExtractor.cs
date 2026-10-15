using System.Diagnostics;
using System.IO.Compression;
using RetroHub.Core.Util;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace RetroHub.Core.Install;

public static class ArchiveExtractor
{
    /// <summary>
    /// Extracts .zip (built-in) or .7z/.rar/.tar.* (SharpCompress) to <paramref name="destination"/>.
    /// On macOS, .dmg is mounted and copied, and .zip/.tar.* go through ditto/tar so app bundles keep their symlinks and permissions.
    /// </summary>
    public static void Extract(string archivePath, string destination, bool stripRoot)
    {
        Directory.CreateDirectory(destination);

        var handled = Platform.IsMac && ExtractMac(archivePath, destination);
        if (!handled && archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, destination, overwriteFiles: true);
        }
        else if (!handled)
        {
            // Sequential extraction (efficient for "solid" 7z); SharpCompress blocks path traversal.
            ArchiveFactory.WriteToDirectory(archivePath, destination,
                new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
        }

        if (stripRoot)
            StripSingleRoot(destination);
    }

    /// <summary>macOS formats that need the system tools. false = not handled here (e.g. .7z, which SharpCompress extracts).</summary>
    private static bool ExtractMac(string archivePath, string destination)
    {
        var name = archivePath.ToLowerInvariant();
        if (name.EndsWith(".dmg"))
        {
            var mount = Path.Combine(Path.GetTempPath(), "retrohub-dmg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(mount);
            Run("hdiutil", "attach", "-nobrowse", "-readonly", "-noautoopen", "-mountpoint", mount, archivePath);
            try
            {
                // Only the apps and folders: a disk image also carries an /Applications alias and hidden layout files.
                foreach (var entry in Directory.EnumerateFileSystemEntries(mount))
                {
                    var fileName = Path.GetFileName(entry);
                    if (fileName.StartsWith('.') || new FileInfo(entry).LinkTarget is not null)
                        continue;
                    Run("ditto", entry, Path.Combine(destination, fileName));
                }
            }
            finally
            {
                try { Run("hdiutil", "detach", mount); }
                catch (InvalidOperationException) { Run("hdiutil", "detach", mount, "-force"); }
                try { Directory.Delete(mount); } catch (IOException) { }
            }
            return true;
        }
        if (name.EndsWith(".zip"))
        {
            Run("ditto", "-x", "-k", archivePath, destination);
            RemoveMacMetadata(destination);
            return true;
        }
        if (name.EndsWith(".tar.gz") || name.EndsWith(".tgz") || name.EndsWith(".tar.xz") || name.EndsWith(".tar.bz2") || name.EndsWith(".tar"))
        {
            Run("tar", "-xf", archivePath, "-C", destination);
            return true;
        }
        return false;
    }

    /// <summary>"__MACOSX" folders that some zips carry would defeat stripRoot.</summary>
    private static void RemoveMacMetadata(string dir)
    {
        var junk = Path.Combine(dir, "__MACOSX");
        if (Directory.Exists(junk))
            Directory.Delete(junk, recursive: true);
    }

    /// <summary>
    /// Downloaded apps run without the "downloaded from the internet" prompt: clears the quarantine flag and makes sure the binary is executable.
    /// No-op outside macOS.
    /// </summary>
    public static void PrepareForLaunch(string installDir, string executable)
    {
        if (!Platform.IsMac)
            return;
        try { Run("xattr", "-dr", "com.apple.quarantine", installDir); }
        catch (InvalidOperationException) { /* nothing quarantined */ }
        if (File.Exists(executable))
        {
            File.SetUnixFileMode(executable, File.GetUnixFileMode(executable)
                | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
    }

    private static void Run(string tool, params string[] args)
    {
        var psi = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"{tool} did not start");
        process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{tool} {string.Join(' ', args)}: {error.Trim()}");
    }

    /// <summary>If the folder contains only one subfolder (e.g. "RetroArch-Win64/"), moves its contents up.</summary>
    private static void StripSingleRoot(string dir)
    {
        if (Directory.GetFiles(dir).Length != 0)
            return;
        var dirs = Directory.GetDirectories(dir);
        // A macOS app bundle is the thing itself, never a wrapper folder.
        if (dirs.Length != 1 || dirs[0].EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            return;

        var temp = dir.TrimEnd(Path.DirectorySeparatorChar) + "_root";
        Directory.Move(dirs[0], temp);
        Directory.Delete(dir, recursive: true);
        Directory.Move(temp, dir);
    }

    /// <param name="preserve">Relative paths not overwritten if they already exist (user settings).</param>
    public static void CopyDirectory(string source, string destination, IReadOnlyCollection<string>? preserve = null)
    {
        var keep = new HashSet<string>((preserve ?? []).Select(p => p.Replace('/', Path.DirectorySeparatorChar)), StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(destination);

        // macOS app bundles are replaced whole with ditto: a file-by-file copy would turn their framework symlinks into duplicates
        // and break the code signature. User files never live inside a bundle.
        var bundles = new List<string>();
        if (Platform.IsMac)
        {
            // Bundles from an older version that the new archive no longer has (e.g. "PCSX2-v2.8.2.app" after an update).
            var incoming = Directory.EnumerateDirectories(source, "*.app").Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (incoming.Count > 0 && Directory.Exists(destination))
                foreach (var stale in Directory.EnumerateDirectories(destination, "*.app").Where(d => !incoming.Contains(Path.GetFileName(d))))
                    Directory.Delete(stale, recursive: true);

            foreach (var app in Directory.EnumerateDirectories(source, "*.app", SearchOption.AllDirectories))
            {
                if (bundles.Any(b => app.StartsWith(b + Path.DirectorySeparatorChar)))
                    continue;
                bundles.Add(app);
                var target = Path.Combine(destination, Path.GetRelativePath(source, app));
                if (Directory.Exists(target))
                    Directory.Delete(target, recursive: true);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                Run("ditto", app, target);
            }
        }
        bool InBundle(string path) => bundles.Any(b => path == b || path.StartsWith(b + Path.DirectorySeparatorChar));

        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories).Where(d => !InBundle(d)))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Where(f => !InBundle(f)))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            // A listed file, or anything inside a listed folder (e.g. a portable-mode "user" folder).
            if (File.Exists(target) && keep.Any(k => relative.Equals(k, StringComparison.OrdinalIgnoreCase)
                                                    || relative.StartsWith(k.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                continue;
            File.Copy(file, target, overwrite: true);
        }
    }
}
