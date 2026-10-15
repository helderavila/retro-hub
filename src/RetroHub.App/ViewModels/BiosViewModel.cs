using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RetroHub.Core;
using RetroHub.Core.Bios;
using RetroHub.Core.Localization;
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

    /// <summary>Systems that need no dump at all (no BIOS or an emulated BIOS).</summary>
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
            .Select(s => s.BiosHle is null ? L.T(s.ShortName) : $"{L.T(s.ShortName)}*")
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
        BusyText = L.T("checking…");
        try
        {
            ApplyReports(await Task.Run(() => _services.Bios.VerifyAsync()));
        }
        catch (Exception ex)
        {
            Notice = Notice.Error(L.F("Check failed: {0}", ex.Message));
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
        var dialog = new OpenFolderDialog { Title = L.T("Where are your BIOS dumps? (folder, USB drive…)"), Multiselect = true };
        return dialog.ShowDialog() == true ? ImportAsync(dialog.FolderNames) : Task.CompletedTask;
    }

    [RelayCommand]
    private Task ImportFilesAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = L.T("Choose BIOS files"),
            Multiselect = true,
            Filter = L.T("BIOS and .zip|*.bin;*.rom;*.zip;*.*"),
        };
        return dialog.ShowDialog() == true ? ImportAsync(dialog.FileNames) : Task.CompletedTask;
    }

    /// <summary>Also called by the view's drag and drop.</summary>
    public async Task ImportAsync(IReadOnlyList<string> sources)
    {
        if (IsBusy || sources.Count == 0)
            return;

        IsBusy = true;
        Notice = null;
        var progress = new Progress<string>(source => BusyText = L.F("reading {0}", Path.GetFileName(source.Split(" › ")[^1])));
        try
        {
            var report = await _services.Bios.ImportAsync(sources, progress);
            ApplyReports(report.Reports);
            Notice = Describe(report);
        }
        catch (Exception ex)
        {
            Notice = Notice.Error(L.F("Import failed: {0}", ex.Message));
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
                ? Notice.Ok(L.T("The installed emulators already have every BIOS found."))
                : Notice.Ok(L.F("{0} file(s) copied to the emulator folders.", copied));
            await VerifyAsync();
        }
        catch (Exception ex)
        {
            Notice = Notice.Error(L.F("Copy failed: {0}", ex.Message));
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
        // Missing first, then what is ready.
        foreach (var report in reports.OrderBy(r => r.Satisfied).ThenBy(r => r.System.BiosRule == BiosRule.None))
            Systems.Add(ToItem(report));

        var required = reports.Where(r => r.System.BiosRule != BiosRule.None).ToList();
        Summary = L.F("{0} of {1} systems that require a BIOS are ready · {2} folders checked",
            required.Count(r => r.Satisfied), required.Count, _services.Bios.SearchDirectories().Count);
    }

    private static Notice Describe(BiosImportReport report)
    {
        if (report.Imported.Count == 0)
        {
            return report.AlreadyPresent > 0
                ? Notice.Ok(L.F("Nothing new: the {0} BIOS found were already imported.", report.AlreadyPresent))
                : Notice.Warn(L.F("No known BIOS among {0} file(s) of a matching size. Identification is by MD5, so dumps from another revision are not recognized.", report.FilesExamined));
        }

        var list = string.Join(", ", report.Imported.Select(i => $"{i.FileName} ({i.System})"));
        return Notice.Ok(report.CopiedToEmulators > 0
            ? L.F("{0} BIOS imported: {1}. {2} copy(ies) made to the installed emulators.", report.Imported.Count, list, report.CopiedToEmulators)
            : L.F("{0} BIOS imported: {1}.", report.Imported.Count, list));
    }

    private static BiosSystemItem ToItem(SystemBiosReport report)
    {
        var rows = report.Checks.Select(c =>
        {
            var (kind, text) = c.Status switch
            {
                BiosStatus.Ok => ("ok", L.T("Found")),
                BiosStatus.Misnamed => ("warn", L.F("Different name — will be copied as {0}", c.Requirement.File)),
                BiosStatus.HashMismatch => ("error", L.T("Hash mismatch")),
                _ => c.Requirement.Optional ? ("idle", L.T("Optional")) : ("idle", L.T("Missing")),
            };
            var md5 = c.Requirement.Md5.FirstOrDefault() is { } h ? h : c.Requirement.Size is { } s ? $"{s / 1024} KiB" : "—";
            return new BiosRow(c.Requirement.File, md5, L.T(c.Requirement.Description), kind, text, c.FoundPath);
        }).ToList();

        var (systemKind, systemText) = report switch
        {
            { UsesHle: true } => ("ok", L.T("Ready · emulated BIOS")),
            { System.BiosRule: BiosRule.None } => (report.Checks.Any(c => c.IsUsable) ? "ok" : "idle", L.T("Optional")),
            { Satisfied: true } => ("ok", L.T("Ready")),
            _ => ("warn", report.System.BiosRule == BiosRule.Any ? L.T("BIOS missing (any one)") : L.T("BIOS missing")),
        };

        return new BiosSystemItem(L.T(report.System.Name), L.T(report.System.ShortName), systemKind, systemText,
            report.System.BiosHle is { } hle ? L.T(hle) : null, rows);
    }
}
