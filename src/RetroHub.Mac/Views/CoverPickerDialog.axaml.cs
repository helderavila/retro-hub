using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RetroHub.Core.Covers;
using RetroHub.Core.Localization;
using RetroHub.Mac.Helpers;

namespace RetroHub.Mac.Views;

public enum CoverChoiceKind { Catalog, File, None }

public sealed record CoverChoice(CoverChoiceKind Kind, string? Value);

/// <summary>Pick the cover: search the libretro archive (with preview), use an image from the Mac or have no cover.</summary>
public partial class CoverPickerDialog : Window
{
    private static readonly HttpClient PreviewHttp = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly CoverFetcher _covers = null!;
    private readonly string _systemId = "";
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private CancellationTokenSource? _search;
    private CancellationTokenSource? _preview;

    public CoverChoice? Choice { get; private set; }

    public CoverPickerDialog() => InitializeComponent();

    private CoverPickerDialog(CoverFetcher covers, string systemId, string initialQuery) : this()
    {
        _covers = covers;
        _systemId = systemId;
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await SearchAsync(); };
        Query.Text = initialQuery;
        Query.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        Opened += (_, _) => { Query.Focus(); Query.SelectAll(); _debounce.Start(); };
    }

    public static async Task<CoverChoice?> ShowAsync(CoverFetcher covers, string systemId, string title)
    {
        if (Dialogs.Owner is not { } owner)
            return null;
        var dialog = new CoverPickerDialog(covers, systemId, title);
        return await dialog.ShowDialog<bool>(owner) ? dialog.Choice : null;
    }

    private async Task SearchAsync()
    {
        _search?.Cancel();
        _search = new CancellationTokenSource();
        var token = _search.Token;
        Status.Text = L.T("searching…");
        try
        {
            var results = await _covers.SearchAsync(_systemId, Query.Text ?? "", token);
            if (token.IsCancellationRequested) return;
            Results.ItemsSource = results;
            Status.Text = results.Count == 0 ? L.T("No covers with that name. Try fewer words.") : "";
            if (results.Count > 0) Results.SelectedIndex = 0;
        }
        catch (HttpRequestException)
        {
            Status.Text = L.T("Cannot reach the cover archive.");
        }
        catch (OperationCanceledException) { }
    }

    private async void Results_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UseButton.IsEnabled = Results.SelectedItem is string;
        Preview.Source = null;
        _preview?.Cancel();
        if (Results.SelectedItem is not string name || _covers.ThumbnailUrl(_systemId, name) is not { } url)
            return;

        PreviewHint.Text = L.T("loading…");
        _preview = new CancellationTokenSource();
        var token = _preview.Token;
        try
        {
            var bytes = await PreviewHttp.GetByteArrayAsync(url, token);
            using var stream = new MemoryStream(bytes);
            var bitmap = Bitmap.DecodeToWidth(stream, 460);
            if (token.IsCancellationRequested) return;
            Preview.Source = bitmap;
            PreviewHint.Text = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!token.IsCancellationRequested)
                PreviewHint.Text = L.T("no preview");
        }
    }

    private void Results_DoubleTapped(object? sender, TappedEventArgs e) => Use_Click(sender, e);

    private void Use_Click(object? sender, RoutedEventArgs e)
    {
        if (Results.SelectedItem is not string name) return;
        Choice = new CoverChoice(CoverChoiceKind.Catalog, name);
        Close(true);
    }

    private async void FromFile_Click(object? sender, RoutedEventArgs e)
    {
        var file = await Dialogs.PickFileAsync(L.T("Cover image"), (L.T("Images"), ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"]));
        if (file is null) return;
        Choice = new CoverChoice(CoverChoiceKind.File, file);
        Close(true);
    }

    private void None_Click(object? sender, RoutedEventArgs e)
    {
        Choice = new CoverChoice(CoverChoiceKind.None, null);
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
