using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RetroHub.Core;
using RetroHub.Core.Covers;
using RetroHub.Core.Launch;
using RetroHub.Core.Library;
using RetroHub.Core.Scanning;
using RetroHub.App.Views;

namespace RetroHub.App.ViewModels;

/// <param name="CollectionId">Preenchido quando o chip é uma coleção em vez de um sistema.</param>
public sealed record SystemChip(string? Id, string Label, int Count, long? CollectionId = null);

/// <summary>Linha da lista lateral (como a da Steam): cabeçalho de grupo recolhível ou um jogo.</summary>
public sealed class SidebarEntry(string groupKey, string? header, int count, GameItemViewModel? item, bool collapsed = false)
{
    public string GroupKey { get; } = groupKey;
    public string? Header { get; } = header;
    public int Count { get; } = count;
    public GameItemViewModel? Item { get; } = item;
    public bool IsHeader => Item is null;
    public bool IsCollapsed { get; } = collapsed;
}

public partial class LibraryViewModel : ObservableObject, IActivatable
{
    private readonly HubServices _services;
    private readonly Action<string> _navigate;
    private readonly ObservableCollection<GameItemViewModel> _games = [];

    public ICollectionView GamesView { get; }
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

    /// <summary>"Jogados recentemente" no início da biblioteca (some durante a busca).</summary>
    public ObservableCollection<GameItemViewModel> RecentGames { get; } = [];
    public ObservableCollection<GameItemViewModel> FavoriteGames { get; } = [];

    public bool ShowRecent => RecentGames.Count > 0 && string.IsNullOrWhiteSpace(SearchText);
    public bool ShowFavorites => FavoriteGames.Count > 0 && string.IsNullOrWhiteSpace(SearchText);

    // ── Lista lateral ───────────────────────────────────────────────────────

    public ObservableCollection<SidebarEntry> Sidebar { get; } = [];
    private readonly HashSet<string> _collapsedGroups = [];
    private bool _syncingSelection;

    /// <summary>Jogo marcado na lista lateral; escolher um abre a página dele.</summary>
    [ObservableProperty] private SidebarEntry? _selectedEntry;

    partial void OnSelectedEntryChanged(SidebarEntry? value)
    {
        if (!_syncingSelection && value?.Item is { } item && Page?.Item.Game.Id != item.Game.Id)
            OpenPage(item);
    }

