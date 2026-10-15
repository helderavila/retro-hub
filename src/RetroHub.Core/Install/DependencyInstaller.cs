using System.ComponentModel;
using RetroHub.Core.Models;
using RetroHub.Core.Util;

namespace RetroHub.Core.Install;

/// <summary>Instala dependências do sistema (VC++ Redist etc.) via winget.</summary>
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

        progress.Report(new InstallUpdate("Verificando dependências", null, null));
        if (!await IsWingetAvailableAsync(ct))
        {
            foreach (var dep in deps)
                progress.Report(InstallUpdate.Line($"! winget indisponível — instale manualmente: {dep.Name} ({dep.WingetId})"));
            return;
        }

        foreach (var dep in deps)
        {
            ct.ThrowIfCancellationRequested();
            if (await IsInstalledAsync(dep, ct))
            {
                progress.Report(InstallUpdate.Line($"✓ {dep.Name} já instalado"));
                continue;
            }

            progress.Report(new InstallUpdate($"Instalando {dep.Name}", null, $"→ winget install {dep.WingetId}"));
            var code = await ProcessRunner.RunAsync("winget",
                $"install --id {dep.WingetId} -e --silent --accept-package-agreements {CommonFlags}",
                line => progress.Report(InstallUpdate.Line("  " + line)), ct);

            // 3010 = sucesso, reinício pendente (comum no VC++ Redist).
            progress.Report(InstallUpdate.Line(code is 0 or 3010
                ? $"✓ {dep.Name} instalado"
                : $"! winget terminou com código {code} para {dep.Name}; o emulador pode não abrir sem ele"));
        }
    }
}
