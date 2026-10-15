using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using RetroHub.Mac.ViewModels;
using RetroHub.Mac.Views;

namespace RetroHub.Mac;

public partial class MainWindow : Window
{
    private TvWindow? _tv;
    private MainViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) _vm.TvModeChanged -= OnTvModeChanged;
        _vm = DataContext as MainViewModel;
        if (_vm is not null) _vm.TvModeChanged += OnTvModeChanged;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11 && _vm is not null)
        {
            _vm.IsTvOpen = true;
            e.Handled = true;
        }
    }

    private void OnTvModeChanged(bool open)
    {
        if (_vm is null)
            return;
        if (open && _tv is null)
        {
            _tv = new TvWindow(_vm.CreateTv());
            _tv.Closed += (_, _) =>
            {
                _tv = null;
                _vm.IsTvOpen = false;
                _vm.Library.Load();
                Show();
                Activate();
            };
            _tv.Show();
            Hide();
        }
        else if (!open)
        {
            _tv?.Close();
        }
    }
}
