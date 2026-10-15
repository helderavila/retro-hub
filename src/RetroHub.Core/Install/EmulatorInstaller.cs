using System.IO.Compression;
using RetroHub.Core.Library;
using RetroHub.Core.Manifests;
using RetroHub.Core.Models;
using RetroHub.Core.Util;

namespace RetroHub.Core.Install;

/// <summary>
/// Pipeline: resolver versão → dependências (winget) → baixar → conferir SHA-256 →
/// extrair em staging → copiar para emulators/{id} → pós-instalação (modo portátil) → cores → registrar.
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

        progress.Report(new InstallUpdate("Resolvendo versão", null, $"→ {manifest.Name}: consultando {DescribeSource(manifest.Source)}"));
        var resolved = await resolver.ResolveAsync(manifest.Source, ct);
        progress.Report(InstallUpdate.Line($"  versão {resolved.Version} · {resolved.FileName}"));

        var deps = manifest.Dependencies.Select(manifests.GetDependency).OfType<DependencyDefinition>().ToList();
        await dependencies.EnsureAsync(deps, progress, ct);

        var archive = Path.Combine(paths.Downloads, manifest.Id, resolved.FileName);
        progress.Report(new InstallUpdate("Baixando", 0, $"↓ {resolved.Url}"));
        await DownloadAsync(resolved.Url, archive, progress, ct);

        progress.Report(new InstallUpdate("Conferindo hash", null, null));
        var actualSha = await FileHasher.Sha256Async(archive, ct);
        var verified = false;
        if (resolved.Sha256 is { } expected)
        {
            if (!string.Equals(actualSha, expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(archive);
                throw new InvalidDataException($"SHA-256 não confere.\n  esperado {expected}\n  obtido   {actualSha}");
            }
            verified = true;
            progress.Report(InstallUpdate.Line($"✓ sha256 {actualSha}"));
        }
        else
        {
            progress.Report(InstallUpdate.Line($"! a fonte não publica hash — registrado sha256 {actualSha}"));
        }

        var target = paths.EmulatorDir(manifest.Id);
        var staging = Path.Combine(paths.Staging, $"{manifest.Id}-{Guid.NewGuid():N}");
        progress.Report(new InstallUpdate("Extraindo", null, $"→ extraindo para {target}"));
        try
        {
            await Task.Run(() => ArchiveExtractor.Extract(archive, staging, manifest.Extract.StripRoot), ct);
            await Task.Run(() => ArchiveExtractor.CopyDirectory(staging, target, manifest.Preserve), ct);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }

        progress.Report(new InstallUpdate("Configurando", null, null));
        RunPostInstall(manifest, target, progress);
        if (manifest.BiosDir is not null)
            Directory.CreateDirectory(Path.Combine(target, manifest.BiosDir));

        var exe = FindExecutable(target, manifest.Executable)
            ?? throw new FileNotFoundException($"Executável '{manifest.Executable}' não encontrado em {target}.");

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

        progress.Report(new InstallUpdate("Concluído", 1, $"✓ {manifest.Name} {resolved.Version} pronto"));
        return record;
    }

    /// <summary>
    /// Emulador que já está no computador: só registra onde ele está. Nada é baixado, copiado ou alterado na pasta dele
    /// (a configuração de controle e imagem, se o manifesto pedir, é gravada pela tela de Emuladores como nos outros).
    /// </summary>
    private InstalledEmulator RegisterLocal(EmulatorManifest manifest, IProgress<InstallUpdate> progress)
    {
        var path = manifest.Source.Path ?? throw new InvalidDataException($"{manifest.Name}: fonte local sem 'path'.");
        string exe;
        if (File.Exists(path))
            exe = path;
        else
            exe = FindExecutable(path, manifest.Executable)
                ?? throw new FileNotFoundException($"Executável '{manifest.Executable}' não encontrado em {path}.");

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
        progress.Report(new InstallUpdate("Concluído", 1, $"✓ {manifest.Name} registrado: {exe}"));
        return record;
    }

    /// <summary>Versão mais nova disponível, se for diferente da instalada. Fontes "url" (versão fixada) só mudam com o manifesto.</summary>
    public async Task<string?> CheckUpdateAsync(EmulatorManifest manifest, InstalledEmulator installed, CancellationToken ct = default)
    {
        if (manifest.Source.Type == "local")
            return null; // quem atualiza é o usuário, na pasta dele
        var latest = await resolver.ResolveAsync(manifest.Source, ct);
        return string.Equals(latest.Version, installed.Version, StringComparison.OrdinalIgnoreCase) ? null : latest.Version;
    }

    /// <summary>Remove a instalação. Emulador "local" (pasta do usuário) só sai do registro: os arquivos dele ficam.</summary>
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
            progress.Report(new InstallUpdate($"Core {i + 1}/{cores.Count}: {core}", (double)i / cores.Count, null));

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
        progress?.Report(new InstallUpdate(null, 1, $"  {FormatSize(received)} baixados"));
    }

    private static void RunPostInstall(EmulatorManifest manifest, string target, IProgress<InstallUpdate> progress)
    {
        var root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
        foreach (var action in manifest.PostInstall)
        {
            var path = Path.GetFullPath(Path.Combine(target, action.Path));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"postInstall fora da pasta do emulador: {action.Path}");

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
                    progress.Report(InstallUpdate.Line($"! ação desconhecida ignorada: {action.Action}"));
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
            .OrderBy(p => p.Length) // prefere o mais raso
            .FirstOrDefault();
    }

    private static string DescribeSource(SourceSpec s) =>
        s.Type switch
        {
            "github" => $"github.com/{s.Repo}",
            "forgejo" => $"{new Uri(s.Api ?? "about:blank").Host}/{s.Repo}",
            "local" => s.Path ?? "pasta local",
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
