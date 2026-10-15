using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using RetroHub.Core.Covers;
using RetroHub.Core.Localization;

namespace RetroHub.App.Views;

public enum CoverChoiceKind { Catalog, File, None }

public sealed record CoverChoice(CoverChoiceKind Kind, string? Value);

/// <summary>Pick the cover: search the libretro archive (with preview), use an image from the PC or have no cover.</summary>
public partial class CoverPickerDialog : Window
{
    private readonly CoverFetcher _covers;
    private readonly string _systemId;
    private readonly DispatcherTimer _debounce;
    private CancellationTokenSource? _search;

    public CoverChoice? Choice { get; private set; }

    private CoverPickerDialog(CoverFetcher covers, string systemId, string initialQuery)
    {
        InitializeComponent();
        _covers = covers;
        _systemId = systemId;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await SearchAsync(); };
        Query.Text = initialQuery;
        Loaded += (_, _) => { Query.Focus(); Query.SelectAll(); };
    }

    public static CoverChoice? Show(CoverFetcher covers, string systemId, string title)
    {
        var dialog = new CoverPickerDialog(covers, systemId, title)
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow,
        };
        return dialog.ShowDialog() == true ? dialog.Choice : null;
    }

    private void Query_TextChanged(object sender, TextChangedEventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private async Task SearchAsync()
    {
        _search?.Cancel();
        _search = new CancellationTokenSource();
        var token = _search.Token;
        Status.Text = L.T("searching…");
        try
        {
            var results = await _covers.SearchAsync(_systemId, Query.Text, token);
            if (token.IsCancellationRequested) return;
            Results.ItemsSource = results;
            Status.Text = results.Count == 0 ? L.T("No covers with that name. Try fewer words.") : "";
            if (results.Count > 0) Results.SelectedIndex = 0;
        }
        catch (HttpRequestException)
        {
            Status.Text = L.T("Cannot reach the cover archive.");
        }
    }

    private void Results_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UseButton.IsEnabled = Results.SelectedItem is string;
        Preview.Source = null;
        PreviewHint.Text = L.T("loading…");
        if (Results.SelectedItem is string name && _covers.ThumbnailUrl(_systemId, name) is { } url)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(url);
            bitmap.DecodePixelWidth = 460;
            bitmap.EndInit();
            bitmap.DownloadCompleted += (_, _) => PreviewHint.Text = "";
            bitmap.DownloadFailed += (_, _) => PreviewHint.Text = L.T("no preview");
            Preview.Source = bitmap;
        }
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        if (Results.SelectedItem is not string name) return;
        Choice = new CoverChoice(CoverChoiceKind.Catalog, name);
        DialogResult = true;
    }

    private void FromFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = L.T("Cover image"), Filter = L.T("Images") + "|*.png;*.jpg;*.jpeg;*.webp;*.bmp" };
        if (dialog.ShowDialog(this) != true) return;
        Choice = new CoverChoice(CoverChoiceKind.File, dialog.FileName);
        DialogResult = true;
    }

    private void None_Click(object sender, RoutedEventArgs e)
    {
        Choice = new CoverChoice(CoverChoiceKind.None, null);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
