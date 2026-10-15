using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RetroHub.Core;

namespace RetroHub.App.ViewModels;

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

    // ── Backup dos saves ────────────────────────────────────────────────────

    public string BackupDir => _services.Backup.Destination;

    public string LastBackupText => _services.Backup.LastBackup is { } t
        ? $"último backup: {t.ToLocalTime():dd/MM/yyyy HH:mm}"
        : "nenhum backup feito ainda";

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
        var dialog = new OpenFolderDialog { Title = "Onde guardar os backups (dica: uma pasta do OneDrive ou Google Drive)" };
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
                ? Notice.Warn("Nada para copiar ainda: instale um emulador e jogue algo.")
                : Notice.Ok($"{result.Files} arquivo(s), {result.Bytes / 1048576.0:0.0} MB → {Path.GetFileName(result.ZipPath)}");
            OnPropertyChanged(nameof(LastBackupText));
        }
        catch (Exception ex)
        {
            BackupNotice = Notice.Error($"Falha no backup: {ex.Message}");
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

    /// <summary>Abre o Retro Hub direto no Modo TV (para usar o PC como console).</summary>
    public bool StartInTv
    {
        get => _services.Database.GetSetting(MainViewModel.StartInTvSetting) == "1";
        set
        {
            _services.Database.SetSetting(MainViewModel.StartInTvSetting, value ? "1" : "0");
            OnPropertyChanged();
        }
    }

    public FoldersViewModel(HubServices services)
    {
        _services = services;
        Systems = services.Manifests.Systems
            .Select(s => new SystemInfoRow(
                s.Name,
                s.ShortName,
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
        var dialog = new OpenFolderDialog { Title = "Escolha pastas de ROMs", Multiselect = true };
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
