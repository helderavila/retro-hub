using System.Net.Http;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.App.Helpers;
using RetroHub.App.Views;
using RetroHub.Core;
using RetroHub.Core.Metadata;
using RetroHub.Core.Saves;

namespace RetroHub.App.ViewModels;

/// <summary>Um save state na lista do painel (com miniatura quando o emulador grava uma).</summary>
public sealed partial class SaveStateItem(SaveState state, Func<SaveState, Task> load) : ObservableObject
{
    private System.Windows.Media.ImageSource? _thumbnail;
    private bool _thumbnailLoaded;

    public SaveState State { get; } = state;
    public string Title => $"{State.Label} · {State.EmulatorName}";
    public string When => State.Modified.ToString("dd/MM/yyyy HH:mm");
    public bool CanLoad => State.CanLoadDirectly;
    public string LoadTooltip => CanLoad ? "Abrir o jogo já neste ponto" : "Este emulador não abre direto num save: o jogo abre e você carrega pelo menu dele";

    public System.Windows.Media.ImageSource? Thumbnail
    {
        get
        {
            if (!_thumbnailLoaded)
            {
                _thumbnailLoaded = true;
                if (SaveStateService.Thumbnail(State) is { } bytes)
                {
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = new MemoryStream(bytes);
                    bitmap.DecodePixelWidth = 160;
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    _thumbnail = bitmap;
                }
            }
            return _thumbnail;
        }
    }

    [RelayCommand]
    private Task Load() => load(State);
}

/// <summary>Chip de coleção no painel: ligado = o jogo está na coleção.</summary>
public sealed partial class CollectionToggle(long id, string name, bool isMember, Action<long, bool> changed) : ObservableObject
{
    public long Id { get; } = id;
    public string Name { get; } = name;
    [ObservableProperty] private bool _isMember = isMember;
    partial void OnIsMemberChanged(bool value) => changed(Id, value);
}

/// <param name="Id">null = o emulador padrão do sistema.</param>
public sealed record EmulatorChoice(string? Id, string Label, bool Installed);

/// <summary>Linha da ficha técnica ("Gênero · Ação").</summary>
public sealed record InfoRow(string Label, string Value);

/// <summary>Uma sessão no histórico da página do jogo.</summary>
public sealed record SessionRow(string When, string Duration);

/// <summary>Painel lateral com tudo de um jogo: informações, arquivo, verificação, emulador, capa, atalho.</summary>
public partial class GameDetailsViewModel : ObservableObject
{
    private readonly HubServices _services;
    private readonly LibraryViewModel _owner;
    private CancellationTokenSource? _verifyCts;

