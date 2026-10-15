using System.ComponentModel;
using RetroHub.Core.Localization;
using RetroHub.Core.Models;
using RetroHub.Core.Util;

namespace RetroHub.Core.Install;

/// <summary>Installs system dependencies (VC++ Redist etc.) via winget.</summary>
public sealed class DependencyInstaller
{
    private const string CommonFlags = "--accept-source-agreements --disable-interactivity";

    public async Task<bool> IsWingetAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            return await ProcessRunner.RunAsync("winget", "--version", null, ct) == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    public async Task<bool> IsInstalledAsync(DependencyDefinition dep, CancellationToken ct = default)
    {
        var found = false;
        var code = await ProcessRunner.RunAsync("winget", $"list --id {dep.WingetId} -e {CommonFlags}",
            line => { if (line.Contains(dep.WingetId, StringComparison.OrdinalIgnoreCase)) found = true; }, ct);
        return code == 0 && found;
    }

    public async Task EnsureAsync(IReadOnlyCollection<DependencyDefinition> deps, IProgress<InstallUpdate> progress, CancellationToken ct)
    {
        if (deps.Count == 0)
            return;

        progress.Report(new InstallUpdate(L.T("Checking dependencies"), null, null));
        if (!await IsWingetAvailableAsync(ct))
        {
            foreach (var dep in deps)
                progress.Report(InstallUpdate.Line(L.F("! winget unavailable — install manually: {0} ({1})", dep.Name, dep.WingetId)));
            return;
        }

        foreach (var dep in deps)
        {
            ct.ThrowIfCancellationRequested();
            if (await IsInstalledAsync(dep, ct))
            {
                progress.Report(InstallUpdate.Line(L.F("✓ {0} already installed", dep.Name)));
                continue;
            }

            progress.Report(new InstallUpdate(L.F("Installing {0}", dep.Name), null, $"→ winget install {dep.WingetId}"));
            var code = await ProcessRunner.RunAsync("winget",
                $"install --id {dep.WingetId} -e --silent --accept-package-agreements {CommonFlags}",
                line => progress.Report(InstallUpdate.Line("  " + line)), ct);

            // 3010 = success, restart pending (common with VC++ Redist).
            progress.Report(InstallUpdate.Line(code is 0 or 3010
                ? L.F("✓ {0} installed", dep.Name)
                : L.F("! winget exited with code {0} for {1}; the emulator may not start without it", code, dep.Name)));
        }
    }
}
