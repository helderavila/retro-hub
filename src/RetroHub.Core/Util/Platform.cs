using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace RetroHub.Core.Util;

/// <summary>What differs between Windows and macOS: manifest overlay key, libretro core extension, app bundles.</summary>
public static partial class Platform
{
    [SupportedOSPlatformGuard("macos")]
    public static bool IsMac => OperatingSystem.IsMacOS();

    [SupportedOSPlatformGuard("windows")]
    public static bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>Key of the per-platform block in emulator manifests ("macos": { … } replaces the top-level keys it names).</summary>
    public static string? ManifestKey => IsMac ? "macos" : null;

    /// <summary>CPU folder name in macOS download paths (e.g. the libretro buildbot): "arm64" (Apple Silicon) or "x86_64" (Intel).</summary>
    public static string MacArch => RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x86_64";

    /// <summary>libretro core file: "snes9x" → "snes9x_libretro.dll" (Windows) or "snes9x_libretro.dylib" (macOS).</summary>
    public static string CoreFileName(string core) => $"{core}_libretro.{(IsMac ? "dylib" : "dll")}";

    [GeneratedRegex(@"<key>CFBundleExecutable</key>\s*<string>([^<]+)</string>")]
    private static partial Regex BundleExecutable();

    /// <summary>
    /// "Foo.app" → "Foo.app/Contents/MacOS/&lt;CFBundleExecutable&gt;" (the binary that runs and blocks until the app quits).
    /// Anything that is not an app bundle is returned as is.
    /// </summary>
    public static string ResolveAppBundle(string path)
    {
        var trimmed = path.TrimEnd('/');
        if (!trimmed.EndsWith(".app", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(trimmed))
            return path;

        var macOs = Path.Combine(trimmed, "Contents", "MacOS");
        var plist = Path.Combine(trimmed, "Contents", "Info.plist");
        if (File.Exists(plist) && BundleExecutable().Match(File.ReadAllText(plist)) is { Success: true } m)
        {
            var exe = Path.Combine(macOs, m.Groups[1].Value.Trim());
            if (File.Exists(exe))
                return exe;
        }

        // Binary plist or no CFBundleExecutable: the bundle name is the usual binary name.
        var byName = Path.Combine(macOs, Path.GetFileNameWithoutExtension(trimmed));
        if (File.Exists(byName))
            return byName;
        return Directory.Exists(macOs) ? Directory.EnumerateFiles(macOs).FirstOrDefault() ?? path : path;
    }

    /// <summary>Asks a process to quit like closing its window: SIGTERM on macOS, WM_CLOSE on Windows. false if it could not be asked.</summary>
    public static bool RequestClose(System.Diagnostics.Process process)
    {
        if (IsWindows)
            return process.CloseMainWindow();
        return Kill(process.Id, SigTerm) == 0;
    }

    private const int SigTerm = 15;

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);
}
