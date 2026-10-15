using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.Core;
using RetroHub.Core.Covers;
using RetroHub.Core.Launch;
using RetroHub.Core.Library;
using RetroHub.Core.Localization;
using RetroHub.Core.Scanning;
using RetroHub.Mac.Helpers;
using RetroHub.Mac.Views;

namespace RetroHub.Mac.ViewModels;

/// <param name="CollectionId">Set when the chip is a collection instead of a system.</param>
public sealed record SystemChip(string? Id, string Label, int Count, long? CollectionId = null);

/// <summary>Sort option for "All games": <see cref="Key"/> is stable (used by the logic), <see cref="Label"/> is translated.</summary>
public sealed record SortOption(string Key, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Row of the sidebar list (like Steam's): a collapsible group header or a game.</summary>
public sealed class SidebarEntry(string groupKey, string? header, int count, GameItemViewModel? item, bool collapsed = false)
{
    public string GroupKey { get; } = groupKey;
    public string? Header { get; } = header;
    public int Count { get; } = count;
    public GameItemViewModel? Item { get; } = item;
    public bool IsHeader => Item is null;
    public bool IsCollapsed { get; } = collapsed;
    /// <summary>▾ open · ▸ collapsed (a WPF DataTrigger in the Windows app).</summary>
    public string Chevron => IsCollapsed ? "▸" : "▾";
}

public partial class LibraryViewModel : ObservableObject, IActivatable
{
    private readonly HubServices _services;
    private readonly Action<string> _navigate;
    private readonly ObservableCollection<GameItemViewModel> _games = [];

    /// <summary>"All games", filtered and sorted (the WPF app uses an ICollectionView; here the list is rebuilt).</summary>
    [ObservableProperty] private IReadOnlyList<GameItemViewModel> _gamesView = [];
    public ObservableCollection<SystemChip> SystemFilters { get; } = [];

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _favoritesOnly;
    [ObservableProperty] private SystemChip? _selectedSystem;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string? _scanStatus;
    [ObservableProperty] private Notice? _notice;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _isFetchingCovers;
    [ObservableProperty] private string? _coverStatus;
    private bool _coversCheckedThisSession;
    private bool _coversRequestedAgain;

    /// <summary>"Recently played" on the library home (hidden while searching).</summary>
    public ObservableCollection<GameItemViewModel> RecentGames { get; } = [];
    public ObservableCollection<GameItemViewModel> FavoriteGames { get; } = [];

    public bool ShowRecent => RecentGames.Count > 0 && string.IsNullOrWhiteSpace(SearchText);
    public bool ShowFavorites => FavoriteGames.Count > 0 && string.IsNullOrWhiteSpace(SearchText);

    // ── Sidebar list ────────────────────────────────────────────────────────

    public ObservableCollection<SidebarEntry> Sidebar { get; } = [];
    private readonly HashSet<string> _collapsedGroups = [];
    private bool _syncingSelection;

    /// <summary>Game selected in the sidebar; picking one opens its page.</summary>
    [ObservableProperty] private SidebarEntry? _selectedEntry;

    partial void OnSelectedEntryChanged(SidebarEntry? value)
    {
        if (!_syncingSelection && value?.Item is { } item && Page?.Item.Game.Id != item.Game.Id)
            OpenPage(item);
    }

    /// <summary>Favorites first, then one group per system; the search filters the list too.</summary>
    internal void RebuildSidebar()
    {
        var query = SearchText.Trim();
        var visible = _games.Where(g => query.Length == 0 || g.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase).ToList();

        var groups = new List<(string Key, string Title, List<GameItemViewModel> Games)>();
        if (visible.Where(g => g.Favorite).ToList() is { Count: > 0 } favorites)
            groups.Add(("*", L.T("FAVORITES"), favorites));
        foreach (var system in visible.GroupBy(g => g.SystemName).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            groups.Add((system.Key, system.Key.ToUpper(L.Culture), system.ToList()));

        _syncingSelection = true;
        Sidebar.Clear();
        foreach (var (key, title, games) in groups)
        {
            var collapsed = _collapsedGroups.Contains(key) && query.Length == 0;
            Sidebar.Add(new SidebarEntry(key, title, games.Count, null, collapsed));
            if (!collapsed)
                foreach (var game in games)
                    Sidebar.Add(new SidebarEntry(key, null, 0, game));
        }
        SelectedEntry = Page is null ? null : Sidebar.FirstOrDefault(e => e.Item?.Game.Id == Page.Item.Game.Id);
        _syncingSelection = false;
    }

    [RelayCommand]
    private void ToggleGroup(SidebarEntry header)
    {
        if (!_collapsedGroups.Remove(header.GroupKey))
            _collapsedGroups.Add(header.GroupKey);
        RebuildSidebar();
    }

    // ── Sorting of "All games" ──────────────────────────────────────────────

    public IReadOnlyList<SortOption> SortModes { get; } =
    [
        new("name", L.T("Name")),
        new("recent", L.T("Recently played")),
        new("playtime", L.T("Play time")),
        new("added", L.T("Recently added")),
    ];

    [ObservableProperty] private SortOption _sortMode = null!;

    partial void OnSortModeChanged(SortOption value) => RefreshView();

    private void RefreshView()
    {
        Comparison<GameItemViewModel> compare = SortMode?.Key switch
        {
            "recent" => (a, b) => Nullable.Compare(b.Game.LastPlayed, a.Game.LastPlayed),
            "playtime" => (a, b) => b.Game.PlaySeconds.CompareTo(a.Game.PlaySeconds),
            "added" => (a, b) => b.Game.AddedAt.CompareTo(a.Game.AddedAt),
            _ => (_, _) => 0,
        };
        var order = Comparer<GameItemViewModel>.Create((a, b) =>
        {
            var result = compare(a, b);
            return result != 0 ? result : string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
        });
        GamesView = _games.Where(Matches).Order(order).ToList();
    }

    /// <summary>All loaded games (TV mode filters and sorts on its own).</summary>
    public IReadOnlyList<GameItemViewModel> AllGames => _games;

    public bool HasGames => _games.Count > 0;
    public int GameCount => _games.Count;

    public LibraryViewModel(HubServices services, Action<string> navigate)
    {
        _services = services;
        _navigate = navigate;
        _sortMode = SortModes[0];
    }

    public void OnActivated()
    {
        Load();
        // On first open, fill in covers for games added before this feature existed (or while offline).
        if (!_coversCheckedThisSession)
        {
            _coversCheckedThisSession = true;
            _ = FetchCoversAsync();
        }
    }

    public void Load()
    {
        var selected = SelectedSystem?.Id;
        _games.Clear();
        foreach (var game in _services.Database.GetGames())
        {
            var system = _services.Manifests.GetSystem(game.SystemId);
            _games.Add(new GameItemViewModel(game, system, _services.Paths, this));
        }

        RebuildFilters(selected, SelectedSystem?.CollectionId);

        var totalSeconds = _games.Sum(g => g.Game.PlaySeconds);
        Summary = _games.Count == 0
            ? L.T("Your games show up here after the first scan.")
            : L.F("{0} games · {1} systems · {2} played", _games.Count, SystemFilters.Count - 1, Shell.FormatPlaytime(totalSeconds));

        RecentGames.Clear();
        foreach (var recent in _games.Where(g => g.Game.LastPlayed is not null).OrderByDescending(g => g.Game.LastPlayed).Take(8))
            RecentGames.Add(recent);
        OnPropertyChanged(nameof(ShowRecent));
        RefreshFavorites();

        // The open page keeps pointing at the right game after reloading.
        if (Page is not null && _games.FirstOrDefault(g => g.Game.Id == Page.Item.Game.Id) is null)
            Page = null;
        RebuildSidebar();

        OnPropertyChanged(nameof(HasGames));
        OnPropertyChanged(nameof(GameCount));
        RefreshView();
    }

    private Dictionary<long, HashSet<long>> _collectionMembers = [];

    /// <summary>Chips: All · systems · collections (only the ones that have games).</summary>
    internal void RebuildFilters(string? selectedSystem = null, long? selectedCollection = null)
    {
        _collectionMembers = _services.Database.GetCollectionMembers();
        SystemFilters.Clear();
        SystemFilters.Add(new SystemChip(null, L.T("All"), _games.Count));
        foreach (var group in _games.GroupBy(g => g.SystemId).OrderBy(g => g.First().SystemShort))
            SystemFilters.Add(new SystemChip(group.Key, group.First().SystemShort, group.Count()));
        foreach (var collection in _services.Database.GetCollections().Where(c => c.Count > 0))
            SystemFilters.Add(new SystemChip(null, L.T(collection.Name), collection.Count, collection.Id));
        SelectedSystem = SystemFilters.FirstOrDefault(c => c.CollectionId is { } id ? id == selectedCollection : c.Id == selectedSystem && selectedCollection is null)
                         ?? SystemFilters[0];
    }

    public IReadOnlyDictionary<long, HashSet<long>> CollectionMembers => _collectionMembers;

    private bool Matches(GameItemViewModel g) =>
        (string.IsNullOrWhiteSpace(SearchText) || g.Title.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase))
        && (!FavoritesOnly || g.Favorite)
        && (SelectedSystem?.Id is null || g.SystemId == SelectedSystem.Id)
        && (SelectedSystem?.CollectionId is not { } cid || (_collectionMembers.TryGetValue(cid, out var members) && members.Contains(g.Game.Id)));

    partial void OnSearchTextChanged(string value)
    {
        RefreshView();
        OnPropertyChanged(nameof(ShowRecent));
        OnPropertyChanged(nameof(ShowFavorites));
        RebuildSidebar();
    }

    private void RefreshFavorites()
    {
        FavoriteGames.Clear();
        foreach (var favorite in _games.Where(g => g.Favorite).OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase))
            FavoriteGames.Add(favorite);
        OnPropertyChanged(nameof(ShowFavorites));
    }
    partial void OnFavoritesOnlyChanged(bool value) { RefreshView(); OnPropertyChanged(nameof(ShowRecent)); }
    partial void OnSelectedSystemChanged(SystemChip? value) { RefreshView(); OnPropertyChanged(nameof(ShowRecent)); }

    [RelayCommand]
    private async Task ScanAsync()
    {
        var folders = _services.Database.GetRomFolders();
        if (folders.Count == 0)
        {
            Notice = Notice.Warn(L.T("No ROM folders set up. Add one in Folders."));
            return;
        }

        IsScanning = true;
        Notice = null;
        ScanStatus = L.T("preparing…");
        var progress = new Progress<ScanProgress>(p =>
            ScanStatus = L.F("{0} files · {1} games identified", p.FilesSeen, p.Identified));

        try
        {
            var report = await Task.Run(() => _services.Scanner.Scan(folders, progress));
            var result = _services.Database.UpsertScanned(report.Roms);
            var removed = _services.Database.RemoveGamesByPath(report.Rejected.Select(r => r.Path));
            var gone = _services.Database.RemoveMissingGames(folders);
            Load();

            var message = L.F("{0} new games, {1} already known.", result.Added, result.Updated);
            if (gone.Count > 0)
                message += " " + L.F("{0} removed because the file no longer exists.", gone.Count);
            var excluded = _services.Database.ExcludedCount();
            if (excluded > 0)
                message += " " + L.F("{0} removed by you stay out (Reset library brings them back).", excluded);
            if (result.Moved > 0)
                message += " " + L.F("{0} moved to another folder (play time kept).", result.Moved);
            if (result.Duplicates.Count > 0)
                message += " " + DescribeDuplicates(result.Duplicates);
            if (report.Rejected.Count > 0)
            {
                var names = string.Join(", ", report.Rejected.Take(3).Select(r => Path.GetFileName(r.Path)));
                message += " " + (removed > 0
                    ? L.F("{0} file(s) skipped because they are not games — {1}: {2} ({3} removed from the library).",
                        report.Rejected.Count, report.Rejected[0].Reason, names, removed)
                    : L.F("{0} file(s) skipped because they are not games — {1}: {2}.",
                        report.Rejected.Count, report.Rejected[0].Reason, names));
            }

            Notice = report.Unidentified.Count == 0 && report.Rejected.Count == 0 && result.Duplicates.Count == 0
                ? Notice.Ok(message)
                : report.Unidentified.Count == 0
                    ? Notice.Warn(message)
                    : Notice.Warn(message + " " + L.F("{0} files not identified — put them in a folder named after the system (e.g. ps2/, gamecube/).",
                        report.Unidentified.Count));
            _ = FetchCoversAsync();
        }
        catch (Exception ex)
        {
            Notice = Notice.Error(L.F("Scan failed: {0}", ex.Message));
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>Clears the library (including games removed by hand) and scans everything again.</summary>
    [RelayCommand]
    private async Task ResetLibraryAsync()
    {
        if (!await ConfirmDialog.AskAsync(
                L.T("Reset the library?"),
                L.T("All games leave the list and the folders are scanned from scratch. Games you removed come back."),
                L.T("Reset and scan"),
                L.T("Lost: favorites and play time.\nKept: ROM folders, emulators, BIOS, downloaded covers and settings.\nNo files on disk are deleted.")))
            return;

        _services.Database.ClearLibrary();
        Load();
        await ScanAsync();
    }

    /// <summary>Game page open to the right of the list — null = library home.</summary>
    [ObservableProperty] private GameDetailsViewModel? _page;

    internal void OpenPage(GameItemViewModel item)
    {
        Page = new GameDetailsViewModel(item, _services, this);
        Page.LoadPage();
        _syncingSelection = true;
        SelectedEntry = Sidebar.FirstOrDefault(e => e.Item == item);
        _syncingSelection = false;
    }

    /// <summary>Back to the library home ("Home", Esc, and actions that take the game off the list).</summary>
    [RelayCommand]
    internal void CloseDetails()
    {
        Page = null;
        _syncingSelection = true;
        SelectedEntry = null;
        _syncingSelection = false;
    }

    internal Task RemoveFromLibrary(GameItemViewModel item)
    {
        CloseDetails();
        _services.Database.ExcludeGame(item.Game.Id);
        Load();
        Notice = Notice.Ok(L.F("\"{0}\" was removed from the library and will not come back on future scans. The file stays on disk.", item.Title));
        return Task.CompletedTask;
    }

    /// <summary>Sends the game's files to the Trash (recoverable) and removes it from the library.</summary>
    internal async Task DeleteFiles(GameItemViewModel item)
    {
        var files = SheetParser.FilesOfGame(item.FilePath);
        if (files.Count == 0)
        {
            await RemoveFromLibrary(item);
            return;
        }

        var totalBytes = files.Sum(f => new FileInfo(f).Length);
        var detail = string.Join("\n", files.Take(8)) + (files.Count > 8 ? "\n" + L.F("… and {0} more", files.Count - 8) : "");
        if (!await ConfirmDialog.AskAsync(
                L.F("Delete \"{0}\"?", item.Title),
                L.F("{0} file(s), {1}, will go to the Trash. You can restore them from there.", files.Count, FormatSize(totalBytes)),
                L.T("Move to Trash"),
                detail))
            return;

        var failed = new List<string>();
        foreach (var file in files)
        {
            try
            {
                await Task.Run(() => MacShell.MoveToTrash(file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                failed.Add(Path.GetFileName(file));
            }
        }

        _services.Database.RemoveGamesByPath([item.FilePath]);
        CloseDetails();
        Load();
        Notice = failed.Count == 0
            ? Notice.Ok(L.F("\"{0}\" was moved to the Trash ({1} freed once you empty it).", item.Title, FormatSize(totalBytes)))
            : Notice.Warn(L.F("Could not delete: {0} (in use or no permission).", string.Join(", ", failed)));
    }

    internal void OpenFileFolder(GameItemViewModel item)
    {
        if (File.Exists(item.FilePath))
            MacShell.Reveal(item.FilePath);
    }

    private static string FormatSize(long bytes) => bytes >= 1L << 30
        ? $"{bytes / (double)(1L << 30):0.0} GB"
        : $"{bytes / (double)(1L << 20):0} MB";

    [RelayCommand]
    private async Task AddGame()
    {
        var files = await Dialogs.PickFilesAsync(L.T("Add games"), multiple: true);
        if (files.Count == 0)
            return;

        var identified = new List<ScannedRom>();
        var rejected = new List<string>();
        foreach (var file in files)
        {
            if (_services.Scanner.Identify(file, null, out var reason) is { } rom)
                identified.Add(rom);
            else if (reason is not null)
                rejected.Add($"{Path.GetFileName(file)} ({reason})");
        }
        var failed = files.Count - identified.Count - rejected.Count;
        var result = _services.Database.UpsertScanned(identified, source: "manual");
        Load();

        var message = L.F("{0} game(s) added.", result.Added);
        if (result.Duplicates.Count > 0)
            message += " " + DescribeDuplicates(result.Duplicates);
        if (rejected.Count > 0)
            message += " " + L.F("Not games: {0}.", string.Join("; ", rejected));
        if (failed > 0)
            message += " " + L.F("{0} file(s) with no recognized system.", failed);
        Notice = rejected.Count == 0 && failed == 0 && result.Duplicates.Count == 0 ? Notice.Ok(message) : Notice.Warn(message);
        _ = FetchCoversAsync();
    }

    /// <summary>Looks up covers by name for games that do not have one yet. Each card updates as soon as its cover arrives.</summary>
    [RelayCommand]
    private async Task FetchCoversAsync()
    {
        if (IsFetchingCovers)
        {
            _coversRequestedAgain = true; // new games arrived during the lookup: run again at the end
            return;
        }

        var pending = _games.Where(g => !g.HasCover).ToList();
        IsFetchingCovers = true;
        _coversRequestedAgain = false;

        try
        {
            string? coverSummary = null;
            if (pending.Count > 0)
            {
                var done = 0;
                CoverStatus = L.F("fetching covers · {0} of {1}", 0, pending.Count);
                var progress = new Progress<CoverFound>(found =>
                {
                    // Look in the current list: a Load() in the middle of the lookup recreates the cards.
                    _games.FirstOrDefault(g => g.Game.Id == found.GameId)?.SetCover(found.Path);
                    CoverStatus = L.F("fetching covers · {0} of {1}", ++done, pending.Count);
                });
                var (found, missing) = await Task.Run(() =>
                    _services.Covers.FetchMissingAsync(pending.Select(p => p.Game).ToList(), progress));
                if (missing > 0)
                    coverSummary = L.F("{0} covers downloaded · {1} with no cover found by name (right-click › Game page › Change cover)", found, missing);
            }

            // After the covers, the info (year, genre, synopsis…) — same open source, no account.
            var games = _games.Select(g => g.Game).Where(g => Core.Metadata.GameMetadata.FromJson(g.MetadataJson)?.Name is null).ToList();
            if (games.Count > 0)
            {
                CoverStatus = L.T("fetching game info…");
                await Task.Run(() => _services.Metadata.FillMissingAsync(games));
            }
            CoverStatus = coverSummary;
        }
        catch (Exception ex)
        {
            CoverStatus = L.F("covers unavailable: {0}", ex.Message);
        }
        finally
        {
            IsFetchingCovers = false;
        }

        if (_coversRequestedAgain)
            await FetchCoversAsync();
    }

    /// <summary>"2 duplicate copies skipped (Black.iso is already in Downloads\ps2)…" — the files are not deleted.</summary>
    private static string DescribeDuplicates(IReadOnlyList<DuplicateRom> duplicates)
    {
        var examples = duplicates.Take(3).Select(d =>
            L.F("{0} in {1}/ = {2}/", Path.GetFileName(d.Path), Path.GetFileName(Path.GetDirectoryName(d.Path)), Path.GetFileName(Path.GetDirectoryName(d.KeptPath))));
        return L.F("{0} duplicate copy(ies) of the same game were left out of the library ({1}). The files stay on disk — delete the extra copy if you want to free up space.",
            duplicates.Count, string.Join("; ", examples));
    }

    [RelayCommand]
    private void GoToFolders() => _navigate("folders");

    [RelayCommand]
    private void DismissNotice() => Notice = null;

    /// <summary>Opens at the most recent save the emulator can load by itself; without a save, opens normally.</summary>
    internal Task ContinueAsync(GameItemViewModel item, CancellationToken ct = default)
    {
        var latest = _services.Saves.Find(item.Game).FirstOrDefault(s => s.CanLoadDirectly);
        return PlayAsync(item, ct, latest);
    }

    internal async Task PlayAsync(GameItemViewModel item, CancellationToken ct = default, RetroHub.Core.Saves.SaveState? state = null)
    {
        if (item.IsRunning)
            return;

        Notice = null;
        item.IsRunning = true;
        try
        {
            await _services.Launcher.LaunchAsync(item.Game, ct, state);
            if (_services.Database.GetGame(item.Game.Id) is { } fresh)
                item.Update(fresh);
        }
        catch (LaunchException ex)
        {
            Notice = Notice.Error(ex.Message);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Notice = Notice.Error(L.F("Could not open the emulator: {0}", ex.Message));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notice = Notice.Error(L.F("Could not open the emulator: {0}", ex.Message));
        }
        finally
        {
            item.IsRunning = false;
        }
    }

    internal void ToggleFavorite(GameItemViewModel item)
    {
        item.Favorite = !item.Favorite;
        _services.Database.SetFavorite(item.Game.Id, item.Favorite);
        item.Game.Favorite = item.Favorite;
        if (FavoritesOnly)
            RefreshView();
        RefreshFavorites();
        RebuildSidebar();
    }
}