    /// <summary>Favoritos primeiro, depois um grupo por sistema; a busca filtra a lista também.</summary>
    internal void RebuildSidebar()
    {
        var query = SearchText.Trim();
        var visible = _games.Where(g => query.Length == 0 || g.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase).ToList();

        var groups = new List<(string Key, string Title, List<GameItemViewModel> Games)>();
        if (visible.Where(g => g.Favorite).ToList() is { Count: > 0 } favorites)
            groups.Add(("*", "FAVORITOS", favorites));
        foreach (var system in visible.GroupBy(g => g.SystemName).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            groups.Add((system.Key, system.Key.ToUpper(new System.Globalization.CultureInfo("pt-BR")), system.ToList()));

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

    // ── Ordenação de "Todos os jogos" ───────────────────────────────────────

    public IReadOnlyList<string> SortModes { get; } = ["Nome", "Jogados recentemente", "Tempo de jogo", "Adicionados recentemente"];
    [ObservableProperty] private string _sortMode = "Nome";

    partial void OnSortModeChanged(string value) => ApplySort();

    private void ApplySort()
    {
        if (GamesView is not ListCollectionView view)
            return;
        Comparison<GameItemViewModel> compare = SortMode switch
        {
            "Jogados recentemente" => (a, b) => Nullable.Compare(b.Game.LastPlayed, a.Game.LastPlayed),
            "Tempo de jogo" => (a, b) => b.Game.PlaySeconds.CompareTo(a.Game.PlaySeconds),
            "Adicionados recentemente" => (a, b) => b.Game.AddedAt.CompareTo(a.Game.AddedAt),
            _ => (_, _) => 0,
        };
        view.CustomSort = Comparer<object>.Create((x, y) =>
        {
            var (a, b) = ((GameItemViewModel)x, (GameItemViewModel)y);
            var result = compare(a, b);
            return result != 0 ? result : string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
        });
    }

    /// <summary>Todos os jogos carregados (o Modo TV filtra e ordena por conta própria).</summary>
    public IReadOnlyList<GameItemViewModel> AllGames => _games;

    public bool HasGames => _games.Count > 0;
    public int GameCount => _games.Count;

    public LibraryViewModel(HubServices services, Action<string> navigate)
    {
        _services = services;
        _navigate = navigate;
        GamesView = CollectionViewSource.GetDefaultView(_games);
        GamesView.Filter = Matches;
        ApplySort();
    }

    public void OnActivated()
    {
        Load();
        // Na primeira abertura, completa capas de jogos adicionados antes desta função existir (ou sem internet).
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
            ? "Seus jogos aparecem aqui depois do primeiro escaneamento."
            : $"{_games.Count} jogos · {SystemFilters.Count - 1} sistemas · {Shell.FormatPlaytime(totalSeconds)} jogados";

        RecentGames.Clear();
        foreach (var recent in _games.Where(g => g.Game.LastPlayed is not null).OrderByDescending(g => g.Game.LastPlayed).Take(8))
            RecentGames.Add(recent);
        OnPropertyChanged(nameof(ShowRecent));
        RefreshFavorites();

        // A página aberta continua apontando para o jogo certo depois de recarregar.
        if (Page is not null && _games.FirstOrDefault(g => g.Game.Id == Page.Item.Game.Id) is null)
            Page = null;
        RebuildSidebar();

        OnPropertyChanged(nameof(HasGames));
        OnPropertyChanged(nameof(GameCount));
        GamesView.Refresh();
    }

    private Dictionary<long, HashSet<long>> _collectionMembers = [];

    /// <summary>Chips: Todos · sistemas · coleções (só as que têm jogos).</summary>
    internal void RebuildFilters(string? selectedSystem = null, long? selectedCollection = null)
    {
        _collectionMembers = _services.Database.GetCollectionMembers();
        SystemFilters.Clear();
        SystemFilters.Add(new SystemChip(null, "Todos", _games.Count));
        foreach (var group in _games.GroupBy(g => g.SystemId).OrderBy(g => g.First().SystemShort))
            SystemFilters.Add(new SystemChip(group.Key, group.First().SystemShort, group.Count()));
        foreach (var collection in _services.Database.GetCollections().Where(c => c.Count > 0))
            SystemFilters.Add(new SystemChip(null, collection.Name, collection.Count, collection.Id));
        SelectedSystem = SystemFilters.FirstOrDefault(c => c.CollectionId is { } id ? id == selectedCollection : c.Id == selectedSystem && selectedCollection is null)
                         ?? SystemFilters[0];
    }

    public IReadOnlyDictionary<long, HashSet<long>> CollectionMembers => _collectionMembers;

    private bool Matches(object item) =>
        item is GameItemViewModel g
        && (string.IsNullOrWhiteSpace(SearchText) || g.Title.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase))
        && (!FavoritesOnly || g.Favorite)
        && (SelectedSystem?.Id is null || g.SystemId == SelectedSystem.Id)
        && (SelectedSystem?.CollectionId is not { } cid || (_collectionMembers.TryGetValue(cid, out var members) && members.Contains(g.Game.Id)));

    partial void OnSearchTextChanged(string value)
    {
        GamesView.Refresh();
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
    partial void OnFavoritesOnlyChanged(bool value) { GamesView.Refresh(); OnPropertyChanged(nameof(ShowRecent)); }
    partial void OnSelectedSystemChanged(SystemChip? value) { GamesView.Refresh(); OnPropertyChanged(nameof(ShowRecent)); }

    [RelayCommand]
    private async Task ScanAsync()
    {
        var folders = _services.Database.GetRomFolders();
        if (folders.Count == 0)
        {
            Notice = Notice.Warn("Nenhuma pasta de ROMs configurada. Adicione uma em Pastas.");
            return;
        }

        IsScanning = true;
        Notice = null;
        ScanStatus = "preparando…";
        var progress = new Progress<ScanProgress>(p =>
            ScanStatus = $"{p.FilesSeen} arquivos · {p.Identified} jogos identificados");

        try
        {
            var report = await Task.Run(() => _services.Scanner.Scan(folders, progress));
            var result = _services.Database.UpsertScanned(report.Roms);
            var removed = _services.Database.RemoveGamesByPath(report.Rejected.Select(r => r.Path));
            var gone = _services.Database.RemoveMissingGames(folders);
            Load();

            var message = $"{result.Added} jogos novos, {result.Updated} já conhecidos.";
            if (gone.Count > 0)
                message += $" {gone.Count} removido(s) porque o arquivo não existe mais.";
            var excluded = _services.Database.ExcludedCount();
            if (excluded > 0)
                message += $" {excluded} removido(s) por você continuam fora (Recomeçar biblioteca traz de volta).";
            if (result.Moved > 0)
                message += $" {result.Moved} mudaram de pasta (tempo jogado mantido).";
            if (result.Duplicates.Count > 0)
                message += " " + DescribeDuplicates(result.Duplicates);
            if (report.Rejected.Count > 0)
            {
                var names = string.Join(", ", report.Rejected.Take(3).Select(r => Path.GetFileName(r.Path)));
                message += $" {report.Rejected.Count} arquivo(s) ignorado(s) por não serem jogo — {report.Rejected[0].Reason}: {names}" +
                           (removed > 0 ? $" ({removed} removido(s) da biblioteca)." : ".");
            }

            Notice = report.Unidentified.Count == 0 && report.Rejected.Count == 0 && result.Duplicates.Count == 0
                ? Notice.Ok(message)
                : report.Unidentified.Count == 0
                    ? Notice.Warn(message)
                    : Notice.Warn($"{message} {report.Unidentified.Count} arquivos não identificados — " +
                                  "coloque-os numa pasta com o nome do sistema (ex.: ps2\\, gamecube\\).");
            _ = FetchCoversAsync();
        }
        catch (Exception ex)
        {
            Notice = Notice.Error($"Falha ao escanear: {ex.Message}");
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>Zera a biblioteca (inclusive jogos removidos manualmente) e escaneia tudo de novo.</summary>
    [RelayCommand]
    private async Task ResetLibraryAsync()
    {
        if (!ConfirmDialog.Ask(
                "Recomeçar a biblioteca?",
                "Todos os jogos saem da lista e as pastas são escaneadas do zero. Jogos que você removeu voltam a aparecer.",
                "Recomeçar e escanear",
                "Perde-se: favoritos e tempo jogado.\nFicam: pastas de ROMs, emuladores, BIOS, capas baixadas e configurações.\nNenhum arquivo do disco é apagado."))
            return;

        _services.Database.ClearLibrary();
        Load();
        await ScanAsync();
    }

    /// <summary>Página do jogo aberta à direita da lista — null = início da biblioteca.</summary>
    [ObservableProperty] private GameDetailsViewModel? _page;

    internal void OpenPage(GameItemViewModel item)
    {
        Page = new GameDetailsViewModel(item, _services, this);
        Page.LoadPage();
        _syncingSelection = true;
        SelectedEntry = Sidebar.FirstOrDefault(e => e.Item == item);
        _syncingSelection = false;
    }

    /// <summary>Volta ao início da biblioteca ("Início", Esc, e ações que tiram o jogo da lista).</summary>
    [RelayCommand]
    internal void CloseDetails()
    {
        Page = null;
        _syncingSelection = true;
        SelectedEntry = null;
        _syncingSelection = false;
    }

    internal void RemoveFromLibrary(GameItemViewModel item)
    {
        CloseDetails();
        _services.Database.ExcludeGame(item.Game.Id);
        Load();
        Notice = Notice.Ok($"\"{item.Title}\" saiu da biblioteca e não volta nos próximos escaneamentos. O arquivo continua no disco.");
    }

    /// <summary>Manda os arquivos do jogo para a Lixeira (recuperável) e tira da biblioteca.</summary>
    internal void DeleteFiles(GameItemViewModel item)
    {
        var files = SheetParser.FilesOfGame(item.FilePath);
        if (files.Count == 0)
        {
            RemoveFromLibrary(item);
            return;
        }

        var totalBytes = files.Sum(f => new FileInfo(f).Length);
        var detail = string.Join("\n", files.Take(8)) + (files.Count > 8 ? $"\n… e mais {files.Count - 8}" : "");
        if (!ConfirmDialog.Ask(
                $"Excluir \"{item.Title}\"?",
                $"{files.Count} arquivo(s), {FormatSize(totalBytes)}, vão para a Lixeira do Windows. Dá para restaurar de lá.",
                "Mover para a Lixeira",
                detail))
            return;

        var failed = new List<string>();
        foreach (var file in files)
        {
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(file,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
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
            ? Notice.Ok($"\"{item.Title}\" foi para a Lixeira ({FormatSize(totalBytes)} liberados ao esvaziá-la).")
            : Notice.Warn($"Não foi possível excluir: {string.Join(", ", failed)} (em uso ou sem permissão).");
    }

    internal void OpenFileFolder(GameItemViewModel item)
    {
        if (File.Exists(item.FilePath))
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
    }

    private static string FormatSize(long bytes) => bytes >= 1L << 30
        ? $"{bytes / (double)(1L << 30):0.0} GB"
        : $"{bytes / (double)(1L << 20):0} MB";

    [RelayCommand]
    private void AddGame()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Adicionar jogos",
            Multiselect = true,
            Filter = "ROMs e imagens de disco|*.*",
        };
        if (dialog.ShowDialog() != true)
            return;

        var identified = new List<ScannedRom>();
        var rejected = new List<string>();
        foreach (var file in dialog.FileNames)
        {
            if (_services.Scanner.Identify(file, null, out var reason) is { } rom)
                identified.Add(rom);
            else if (reason is not null)
                rejected.Add($"{Path.GetFileName(file)} ({reason})");
        }
        var failed = dialog.FileNames.Length - identified.Count - rejected.Count;
        var result = _services.Database.UpsertScanned(identified, source: "manual");
        Load();

        var message = $"{result.Added} jogo(s) adicionado(s).";
        if (result.Duplicates.Count > 0)
            message += " " + DescribeDuplicates(result.Duplicates);
        if (rejected.Count > 0)
            message += $" Não são jogos: {string.Join("; ", rejected)}.";
        if (failed > 0)
            message += $" {failed} arquivo(s) sem sistema reconhecido.";
        Notice = rejected.Count == 0 && failed == 0 && result.Duplicates.Count == 0 ? Notice.Ok(message) : Notice.Warn(message);
        _ = FetchCoversAsync();
    }

    /// <summary>Busca capas pelo nome para os jogos que ainda não têm. Cada card atualiza assim que a capa chega.</summary>
    [RelayCommand]
    private async Task FetchCoversAsync()
    {
        if (IsFetchingCovers)
        {
            _coversRequestedAgain = true; // jogos novos chegaram durante a busca: roda de novo no fim
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
                CoverStatus = $"buscando capas · 0 de {pending.Count}";
                var progress = new Progress<CoverFound>(found =>
                {
                    // Procura na lista atual: um Load() no meio da busca recria os cards.
                    _games.FirstOrDefault(g => g.Game.Id == found.GameId)?.SetCover(found.Path);
                    CoverStatus = $"buscando capas · {++done} de {pending.Count}";
                });
                var (found, missing) = await Task.Run(() =>
                    _services.Covers.FetchMissingAsync(pending.Select(p => p.Game).ToList(), progress));
                if (missing > 0)
                    coverSummary = $"{found} capas baixadas · {missing} sem capa encontrada pelo nome (botão direito › Página do jogo › Trocar capa)";
            }

            // Depois das capas, as informações (ano, gênero, sinopse…) — mesma fonte aberta, sem conta.
            var games = _games.Select(g => g.Game).Where(g => Core.Metadata.GameMetadata.FromJson(g.MetadataJson)?.Name is null).ToList();
            if (games.Count > 0)
            {
                CoverStatus = "buscando informações dos jogos…";
                await Task.Run(() => _services.Metadata.FillMissingAsync(games));
            }
            CoverStatus = coverSummary;
        }
        catch (Exception ex)
        {
            CoverStatus = $"capas indisponíveis: {ex.Message}";
        }
        finally
        {
            IsFetchingCovers = false;
        }

        if (_coversRequestedAgain)
            await FetchCoversAsync();
    }

    /// <summary>"2 cópias repetidas ignoradas (Black.iso já está em Downloads\ps2)…" — os arquivos não são apagados.</summary>
    private static string DescribeDuplicates(IReadOnlyList<DuplicateRom> duplicates)
    {
        var examples = duplicates.Take(3).Select(d =>
            $"{Path.GetFileName(d.Path)} em {Path.GetFileName(Path.GetDirectoryName(d.Path))}\\ = {Path.GetFileName(Path.GetDirectoryName(d.KeptPath))}\\");
        return $"{duplicates.Count} cópia(s) repetida(s) do mesmo jogo ficaram de fora da biblioteca ({string.Join("; ", examples)}). " +
               "Os arquivos continuam no disco — apague a cópia extra se quiser liberar espaço.";
    }

    [RelayCommand]
    private void GoToFolders() => _navigate("folders");

    [RelayCommand]
    private void DismissNotice() => Notice = null;

    /// <summary>Abre no save mais recente que o emulador consegue carregar sozinho; sem save, abre normal.</summary>
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
            Notice = Notice.Error($"Não foi possível abrir o emulador: {ex.Message}");
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
            GamesView.Refresh();
        RefreshFavorites();
        RebuildSidebar();
    }
}
