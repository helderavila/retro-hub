using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using RetroHub.Mac.Input;
using RetroHub.Core;
using RetroHub.Core.Localization;

namespace RetroHub.Mac.ViewModels;

public sealed partial class TvTab(string? systemId, string label, int count) : ObservableObject
{
    public string? SystemId { get; } = systemId;
    public string Label { get; } = label;
    public int Count { get; } = count;
    [ObservableProperty] private bool _isActive;
}

/// <summary>TV mode: a shelf of covers navigated with the controller only.</summary>
public partial class TvViewModel : ObservableObject
{
    private readonly LibraryViewModel _library;
    private readonly HubServices _services;
    private readonly DispatcherTimer _clockTimer;
    private CancellationTokenSource? _gameCts;

    public ObservableCollection<TvTab> Tabs { get; } = [];
    public ObservableCollection<GameItemViewModel> Games { get; } = [];

    [ObservableProperty] private int _tabIndex;
    [ObservableProperty] private int _selectedIndex = -1;
    [ObservableProperty] private GameItemViewModel? _selected;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private string _clock = DateTime.Now.ToString("HH:mm");

    public bool HasGames => Games.Count > 0;

    /// <summary>Request to close TV mode (B on the shelf).</summary>
    public event Action? CloseRequested;

    /// <summary>The game ended: the window comes back to the front.</summary>
    public event Action? GameEnded;

    public TvViewModel(LibraryViewModel library, HubServices services)
    {
        _library = library;
        _services = services;

        Tabs.Add(new TvTab(null, L.T("All"), library.AllGames.Count));
        if (library.AllGames.Any(g => g.Favorite))
            Tabs.Add(new TvTab("*", L.T("Favorites"), library.AllGames.Count(g => g.Favorite)));
        foreach (var group in library.AllGames.GroupBy(g => g.SystemId).OrderBy(g => g.First().SystemShort))
            Tabs.Add(new TvTab(group.Key, group.First().SystemShort, group.Count()));
        // Collections with games become tabs too ("Completed", "Want to play"…).
        foreach (var collection in services.Database.GetCollections().Where(c => c.Count > 0))
            Tabs.Add(new TvTab($"c:{collection.Id}", L.T(collection.Name), collection.Count));

        ApplyTab(0);
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _clockTimer.Tick += (_, _) => Clock = DateTime.Now.ToString("HH:mm");
        _clockTimer.Start();
    }

    public void Handle(PadButton button)
    {
        if (IsPlaying)
            return; // the controller belongs to the emulator now; only Back + Start (ExitGame) counts

        Message = null;
        if (Page is not null)
        {
            HandleOnPage(button);
            return;
        }
        switch (button)
        {
            case PadButton.Left: Move(-1); break;
            case PadButton.Right: Move(+1); break;
            case PadButton.LB or PadButton.Up: ApplyTab(TabIndex - 1); break;
            case PadButton.RB: ApplyTab(TabIndex + 1); break;
            case PadButton.Down: OpenPage(); break;
            case PadButton.A or PadButton.Start: _ = PlayAsync(resume: false); break;
            case PadButton.X: _ = PlayAsync(resume: true); break;
            case PadButton.Y: ToggleFavorite(); break;
            case PadButton.B: CloseRequested?.Invoke(); break;
        }
    }

    // ── Game page (↓ on the shelf) ──────────────────────────────────────────

    /// <summary>Page of the selected game open over the shelf; null = closed.</summary>
    [ObservableProperty] private GameDetailsViewModel? _page;

    /// <summary>Scroll the page text: −1 up, +1 down.</summary>
    public event Action<int>? ScrollRequested;

    private void OpenPage()
    {
        if (Selected is not { } game)
            return;
        Page = new GameDetailsViewModel(game, _services, _library);
        Page.LoadPage();
    }

    private void HandleOnPage(PadButton button)
    {
        switch (button)
        {
            case PadButton.Left or PadButton.LB: Move(-1); OpenPage(); break;
            case PadButton.Right or PadButton.RB: Move(+1); OpenPage(); break;
            case PadButton.Up: ScrollRequested?.Invoke(-1); break;
            case PadButton.Down: ScrollRequested?.Invoke(+1); break;
            case PadButton.A or PadButton.Start: _ = PlayAsync(resume: false); break;
            case PadButton.X: _ = PlayAsync(resume: true); break;
            case PadButton.Y: ToggleFavorite(); break;
            case PadButton.B: Page = null; break;
        }
    }

    public void ExitGame() => _gameCts?.Cancel();

    public void Stop()
    {
        _clockTimer.Stop();
        _gameCts?.Cancel();
    }

    private void Move(int delta)
    {
        if (Games.Count == 0)
            return;
        SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, Games.Count - 1);
    }

    partial void OnSelectedIndexChanged(int value) =>
        Selected = value >= 0 && value < Games.Count ? Games[value] : null;

    /// <summary>Preview video of the selected game (local file), or null.</summary>
    [ObservableProperty] private string? _previewVideo;

    partial void OnSelectedChanged(GameItemViewModel? value) =>
        PreviewVideo = value is null ? null : Core.Scanning.CoverResolver.FindVideo(value.Game, _services.Paths);

    private void ApplyTab(int index)
    {
        if (Tabs.Count == 0)
            return;
        index = (index + Tabs.Count) % Tabs.Count; // wraps around at the ends
        TabIndex = index;
        for (var i = 0; i < Tabs.Count; i++)
            Tabs[i].IsActive = i == index;

        var tab = Tabs[index];
        var games = _library.AllGames
            .Where(g => tab.SystemId switch
            {
                null => true,
                "*" => g.Favorite,
                var c when c.StartsWith("c:") => _library.CollectionMembers.TryGetValue(long.Parse(c[2..]), out var m) && m.Contains(g.Game.Id),
                var id => g.SystemId == id,
            })
            // Recently played first, then alphabetical.
            .OrderByDescending(g => g.Game.LastPlayed ?? DateTimeOffset.MinValue)
            .ThenBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Games.Clear();
        foreach (var g in games)
            Games.Add(g);
        OnPropertyChanged(nameof(HasGames));
        SelectedIndex = Games.Count > 0 ? 0 : -1;
        OnSelectedIndexChanged(SelectedIndex);
    }

    private void ToggleFavorite()
    {
        if (Selected is { } game)
            game.ToggleFavoriteCommand.Execute(null);
    }

    private async Task PlayAsync(bool resume)
    {
        if (Selected is not { } game || IsPlaying)
            return;

        IsPlaying = true;
        _gameCts = new CancellationTokenSource();
        try
        {
            if (resume)
                await _library.ContinueAsync(game, _gameCts.Token, curtain: false);
            else
                await _library.PlayAsync(game, _gameCts.Token, curtain: false);
            if (_library.Notice is { Kind: "error" } error)
                Message = error.Message;
        }
        finally
        {
            IsPlaying = false;
            _gameCts.Dispose();
            _gameCts = null;
            if (Page is not null)
                OpenPage(); // refreshed playtime and saves
            GameEnded?.Invoke();
        }
    }
}
