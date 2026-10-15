using RetroHub.Core.Localization;

namespace RetroHub.Mac.Helpers;

/// <summary>
/// Desktop shortcut that opens a game directly: a tiny app bundle whose script runs "Retro Hub --play &lt;id&gt;"
/// (the macOS counterpart of the Windows .lnk).
/// </summary>
public static class Shortcuts
{
    public static string CreateForGame(long gameId, string title, string? iconPath)
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException();
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException(L.T("Retro Hub executable not found."));
        var safe = string.Concat(title.Split(Path.GetInvalidFileNameChars().Append(':').ToArray())).Trim();
        var bundle = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{safe}.app");
        var macOs = Path.Combine(bundle, "Contents", "MacOS");
        Directory.CreateDirectory(macOs);

        var script = Path.Combine(macOs, "play");
        File.WriteAllText(script, $"#!/bin/sh\nexec \"{exe.Replace("\"", "\\\"")}\" --play {gameId}\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                                     | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        var name = System.Security.SecurityElement.Escape(title);
        File.WriteAllText(Path.Combine(bundle, "Contents", "Info.plist"), $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>CFBundleName</key><string>{name}</string>
              <key>CFBundleExecutable</key><string>play</string>
              <key>CFBundleIdentifier</key><string>com.retrohub.play.{gameId}</string>
              <key>CFBundlePackageType</key><string>APPL</string>
              <key>LSUIElement</key><true/>
            </dict>
            </plist>
            """);
        return bundle;
    }
}
