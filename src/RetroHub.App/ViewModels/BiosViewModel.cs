using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RetroHub.Core;
using RetroHub.Core.Bios;
using RetroHub.Core.Models;

namespace RetroHub.App.ViewModels;

public sealed record BiosRow(string File, string Md5, string Description, string StatusKind, string StatusText, string? FoundPath);

public sealed record BiosSystemItem(string Name, string ShortName, string StatusKind, string StatusText, string? Note, IReadOnlyList<BiosRow> Rows);

public partial class BiosViewModel : ObservableObject, IActivatable
{
    private readonly HubServices _services;
    private IReadOnlyList<SystemBiosReport>? _reports;

    public ObservableCollection<BiosSystemItem> Systems { get; } = [];
    public string BiosDir => _services.Paths.Bios;

    /// <summary>Sistemas que não precisam de dump nenhum (sem BIOS ou com BIOS emulada).</summary>
    public string NoBiosNeeded { get; }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string? _busyText;
    [ObservableProperty] private Notice? _notice;

    public BiosViewModel(HubServices services)
    {
        _services = services;
        var free = services.Manifests.Systems
            .Where(s => s.Bios.Count == 0 || s.BiosHle is not null || s.BiosRule == BiosRule.None)
            .Select(s => s.BiosHle is null ? s.ShortName : $"{s.ShortName}*")
            .ToList();
        NoBiosNeeded = string.Join("  ", free);
    }

    public void OnActivated()
    {
        if (_reports is null)
            _ = VerifyAsync();
    }

    [RelayCommand]
    private async Task VerifyAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        BusyText = "verificando…";
        try
        {
            ApplyReports(await Task.Run(() => _services.Bios.VerifyAsync()));
        }
        catch (Exception ex)
        {
            Notice = Notice.Error($"Falha ao verificar: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
    }

    [RelayCommand]
    private Task ImportFolderAsync()
    {
        var dialog = new OpenFolderDialog { Title = "Onde estão os dumps de BIOS? (pasta, pendrive…)", Multiselect = true };
        return dialog.ShowDialog() == true ? ImportAsync(dialog.FolderNames) : Task.CompletedTask;
    }

    [RelayCommand]
    private Task ImportFilesAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolha arquivos de BIOS",
            Multiselect = true,
            Filter = "BIOS e .zip|*.bin;*.rom;*.zip;*.*",
        };
        return dialog.ShowDialog() == true ? ImportAsync(dialog.FileNames) : Task.CompletedTask;
    }

    /// <summary>Também chamado pelo arrastar-e-soltar da tela.</summary>
    public async Task ImportAsync(IReadOnlyList<string> sources)
    {
        if (IsBusy || sources.Count == 0)
            return;

        IsBusy = true;
        Notice = null;
        var progress = new Progress<string>(source => BusyText = $"lendo {Path.GetFileName(source.Split(" › ")[^1])}");
        try
        {
            var report = await _services.Bios.ImportAsync(sources, progress);
            ApplyReports(report.Reports);
            Notice = Describe(report);
        }
        catch (Exception ex)
        {
            Notice = Notice.Error($"Falha ao importar: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
    }

    [RelayCommand]
    private async Task SyncAsync()
    {
        if (_reports is null)
            await VerifyAsync();
        if (_reports is null)
            return;

        try
        {
            var copied = _services.Bios.SyncToEmulators(_reports);
            Notice = copied == 0
                ? Notice.Ok("Os emuladores instalados já têm todas as BIOS encontradas.")
                : Notice.Ok($"{copied} arquivo(s) copiado(s) para as pastas dos emuladores.");
            await VerifyAsync();
        }
        catch (Exception ex)
        {
            Notice = Notice.Error($"Falha ao copiar: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenFolder() => Shell.OpenFolder(BiosDir);

    [RelayCommand]
    private void DismissNotice() => Notice = null;

    private void ApplyReports(IReadOnlyList<SystemBiosReport> reports)
    {
        _reports = reports;
        Systems.Clear();
        // Primeiro o que ainda falta, depois o que está pronto.
        foreach (var report in reports.OrderBy(r => r.Satisfied).ThenBy(r => r.System.BiosRule == BiosRule.None))
            Systems.Add(ToItem(report));

        var required = reports.Where(r => r.System.BiosRule != BiosRule.None).ToList();
        Summary = $"{required.Count(r => r.Satisfied)} de {required.Count} sistemas que exigem BIOS estão prontos · " +
                  $"{_services.Bios.SearchDirectories().Count} pastas verificadas";
    }

    private static Notice Describe(BiosImportReport report)
    {
        if (report.Imported.Count == 0)
        {
            return report.AlreadyPresent > 0
                ? Notice.Ok($"Nada novo: as {report.AlreadyPresent} BIOS encontradas já estavam importadas.")
                : Notice.Warn($"Nenhuma BIOS conhecida entre {report.FilesExamined} arquivo(s) com tamanho compatível. " +
                              "A identificação é pelo MD5 — dumps de outra revisão não são reconhecidos.");
        }

        var list = string.Join(", ", report.Imported.Select(i => $"{i.FileName} ({i.System})"));
        var emulators = report.CopiedToEmulators > 0 ? $" {report.CopiedToEmulators} cópia(s) feitas para os emuladores instalados." : "";
        return Notice.Ok($"{report.Imported.Count} BIOS importada(s): {list}.{emulators}");
    }

    private static BiosSystemItem ToItem(SystemBiosReport report)
    {
        var rows = report.Checks.Select(c =>
        {
            var (kind, text) = c.Status switch
            {
                BiosStatus.Ok => ("ok", "Encontrada"),
                BiosStatus.Misnamed => ("warn", $"Nome diferente — será copiada como {c.Requirement.File}"),
                BiosStatus.HashMismatch => ("error", "Hash não confere"),
                _ => c.Requirement.Optional ? ("idle", "Opcional") : ("idle", "Ausente"),
            };
            var md5 = c.Requirement.Md5.FirstOrDefault() is { } h ? h : c.Requirement.Size is { } s ? $"{s / 1024} KiB" : "—";
            return new BiosRow(c.Requirement.File, md5, c.Requirement.Description, kind, text, c.FoundPath);
        }).ToList();

        var (systemKind, systemText) = report switch
        {
            { UsesHle: true } => ("ok", "Pronto · BIOS emulada"),
            { System.BiosRule: BiosRule.None } => (report.Checks.Any(c => c.IsUsable) ? "ok" : "idle", "Opcional"),
            { Satisfied: true } => ("ok", "Pronto"),
            _ => ("warn", report.System.BiosRule == BiosRule.Any ? "Falta BIOS (qualquer uma)" : "Falta BIOS"),
        };

        return new BiosSystemItem(report.System.Name, report.System.ShortName, systemKind, systemText, report.System.BiosHle, rows);
    }
}
