using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RetroHub.Core;
using RetroHub.Core.Localization;

namespace RetroHub.App.ViewModels;

public sealed record LanguageOption(string Code, string Name);

public sealed record SystemInfoRow(string Name, string ShortName, string Folders, string Extensions, string Emulator);

public partial class FoldersViewModel : ObservableObject, IActivatable
{
    private readonly HubServices _services;

    public ObservableCollection<string> Folders { get; } = [];
    public IReadOnlyList<SystemInfoRow> Systems { get; }
    public string DataRoot => _services.Paths.Root;
    public string ManifestsDir => _services.Paths.Manifests;
    public IReadOnlyList<string> ManifestWarnings => _services.Manifests.Warnings;
    public bool HasFolders => Folders.Count > 0;
    public bool HasWarnings => ManifestWarnings.Count > 0;

    // ── Save backup ─────────────────────────────────────────────────────────

    public string BackupDir => _services.Backup.Destination;

    public string LastBackupText => _services.Backup.LastBackup is { } t
        ? L.F("last backup: {0}", L.DateTime(t))
        : L.T("no backup made yet");

    [ObservableProperty] private bool _isBackingUp;
    [ObservableProperty] private Notice? _backupNotice;

    public bool AutoBackup
    {
        get => _services.Backup.AutoEnabled;
        set
        {
            _services.Database.SetSetting(Core.Saves.BackupService.AutoSetting, value ? "1" : "0");
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void ChangeBackupDir()
    {
        var dialog = new OpenFolderDialog { Title = L.T("Where to keep backups (tip: a OneDrive or Google Drive folder)") };
        if (dialog.ShowDialog() != true) return;
        _services.Database.SetSetting(Core.Saves.BackupService.DirSetting, dialog.FolderName);
        OnPropertyChanged(nameof(BackupDir));
    }

    [RelayCommand]
    private async Task BackupNowAsync()
    {
        IsBackingUp = true;
        try
        {
            var result = await _services.Backup.CreateAsync();
            BackupNotice = result is null
                ? Notice.Warn(L.T("Nothing to back up yet: install an emulator and play something."))
                : Notice.Ok(L.F("{0} file(s), {1:0.0} MB → {2}", result.Files, result.Bytes / 1048576.0, Path.GetFileName(result.ZipPath)));
            OnPropertyChanged(nameof(LastBackupText));
        }
        catch (Exception ex)
        {
            BackupNotice = Notice.Error(L.F("Backup failed: {0}", ex.Message));
        }
        finally
        {
            IsBackingUp = false;
        }
    }

    [RelayCommand]
    private void OpenBackupDir()
    {
        Directory.CreateDirectory(BackupDir);
        Shell.OpenFolder(BackupDir);
    }

    /// <summary>Opens Retro Hub straight into TV mode (to use the PC as a console).</summary>
    public bool StartInTv
    {
        get => _services.Database.GetSetting(MainViewModel.StartInTvSetting) == "1";
        set
        {
            _services.Database.SetSetting(MainViewModel.StartInTvSetting, value ? "1" : "0");
            OnPropertyChanged();
        }
    }

    // ── Language ────────────────────────────────────────────────────────────

    public IReadOnlyList<LanguageOption> Languages { get; } = L.Languages.Select(l => new LanguageOption(l.Code, l.Name)).ToList();

    /// <summary>Chosen interface language; saved right away, applied on the next start.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LanguageRestartNeeded))]
    private string _selectedLanguage = L.Language;

    /// <summary>The saved language differs from the one loaded at startup.</summary>
    public bool LanguageRestartNeeded => SelectedLanguage != L.Language;

    partial void OnSelectedLanguageChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
            _services.Database.SetSetting(L.Setting, value);
    }

    [RelayCommand]
    private void Restart()
    {
        if (Environment.ProcessPath is { } exe)
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        Application.Current.Shutdown();
    }

    public FoldersViewModel(HubServices services)
    {
        _services = services;
        Systems = services.Manifests.Systems
            .Select(s => new SystemInfoRow(
                L.T(s.Name),
                L.T(s.ShortName),
                string.Join("  ", s.FolderAliases.Take(3).Select(a => a + "\\")),
                string.Join(" ", s.Extensions),
                s.DefaultEmulator is { } e ? services.Manifests.GetEmulator(e)?.Name ?? e : "—"))
            .ToList();
    }

    public void OnActivated() => Load();

    private void Load()
    {
        Folders.Clear();
        foreach (var folder in _services.Database.GetRomFolders())
            Folders.Add(folder);
        OnPropertyChanged(nameof(HasFolders));
    }

    [RelayCommand]
    private void AddFolder()
    {
        var dialog = new OpenFolderDialog { Title = L.T("Choose ROM folders"), Multiselect = true };
        if (dialog.ShowDialog() != true)
            return;

        foreach (var folder in dialog.FolderNames)
            _services.Database.AddRomFolder(folder);
        Load();
    }

    [RelayCommand]
    private void RemoveFolder(string path)
    {
        _services.Database.RemoveRomFolder(path);
        Load();
    }

    [RelayCommand]
    private void OpenFolder(string path) => Shell.OpenFolder(path);
}