    public GameItemViewModel Item { get; }
    public ObservableCollection<EmulatorChoice> EmulatorChoices { get; } = [];
    public ObservableCollection<SaveStateItem> Saves { get; } = [];
    public ObservableCollection<CollectionToggle> Collections { get; } = [];
    [ObservableProperty] private string _newCollectionName = "";
    public bool HasSaves => Saves.Count > 0;
    public bool CanContinue => Saves.Any(s => s.CanLoad);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InfoLine), nameof(PeopleLine), nameof(Description), nameof(HasDescription),
        nameof(OfficialName), nameof(VerifyKind), nameof(VerifyText), nameof(HasInfo), nameof(Facts))]
    private GameMetadata? _meta;

    [ObservableProperty] private EmulatorChoice? _selectedEmulator;
    [ObservableProperty] private bool _isVerifying;
    [ObservableProperty] private double _verifyProgress;
    [ObservableProperty] private bool _isLoadingInfo;
    [ObservableProperty] private Notice? _notice;

    public GameDetailsViewModel(GameItemViewModel item, HubServices services, LibraryViewModel owner)
    {
        Item = item;
        _services = services;
        _owner = owner;
        _meta = GameMetadata.FromJson(item.Game.MetadataJson);

        var system = services.Manifests.GetSystem(item.SystemId);
        var installed = services.Database.GetInstalledEmulators().Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var defaultName = system?.DefaultEmulator is { } d ? services.Manifests.GetEmulator(d)?.Name ?? d : "—";
        EmulatorChoices.Add(new EmulatorChoice(null, $"Padrão ({defaultName})", true));
        foreach (var emu in services.Manifests.EmulatorsForSystem(item.SystemId))
            EmulatorChoices.Add(new EmulatorChoice(emu.Id, installed.Contains(emu.Id) ? emu.Name : $"{emu.Name} · não instalado", installed.Contains(emu.Id)));
        _selectedEmulator = EmulatorChoices.FirstOrDefault(c => c.Id == item.Game.EmulatorOverride) ?? EmulatorChoices[0];

        if (_meta is not { ManualInfo: true } and not { Name: not null })
            _ = LoadInfoAsync();
        RefreshSaves();
        RefreshCollections();
    }

    private void RefreshCollections()
    {
        Collections.Clear();
        var members = _services.Database.GetCollectionMembers();
        foreach (var c in _services.Database.GetCollections())
            Collections.Add(new CollectionToggle(c.Id, c.Name,
                members.TryGetValue(c.Id, out var set) && set.Contains(Item.Game.Id), OnCollectionToggled));
    }

    private void OnCollectionToggled(long collectionId, bool member)
    {
        _services.Database.SetInCollection(collectionId, Item.Game.Id, member);
        _owner.RebuildFilters(_owner.SelectedSystem?.Id, _owner.SelectedSystem?.CollectionId);
    }

    [RelayCommand]
    private void CreateCollection()
    {
        if (string.IsNullOrWhiteSpace(NewCollectionName)) return;
        var id = _services.Database.CreateCollection(NewCollectionName);
        _services.Database.SetInCollection(id, Item.Game.Id, true);
        NewCollectionName = "";
        RefreshCollections();
        _owner.RebuildFilters(_owner.SelectedSystem?.Id, _owner.SelectedSystem?.CollectionId);
    }

    public string Title => Item.Title;
    public string SystemName => Item.SystemName;
    public string FilePath => Item.FilePath;
    public string? Serial => Item.Game.Serial ?? Meta?.Serial;
    public string SerialText => string.IsNullOrWhiteSpace(Serial) ? "" : $" · serial {Serial}";
    public string SizeText => Item.Game.SizeBytes >= 1L << 30
        ? $"{Item.Game.SizeBytes / (double)(1L << 30):0.00} GB"
        : $"{Item.Game.SizeBytes / (double)(1L << 20):0.0} MB";
    public string PlaytimeText => Item.PlaytimeText;
    public string LastPlayedText => Item.Game.LastPlayed is { } t ? $"última vez em {t.ToLocalTime():dd/MM/yyyy}" : "nunca jogado";

    public bool HasInfo => Meta?.HasInfo == true;
    public string? OfficialName => Meta?.Name;
    public string? Description => Meta?.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Meta?.Description);

    /// <summary>"1995 · RPG · 1 jogador"</summary>
    public string InfoLine => string.Join("  ·  ", new[]
    {
        Meta?.Year, Meta?.Genre,
        Meta?.Players is { } p ? (p == "1" ? "1 jogador" : $"até {p} jogadores") : null,
    }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string PeopleLine => (Meta?.Developer, Meta?.Publisher) switch
    {
        ({ } dev, { } pub) when dev != pub => $"{dev} · {pub}",
        ({ } dev, _) => dev,
        (_, { } pub) => pub,
        _ => "",
    };

    public string VerifyKind => Meta?.VerifyStatus switch { "ok" => "ok", "unknown" => "warn", _ => "idle" };

    public string VerifyText => Meta?.VerifyStatus switch
    {
        "ok" => $"Verificado · {Meta.VerifyDetail?.Replace("Bate com o dump oficial: ", "")}",
        "unknown" => Meta.VerifyDetail ?? "Não bate com a lista oficial",
        "unsupported" => Meta.VerifyDetail ?? "Verificação indisponível",
        _ => "Ainda não verificado",
    };

    private async Task LoadInfoAsync()
    {
        IsLoadingInfo = true;
        try
        {
            if (await _services.Metadata.LookupAsync(Item.Game) is { } info)
            {
                Meta = (Meta ?? new GameMetadata()) with
                {
                    Name = info.Name, Description = info.Description, Developer = info.Developer, Publisher = info.Publisher,
                    Genre = info.Genre, Year = info.Year, Region = info.Region, Players = info.Players,
                };
                Item.Game.MetadataJson = Meta.ToJson();
                _services.Database.SetMetadata(Item.Game.Id, Item.Game.MetadataJson);
            }
        }
        catch (HttpRequestException) { /* sem internet: painel mostra o que tem */ }
        finally
        {
            IsLoadingInfo = false;
        }
    }

    partial void OnSelectedEmulatorChanged(EmulatorChoice? value)
    {
        if (value is null) return;
        Item.Game.EmulatorOverride = value.Id;
        _services.Database.SetEmulatorOverride(Item.Game.Id, value.Id);
        if (!value.Installed)
            Notice = Notice.Warn($"{value.Label.Split(" · ")[0]} não está instalado — instale em Emuladores para jogar com ele.");
        else
            Notice = null;
    }

    private void RefreshSaves()
    {
        Saves.Clear();
        try
        {
            foreach (var state in _services.Saves.Find(Item.Game).Take(12))
                Saves.Add(new SaveStateItem(state, LoadStateAsync));
        }
        catch (IOException) { }
        OnPropertyChanged(nameof(HasSaves));
        OnPropertyChanged(nameof(CanContinue));
    }

    private async Task LoadStateAsync(SaveState state)
    {
        await _owner.PlayAsync(Item, default, state.CanLoadDirectly ? state : null);
        RefreshSaves();
    }

    [RelayCommand]
    private async Task Play()
    {
        await _owner.PlayAsync(Item);
        RefreshSaves();
        RefreshHistory();
    }

    [RelayCommand]
    private async Task Continue()
    {
        await _owner.ContinueAsync(Item);
        RefreshSaves();
        RefreshHistory();
    }

    // ── Página do jogo ──────────────────────────────────────────────────────

    public ObservableCollection<SessionRow> RecentSessions { get; } = [];
    /// <summary>Tempo jogado por mês nos últimos 12 meses.</summary>
    public ObservableCollection<StatBar> Months { get; } = [];
    [ObservableProperty] private string _sessionCount = "0";
    [ObservableProperty] private string _averageText = "—";
    [ObservableProperty] private string _firstPlayedText = "";
    [ObservableProperty] private bool _hasSessions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeroImage), nameof(HasArt))]
    private System.Windows.Media.ImageSource? _titleImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeroImage), nameof(HasArt))]
    private System.Windows.Media.ImageSource? _snapImage;

    /// <summary>Fundo do topo da página: o jogo rodando, a tela de título ou a capa.</summary>
    public System.Windows.Media.ImageSource? HeroImage => SnapImage ?? TitleImage ?? Item.CoverImage;
    public bool HasArt => SnapImage is not null || TitleImage is not null;
    public string AddedText => $"na biblioteca desde {Item.Game.AddedAt.ToLocalTime():dd/MM/yyyy}";

    /// <summary>Ficha técnica: só as linhas que têm valor.</summary>
    public IReadOnlyList<InfoRow> Facts => new (string Label, string? Value)[]
    {
        ("Ano", Meta?.Year), ("Gênero", Meta?.Genre), ("Desenvolvedora", Meta?.Developer), ("Distribuidora", Meta?.Publisher),
        ("Jogadores", Meta?.Players), ("Região", Meta?.Region), ("Sistema", SystemName), ("Serial", Serial), ("Nome oficial", Meta?.Name),
    }.Where(r => !string.IsNullOrWhiteSpace(r.Value)).Select(r => new InfoRow(r.Label, r.Value!)).ToList();

    /// <summary>Chamado ao abrir a página: histórico e imagens (o painel lateral não precisa delas).</summary>
    public void LoadPage()
    {
        RefreshHistory();
        _ = LoadArtAsync();
    }

    private void RefreshHistory()
    {
        var sessions = _services.Database.GetSessions(DateTimeOffset.UnixEpoch).Where(s => s.GameId == Item.Game.Id).ToList();
        HasSessions = sessions.Count > 0;
        SessionCount = sessions.Count.ToString();
        AverageText = sessions.Count == 0 ? "—" : Shell.FormatPlaytime(sessions.Sum(s => s.Seconds) / sessions.Count);
        FirstPlayedText = sessions.Count == 0 ? "" : $" · jogado pela primeira vez em {sessions[0].StartedAt.ToLocalTime():dd/MM/yyyy}";

        RecentSessions.Clear();
        foreach (var s in sessions.AsEnumerable().Reverse().Take(8))
            RecentSessions.Add(new SessionRow(s.StartedAt.ToLocalTime().ToString("dd/MM/yyyy · HH:mm"), Shell.FormatPlaytime(s.Seconds)));

        var culture = new System.Globalization.CultureInfo("pt-BR");
        var thisMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        var perMonth = sessions.GroupBy(s => { var d = s.StartedAt.ToLocalTime(); return new DateTime(d.Year, d.Month, 1); })
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Seconds));
        var months = Enumerable.Range(0, 12).Select(i => thisMonth.AddMonths(i - 11)).ToList();
        var max = months.Max(m => perMonth.GetValueOrDefault(m));
        Months.Clear();
        foreach (var month in months)
        {
            var seconds = perMonth.GetValueOrDefault(month);
            var text = seconds == 0 ? "—" : Shell.FormatPlaytime(seconds);
            Months.Add(new StatBar(month.ToString("MMM", culture).TrimEnd('.'), text, max == 0 ? 0 : (double)seconds / max,
                $"{month.ToString("MMMM 'de' yyyy", culture)} · {text}"));
        }

        OnPropertyChanged(nameof(PlaytimeText));
        OnPropertyChanged(nameof(LastPlayedText));
        OnPropertyChanged(nameof(LastPlayedTile));
    }

    public string LastPlayedTile => Item.LastPlayedText is { Length: > 0 } t ? t : "nunca";

    private async Task LoadArtAsync()
    {
        foreach (var (folder, set) in new (string, Action<System.Windows.Media.ImageSource>)[]
                 {
                     (Core.Covers.CoverFetcher.Snapshots, img => SnapImage = img),
                     (Core.Covers.CoverFetcher.TitleScreens, img => TitleImage = img),
                 })
        {
            try
            {
                if (await _services.Covers.FetchArtAsync(Item.Game, folder) is { } path && LoadImage(path) is { } image)
                    set(image);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException) { /* sem imagem: a página usa a capa */ }
        }
    }

    private static System.Windows.Media.ImageSource? LoadImage(string path)
    {
        try
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 960;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception) { return null; }
    }

    // Edição das informações (o que a base aberta não tem ou tem errado).
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editYear = "";
    [ObservableProperty] private string _editGenre = "";
    [ObservableProperty] private string _editDeveloper = "";
    [ObservableProperty] private string _editPublisher = "";
    [ObservableProperty] private string _editPlayers = "";
    [ObservableProperty] private string _editDescription = "";

    [RelayCommand]
    private void EditInfo()
    {
        EditYear = Meta?.Year ?? "";
        EditGenre = Meta?.Genre ?? "";
        EditDeveloper = Meta?.Developer ?? "";
        EditPublisher = Meta?.Publisher ?? "";
        EditPlayers = Meta?.Players ?? "";
        EditDescription = Meta?.Description ?? "";
        IsEditing = true;
    }

    [RelayCommand]
    private void SaveInfo()
    {
        static string? Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        Meta = (Meta ?? new GameMetadata()) with
        {
            Year = Clean(EditYear), Genre = Clean(EditGenre), Developer = Clean(EditDeveloper), Publisher = Clean(EditPublisher),
            Players = Clean(EditPlayers), Description = Clean(EditDescription), ManualInfo = true,
        };
        Item.Game.MetadataJson = Meta.ToJson();
        _services.Database.SetMetadata(Item.Game.Id, Item.Game.MetadataJson);
        IsEditing = false;
        Notice = Notice.Ok("Informações salvas. A busca automática não muda mais este jogo.");
    }

    [RelayCommand] private void CancelEdit() => IsEditing = false;

    /// <summary>Abre uma busca sobre o jogo no navegador (só o nome do jogo vai na busca).</summary>
    [RelayCommand]
    private void OpenWeb(string site)
    {
        var query = Uri.EscapeDataString(site switch
        {
            "youtube" => $"{Title} {SystemName} gameplay",
            "guide" => $"{Title} {SystemName} detonado guia",
            _ => Title,
        });
        var url = site switch
        {
            "youtube" => $"https://www.youtube.com/results?search_query={query}",
            "wikipedia" => $"https://pt.wikipedia.org/w/index.php?search={query}",
            _ => $"https://www.google.com/search?q={query}",
        };
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Notice = Notice.Error($"Não foi possível abrir o navegador: {ex.Message}"); }
    }

    [RelayCommand] private void OpenPage() => _owner.OpenPage(Item);

    [RelayCommand]
    private async Task VerifyAsync()
    {
        if (IsVerifying)
        {
            _verifyCts?.Cancel();
            return;
        }

        IsVerifying = true;
        VerifyProgress = 0;
        _verifyCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(p => VerifyProgress = p);
            Meta = await _services.Metadata.VerifyAsync(Item.Game, progress, _verifyCts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            Notice = Notice.Error($"Não foi possível verificar: {ex.Message}");
        }
        finally
        {
            IsVerifying = false;
            _verifyCts.Dispose();
            _verifyCts = null;
        }
    }

    [RelayCommand]
    private async Task ChangeCoverAsync()
    {
        var query = Meta?.Name is { } official ? Core.Scanning.TitleCleaner.Clean(official + ".x") : Item.Title;
        var choice = CoverPickerDialog.Show(_services.Covers, Item.SystemId, query);
        if (choice is null) return;

        try
        {
            switch (choice.Kind)
            {
                case CoverChoiceKind.Catalog when await _services.Covers.DownloadAsync(Item.Game, choice.Value!) is { } path:
                    Item.SetCover(path);
                    break;
                case CoverChoiceKind.File:
                    Item.SetCover(_services.Covers.SetCustomCover(Item.Game, choice.Value!));
                    break;
                case CoverChoiceKind.None:
                    _services.Covers.RemoveCover(Item.Game);
                    Item.ClearCover();
                    break;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            Notice = Notice.Error($"Não foi possível trocar a capa: {ex.Message}");
        }
    }

    [RelayCommand]
    private void CreateShortcut()
    {
        try
        {
            var path = Shortcuts.CreateForGame(Item.Game.Id, Item.Title, null);
            Notice = Notice.Ok($"Atalho criado na área de trabalho: {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            Notice = Notice.Error($"Não foi possível criar o atalho: {ex.Message}");
        }
    }

    [RelayCommand] private void OpenFolder() => _owner.OpenFileFolder(Item);
    [RelayCommand] private void Remove() => _owner.RemoveFromLibrary(Item);
    [RelayCommand] private void Delete() => _owner.DeleteFiles(Item);
    [RelayCommand] private void ToggleFavorite() => _owner.ToggleFavorite(Item);
    [RelayCommand] private void Close() => _owner.CloseDetails();
    [RelayCommand] private void DismissNotice() => Notice = null;
}
