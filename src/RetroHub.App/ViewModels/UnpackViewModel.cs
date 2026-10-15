using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.Core;
using RetroHub.Core.Install;
using RetroHub.Core.Library;
using RetroHub.Core.Localization;
using RetroHub.Core.Models;
using Microsoft.Win32;

namespace RetroHub.App.ViewModels;

/// <summary>A system in the per-row picker; null id = detect it from the extracted disc.</summary>
public sealed record SystemChoice(string? Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A compressed game in the "Extract" dialog: from the library, or a file picked from any folder.</summary>
public sealed partial class UnpackRow : ObservableObject
{
    private readonly Action _changed;
    private readonly Func<string?, CompressedGame> _retarget;

    public UnpackRow(CompressedGame info, bool isChecked, Action changed, Func<string?, CompressedGame> retarget, IReadOnlyList<SystemChoice> systems)
    {
        Info = info;
        _changed = changed;
        _retarget = retarget;
        Systems = systems;
        // Picked file whose system is not known yet: the user can choose it (or let the extracted disc tell).
        NeedsSystem = IsExternal && string.IsNullOrEmpty(info.Game.SystemId);
        _selectedSystem = systems.FirstOrDefault();
        _isChecked = isChecked && info.Advice != UnpackAdvice.KeepCompressed;
    }

    public CompressedGame Info { get; private set; }
    public Game Game => Info.Game;
    public string ArchivePath => Info.Game.Path;
    /// <summary>Not in the library yet (picked with "Add files…"): it is added once extracted.</summary>
    public bool IsExternal => Info.Game.Id == 0;
    public bool NeedsSystem { get; }
    public IReadOnlyList<SystemChoice> Systems { get; }
    public string FileName => Path.GetFileName(Info.Game.Path);
    public string SystemShort => Info.System is { } system ? L.T(system.ShortName) : "?";

    /// <summary>The system to extract as: the chosen one, else the known one, else null (detect).</summary>
    public string? SystemId => NeedsSystem ? SelectedSystem?.Id : Info.Game.SystemId;
    public string SizeText => Info.UnpackedBytes is { } bytes ? UnpackViewModel.FormatSize(bytes) : "?";
    public bool CanExtract => Info.Advice != UnpackAdvice.KeepCompressed;

    /// <summary>"→ ps2/" · "opens compressed in RetroArch" · "arcade sets must stay .zip".</summary>
    public string Note => Info.Advice switch
    {
        _ when NeedsSystem && SelectedSystem?.Id is null => L.T("system detected after extracting"),
        UnpackAdvice.KeepCompressed => L.T("arcade sets must stay .zip"),
        UnpackAdvice.Optional => L.F("→ {0}/ · also opens compressed", Path.GetFileName(Info.TargetDir)),
        _ => $"→ {Path.GetFileName(Info.TargetDir)}/",
    };

    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private SystemChoice? _selectedSystem;

    /// <summary>After extracting: "ok" | "error" (with <see cref="Result"/>).</summary>
    [ObservableProperty] private string? _resultKind;
    [ObservableProperty] private string? _result;

    partial void OnIsCheckedChanged(bool value) => _changed();

    partial void OnSelectedSystemChanged(SystemChoice? value) => Retarget();

    /// <summary>Recomputes the destination (games folder or chosen system changed).</summary>
    public void Retarget()
    {
        Info = _retarget(SystemId);
        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(SystemShort));
        OnPropertyChanged(nameof(CanExtract));
        if (!CanExtract) IsChecked = false;
    }
}

/// <summary>
/// "Extract compressed games": pick games kept as .zip/.7z, extract them into the games folder (&lt;folder&gt;/&lt;system&gt;/)
/// and point the library at the extracted files. Play time, favorites, collections and covers stay.
/// </summary>
public sealed partial class UnpackViewModel : ObservableObject
{
    public const string RootSetting = "unpack_root";
    public const string KeepSetting = "unpack_keep_archive";

