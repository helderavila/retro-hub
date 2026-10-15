using RetroHub.Core.Manifests;
using RetroHub.Core.Util;

namespace RetroHub.Core.Tests;

/// <summary>Bloco "macos" dos manifestos: substitui as chaves de topo no Mac e é ignorado no Windows.</summary>
public class PlatformTests
{
    private static string Create(TempDir dir)
    {
        dir.File("manifests/systems.json", """{ "systems": [ { "id": "ps1", "name": "PS1", "shortName": "PS1", "extensions": [".cue"] } ] }""");
        dir.File("manifests/emulators/both.json", """
            { "id": "both", "name": "Both", "systems": ["ps1"],
              "source": { "type": "github", "repo": "a/b", "asset": "windows\\.zip$" },
              "executable": "both.exe", "dependencies": ["vcredist-x64"],
              "launch": [ { "systems": ["ps1"], "args": "-batch \"{rom}\"" } ],
              // Comentários continuam valendo dentro do bloco.
              "macos": {
                "source": { "type": "github", "repo": "a/b", "asset": "mac\\.zip$" },
                "executable": "Both.app/Contents/MacOS/Both",
                "dependencies": [],
              } }
            """);
        dir.File("manifests/emulators/windows-only.json", """
            { "id": "windows-only", "name": "Win", "systems": ["ps1"], "source": { "type": "url", "url": "https://x.invalid/a.zip" },
              "executable": "win.exe", "launch": [ { "systems": ["ps1"] } ] }
            """);
        dir.File("manifests/emulators/local.json", """
            { "id": "local", "name": "Local", "systems": ["ps1"], "source": { "type": "local", "path": "/Applications/Local.app" },
              "executable": "Local", "launch": [ { "systems": ["ps1"] } ] }
            """);
        return Path.Combine(dir.Path, "manifests");
    }

    [Fact]
    public void Mac_block_replaces_the_top_level_keys()
    {
        using var dir = new TempDir();
        var mac = ManifestRepository.LoadFor("macos", Create(dir));

        var both = mac.GetEmulator("both")!;
        Assert.Equal("mac\\.zip$", both.Source.Asset);
        Assert.Equal("Both.app/Contents/MacOS/Both", both.Executable);
        Assert.Empty(both.Dependencies);
        Assert.Equal("-batch \"{rom}\"", both.Launch.Single().Args); // not in the block: kept
    }

    [Fact]
    public void Windows_only_downloads_are_hidden_on_mac_but_local_apps_are_kept()
    {
        using var dir = new TempDir();
        var mac = ManifestRepository.LoadFor("macos", Create(dir));
        var windows = ManifestRepository.LoadFor(null, Create(dir));

        Assert.Equal(["both", "local"], mac.Emulators.Select(e => e.Id).Order());
        Assert.Equal(["both", "local", "windows-only"], windows.Emulators.Select(e => e.Id).Order());
        Assert.Equal("both.exe", windows.GetEmulator("both")!.Executable);
    }

    [Fact]
    public void Core_file_name_follows_the_platform()
    {
        Assert.Equal(OperatingSystem.IsMacOS() ? "snes9x_libretro.dylib" : "snes9x_libretro.dll", Platform.CoreFileName("snes9x"));
    }

    [Fact]
    public void App_bundle_resolves_to_its_binary()
    {
        using var dir = new TempDir();
        dir.File("Foo.app/Contents/Info.plist", """
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0"><dict>
              <key>CFBundleExecutable</key>
              <string>foo-qt</string>
            </dict></plist>
            """);
        var binary = dir.File("Foo.app/Contents/MacOS/foo-qt", [0]);
        var app = Path.Combine(dir.Path, "Foo.app");

        Assert.Equal(binary, Platform.ResolveAppBundle(app));
        Assert.Equal(binary, Platform.ResolveAppBundle(app + "/"));
        Assert.Equal(binary, Platform.ResolveAppBundle(binary)); // not a bundle: unchanged
    }
}
