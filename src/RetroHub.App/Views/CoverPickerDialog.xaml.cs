using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using RetroHub.Core.Covers;

namespace RetroHub.App.Views;

public enum CoverChoiceKind { Catalog, File, None }

public sealed record CoverChoice(CoverChoiceKind Kind, string? Value);

/// <summary>Escolher a capa: buscar no acervo do libretro (com prévia), usar uma imagem do PC ou ficar sem capa.</summary>
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
        Status.Text = "buscando…";
        try
        {
            var results = await _covers.SearchAsync(_systemId, Query.Text, token);
            if (token.IsCancellationRequested) return;
            Results.ItemsSource = results;
            Status.Text = results.Count == 0 ? "Nenhuma capa com esse nome. Tente menos palavras." : "";
            if (results.Count > 0) Results.SelectedIndex = 0;
        }
        catch (HttpRequestException)
        {
            Status.Text = "Sem conexão com o acervo de capas.";
        }
    }

    private void Results_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UseButton.IsEnabled = Results.SelectedItem is string;
        Preview.Source = null;
        PreviewHint.Text = "carregando…";
        if (Results.SelectedItem is string name && _covers.ThumbnailUrl(_systemId, name) is { } url)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(url);
            bitmap.DecodePixelWidth = 460;
            bitmap.EndInit();
            bitmap.DownloadCompleted += (_, _) => PreviewHint.Text = "";
            bitmap.DownloadFailed += (_, _) => PreviewHint.Text = "sem prévia";
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
        var dialog = new OpenFileDialog { Title = "Imagem da capa", Filter = "Imagens|*.png;*.jpg;*.jpeg;*.webp;*.bmp" };
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
