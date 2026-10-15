using System.Diagnostics;
using RetroHub.Core.Install;
using RetroHub.Core.Library;
using RetroHub.Core.Manifests;
using RetroHub.Core.Models;

namespace RetroHub.Core.Tests;

/// <summary>
/// Instalação real no Mac (baixa ~410 MB): só roda com RETROHUB_NET_TESTS=1 em macOS.
/// Confere que cada app chega inteiro (binário executável, assinatura de código intacta depois da cópia).
/// </summary>
public class MacInstallTests
{
    private static bool Enabled => OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable("RETROHUB_NET_TESTS") == "1";

    [Theory]
    [InlineData("duckstation", false)]
    [InlineData("pcsx2", true)]
    [InlineData("ppsspp", true)]
    [InlineData("dolphin", true)]
    [InlineData("retroarch", true)]
    public async Task Installs_the_mac_app(string id, bool notarized)
    {
        if (!Enabled)
            return;

        using var dir = new TempDir();
        var paths = new AppPaths { Root = Path.Combine(dir.Path, "data"), Manifests = Path.Combine(AppContext.BaseDirectory, "manifests") };
        paths.EnsureCreated();
        var manifests = ManifestRepository.LoadFor("macos", paths.Manifests);
        var db = new LibraryDatabase(paths.Database);
        db.Initialize();
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RetroHub-tests");
        var installer = new EmulatorInstaller(paths, http, new DownloadSourceResolver(http), new DependencyInstaller(), manifests, db);

        var log = new List<string>();
        var manifest = manifests.GetEmulator(id)!;
        var installed = await installer.InstallAsync(manifest, new SyncProgress(u => { if (u.Log is { } l) log.Add(l); }), CancellationToken.None);

        Assert.True(File.Exists(installed.ExecutablePath), string.Join("\n", log));
        Assert.Contains(".app/Contents/MacOS/", installed.ExecutablePath);
        Assert.True(File.GetUnixFileMode(installed.ExecutablePath).HasFlag(UnixFileMode.UserExecute));
        foreach (var action in manifest.PostInstall)
            Assert.True(File.Exists(Path.Combine(installed.InstallDir, action.Path)) || Directory.Exists(Path.Combine(installed.InstallDir, action.Path)), action.Path);

        var bundle = installed.ExecutablePath[..(installed.ExecutablePath.IndexOf(".app/", StringComparison.Ordinal) + 4)];
        if (notarized)
        {
            var (code, output) = Run("/usr/bin/codesign", "--verify", "--deep", "--strict", bundle);
            Assert.True(code == 0, $"codesign: {output}");
        }

        if (manifest.Cores is not null)
        {
            var core = Path.Combine(installed.InstallDir, "cores", Util.Platform.CoreFileName("snes9x"));
            Assert.True(File.Exists(core), string.Join("\n", log));
        }
    }

    private static (int, string) Run(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output);
    }

    /// <summary>Progress&lt;T&gt; posts to a sync context; here lines are collected right away.</summary>
    private sealed class SyncProgress(Action<InstallUpdate> report) : IProgress<InstallUpdate>
    {
        public void Report(InstallUpdate value) => report(value);
    }
}