    private readonly HubServices _services;
    private CancellationTokenSource? _cts;

    public ObservableCollection<UnpackRow> Rows { get; } = [];

    [ObservableProperty] private string _gamesRoot;
    [ObservableProperty] private bool _keepArchive;
    [ObservableProperty] private bool _isWorking;
    [ObservableProperty] private bool _isDone;
    [ObservableProperty] private double _fraction;
    [ObservableProperty] private string _stage = "";
    [ObservableProperty] private string _log = "";

    public bool HasRows => Rows.Count > 0;

    /// <summary>"Detect" + every system whose games can be extracted (not arcade sets).</summary>
    private readonly IReadOnlyList<SystemChoice> _systems;
    public int SelectedCount => Rows.Count(r => r.IsChecked);
    public string SelectionText => L.F("{0} selected · {1}", SelectedCount,
        FormatSize(Rows.Where(r => r.IsChecked).Sum(r => r.Info.UnpackedBytes ?? 0)));
    public string ExtractLabel => L.F("Extract {0}", SelectedCount);
    public bool CanStart => SelectedCount > 0 && !IsWorking;
    public bool ShowProgress => IsWorking || IsDone;

    /// <summary>True when at least one game changed (the library reloads).</summary>
    public bool Changed { get; private set; }

    public event Action? CloseRequested;

