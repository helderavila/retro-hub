using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.Core;
using RetroHub.Core.Models;
using RetroHub.Core.Scanning;

namespace RetroHub.App.ViewModels;

public partial class GameItemViewModel : ObservableObject
{
    private readonly LibraryViewModel _owner;
    private string? _coverPath;
    private ImageSource? _cover;

    public Game Game { get; private set; }
    public string Title => Game.Title;
    public string SystemId => Game.SystemId;
    public string SystemShort { get; }
    public string SystemName { get; }
    public string FilePath => Game.Path;
    public string Initials { get; }
    public string PlaytimeText => Shell.FormatPlaytime(Game.PlaySeconds);

    [ObservableProperty] private bool _favorite;
    [ObservableProperty] private bool _isRunning;

    public GameItemViewModel(Game game, SystemDefinition? system, AppPaths paths, LibraryViewModel owner)
    {
        Game = game;
        _owner = owner;
        _favorite = game.Favorite;
        SystemShort = system?.ShortName ?? game.SystemId.ToUpperInvariant();
        SystemName = system?.Name ?? game.SystemId;
        _coverPath = CoverResolver.Find(game, paths);

        var words = game.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetterOrDigit(w[0]) && w.Length > 2 || w.All(char.IsDigit))
            .Take(2).ToList();
        Initials = words.Count == 0
            ? game.Title[..Math.Min(2, game.Title.Length)].ToUpperInvariant()
            : string.Concat(words.Select(w => char.ToUpperInvariant(w[0])));
    }

    public bool HasCover => _coverPath is not null;

    public ImageSource? CoverImage
    {
        get
        {
            if (_cover is null && _coverPath is not null)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(_coverPath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 352;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    _cover = bitmap;
                }
                catch (Exception)
                {
                    // capa corrompida: fica o placeholder
                }
            }
            return _cover;
        }
    }

    public void Update(Game game)
    {
        Game = game;
        OnPropertyChanged(nameof(PlaytimeText));
        OnPropertyChanged(nameof(LastPlayedText));
    }

    /// <summary>Chamado quando a capa termina de baixar — o card troca o placeholder pela imagem.</summary>
    public void SetCover(string path)
    {
        Game.CoverPath = path;
        _coverPath = path;
        _cover = null;
        OnPropertyChanged(nameof(HasCover));
        OnPropertyChanged(nameof(CoverImage));
    }

    public void ClearCover()
    {
        _coverPath = null;
        _cover = null;
        OnPropertyChanged(nameof(HasCover));
        OnPropertyChanged(nameof(CoverImage));
    }

    [RelayCommand]
    private void OpenPage() => _owner.OpenPage(this);

    [RelayCommand]
    private Task Play() => _owner.PlayAsync(this);

    [RelayCommand]
    private Task Continue() => _owner.ContinueAsync(this);

    public string LastPlayedText => Game.LastPlayed is not { } t ? "" : (DateTimeOffset.Now - t) switch
    {
        { TotalHours: < 1 } => "agora há pouco",
        { TotalDays: < 1 } d => $"há {(int)d.TotalHours} h",
        { TotalDays: < 2 } => "ontem",
        { TotalDays: < 30 } d => $"há {(int)d.TotalDays} dias",
        _ => t.ToLocalTime().ToString("dd/MM/yyyy"),
    };

    [RelayCommand]
    private void ToggleFavorite() => _owner.ToggleFavorite(this);

    [RelayCommand]
    private void RemoveFromLibrary() => _owner.RemoveFromLibrary(this);

    [RelayCommand]
    private void DeleteFiles() => _owner.DeleteFiles(this);

    [RelayCommand]
    private void OpenFileFolder() => _owner.OpenFileFolder(this);
}
