using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RetroHub.App.Helpers;
using RetroHub.App.ViewModels;
using RetroHub.App.Views;

namespace RetroHub.App;

public partial class MainWindow : Window
{
    private TvWindow? _tv;

    public MainWindow()
    {
        InitializeComponent();
        Grain.Fill = NoiseBrush.Create((int)FindResource("Grain.MaxAlpha"));
        SourceInitialized += (_, _) => WindowEffects.ApplyDarkChrome(this,
            (Color)FindResource("Color.Bg.Subtle"),
            (Color)FindResource("Color.Surface.Raised"));

        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is MainViewModel old) old.TvModeChanged -= OnTvModeChanged;
            if (e.NewValue is MainViewModel vm) vm.TvModeChanged += OnTvModeChanged;
        };
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            Vm.IsTvOpen = true;
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    private void OnTvModeChanged(bool open)
    {
        if (open && _tv is null)
        {
            _tv = new TvWindow(Vm.CreateTv());
            _tv.Closed += (_, _) =>
            {
                _tv = null;
                Vm.IsTvOpen = false;
                Vm.Library.Load();
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