    /// <param name="only">Open with just this game checked (right-click › Extract); null = the recommended ones.</param>
    public UnpackViewModel(HubServices services, IEnumerable<Game> games, long? only = null)
    {
        _services = services;
        var list = games.Where(g => GameUnpacker.IsCompressed(g.Path) && File.Exists(g.Path)).ToList();
        _gamesRoot = services.Database.GetSetting(RootSetting) is { Length: > 0 } saved ? saved : SuggestRoot(list);
        _keepArchive = services.Database.GetSetting(KeepSetting) == "1";
        _systems = [new SystemChoice(null, L.T("Detect")),
            .. services.Manifests.Systems
                .Where(sys => services.Unpacker.Advice(sys.Id) != UnpackAdvice.KeepCompressed)
                .Select(sys => new SystemChoice(sys.Id, L.T(sys.Name)))
                .OrderBy(c => c.Label, StringComparer.CurrentCultureIgnoreCase)];

        foreach (var info in services.Unpacker.FindCompressed(list, _gamesRoot)
                     .OrderBy(i => i.Advice).ThenBy(i => i.Game.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            var check = only is { } id ? info.Game.Id == id : info.Advice == UnpackAdvice.Recommended;
            AddRow(info, check);
        }
    }

    private void AddRow(CompressedGame info, bool check)
    {
        var game = info.Game;
        Rows.Add(new UnpackRow(info, check, SelectionChanged, systemId => Describe(game, systemId), _systems));
        OnPropertyChanged(nameof(HasRows));
        SelectionChanged();
    }

    private CompressedGame Describe(Game game, string? systemId)
    {
        var copy = new Game { Id = game.Id, Path = game.Path, Title = game.Title, SystemId = systemId ?? "" };
        return _services.Unpacker.Describe(copy, GamesRoot);
    }

    /// <summary>.zip/.7z from any folder (Downloads, a USB drive…): they join the list, checked.</summary>
    [RelayCommand]
    private async Task AddFiles() => AddArchives(await PickArchivesAsync());

    internal void AddArchives(IEnumerable<string> files)
    {
        foreach (var file in files.Where(GameUnpacker.IsCompressed))
        {
            if (Rows.Any(r => string.Equals(r.ArchivePath, Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase)))
                continue;
            AddRow(_services.Unpacker.DescribeFile(file, GamesRoot), check: true);
        }
    }

    /// <summary>The ROM folder that already holds most of these games; else the first ROM folder; else %USERPROFILE%\\Games.</summary>
    private string SuggestRoot(IReadOnlyList<Game> games)
    {
        var folders = _services.Database.GetRomFolders();
        var best = folders
            .Select(f => (Folder: f, Count: games.Count(g => g.Path.StartsWith(f.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();
        return best.Folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games");
    }

    private void SelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(ExtractLabel));
        OnPropertyChanged(nameof(CanStart));
    }

    partial void OnIsWorkingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(ShowProgress));
    }

    partial void OnIsDoneChanged(bool value) => OnPropertyChanged(nameof(ShowProgress));

    partial void OnGamesRootChanged(string value)
    {
        foreach (var row in Rows)
            row.Retarget();
    }

    [RelayCommand]
    private void ChangeFolder()
    {
        var dialog = new OpenFolderDialog { Title = L.T("Games folder (each system gets its own subfolder)") };
        if (dialog.ShowDialog() == true)
            GamesRoot = dialog.FolderName;
    }

    private static Task<IReadOnlyList<string>> PickArchivesAsync()
    {
        var dialog = new OpenFileDialog { Title = L.T("Compressed games"), Multiselect = true, Filter = L.T("Archives (.zip, .7z)") + "|*.zip;*.7z" };
        return Task.FromResult<IReadOnlyList<string>>(dialog.ShowDialog() == true ? dialog.FileNames : []);
    }

    [RelayCommand]
    private void SelectAll()
    {
        var on = Rows.Where(r => r.CanExtract).Any(r => !r.IsChecked);
        foreach (var row in Rows.Where(r => r.CanExtract))
            row.IsChecked = on;
    }

    [RelayCommand]
    private async Task Extract()
    {
        var rows = Rows.Where(r => r.IsChecked && r.CanExtract).ToList();
        if (rows.Count == 0 || IsWorking)
            return;

        _services.Database.SetSetting(RootSetting, GamesRoot);
        _services.Database.SetSetting(KeepSetting, KeepArchive ? "1" : "0");
        IsWorking = true;
        IsDone = false;
        Log = "";
        Fraction = 0;
        _cts = new CancellationTokenSource();

        var index = 0;
        var progress = new Progress<InstallUpdate>(u =>
        {
            if (u.Stage is { } stage) { Stage = stage; index++; }
            if (u.Fraction is { } f) Fraction = (index - 1 + f) / rows.Count;
            if (u.Log is { } line) Log += (Log.Length == 0 ? "" : "\n") + line;
        });

        try
        {
            var items = rows.Select(r => new UnpackItem(r.ArchivePath, r.IsExternal ? null : r.Game.Id, r.SystemId)).ToList();
            var results = await _services.Unpacker.UnpackAsync(items, GamesRoot,
                KeepArchive ? null : MoveToRecycleBin, progress, _cts.Token);
            foreach (var result in results)
            {
                var row = rows.First(r => r.ArchivePath == result.Item.ArchivePath);
                row.ResultKind = result.NewPath is null ? "error" : "ok";
                row.Result = result.NewPath is null ? result.Error : L.T("extracted");
                row.IsChecked = false;
            }
            var ok = results.Count(r => r.NewPath is not null);
            Changed |= ok > 0;
            Stage = ok == results.Count
                ? L.F("{0} game(s) extracted to {1}", ok, GamesRoot)
                : L.F("{0} extracted, {1} failed (see below)", ok, results.Count - ok);
            Fraction = 1;
        }
        catch (OperationCanceledException)
        {
            Stage = L.T("Cancelled. Games already extracted stay; the one in progress was left as it was.");
            Changed = true;
        }
        finally
        {
            IsWorking = false;
            IsDone = true;
            _cts.Dispose();
            _cts = null;
            SelectionChanged();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (_cts is { } cts)
            cts.Cancel();
        else
            CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();

    private static void MoveToRecycleBin(string file) =>
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(file,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

    internal static string FormatSize(long bytes) => bytes <= 0 ? "0 MB" : bytes >= 1L << 30
        ? $"{bytes / (double)(1L << 30):0.0} GB"
        : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0} MB" : $"{Math.Max(1, bytes / 1024.0):0} KB";
}
