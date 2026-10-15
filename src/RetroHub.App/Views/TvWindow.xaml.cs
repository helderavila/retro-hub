using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RetroHub.App.Helpers;
using RetroHub.App.Input;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Views;

/// <summary>TV mode window: full screen, no mouse, driven by controller (or keyboard).</summary>
public partial class TvWindow : Window
{
    private readonly TvViewModel _vm;
    private readonly GamepadNavigator _pad = new();

    public TvWindow(TvViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        Grain.Fill = NoiseBrush.Create((int)FindResource("Grain.MaxAlpha"));

        _pad.Pressed += _vm.Handle;
        _pad.ExitComboHeld += _vm.ExitGame;
        _vm.CloseRequested += Close;
        _vm.GameEnded += BringToFront;
        _vm.ScrollRequested += delta => PageScroll.ScrollToVerticalOffset(PageScroll.VerticalOffset + delta * 160);

        Loaded += (_, _) => { _pad.Start(); ScrollToSelected(); BringToFront(); };

        // Preview: waits 0.8 s on a game before playing (does not play while you scroll quickly through games).
        _videoDelay = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _videoDelay.Tick += (_, _) => { _videoDelay.Stop(); StartPreview(); };
        Video.MediaEnded += (_, _) => { Video.Position = TimeSpan.Zero; Video.Play(); };
        Video.MediaOpened += (_, _) => Fade(Video, 0.55);
        Video.MediaFailed += (_, _) => StopPreview();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TvViewModel.PreviewVideo)) { StopPreview(); _videoDelay.Stop(); _videoDelay.Start(); }
            if (e.PropertyName == nameof(TvViewModel.IsPlaying) && _vm.IsPlaying) StopPreview(); // game starting: silence
            if (e.PropertyName == nameof(TvViewModel.Page))
            {
                PageScroll.ScrollToTop(); // another game: start from the top
                // The video preview is hidden behind the page: no sound while it is open.
                _videoDelay.Stop();
                if (_vm.Page is not null) StopPreview(); else _videoDelay.Start();
            }
        };
        Closed += (_, _) => { _videoDelay.Stop(); Video.Close(); };
        // When returning to the window (e.g. Alt+Tab), cover the taskbar again.
        Activated += (_, _) => Topmost = !_vm.IsPlaying;
        Deactivated += (_, _) => Topmost = false;
        // Game starting: drop topmost so the emulator shows in front.
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TvViewModel.IsPlaying) && _vm.IsPlaying)
                Topmost = false;
        };
        Closed += (_, _) => { _pad.Dispose(); _vm.Stop(); };
    }

    /// <summary>The keyboard navigates too: arrows, Enter, Esc/Backspace, F (favorite), C (continue), PgUp/PgDn or Q/E (system).</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        PadButton? button = e.Key switch
        {
            Key.Left => PadButton.Left,
            Key.Right => PadButton.Right,
            Key.Up => PadButton.Up,
            Key.Down => PadButton.Down,
            Key.Enter or Key.Space => PadButton.A,
            Key.Escape or Key.Back => PadButton.B,
            Key.F => PadButton.Y,
            Key.C => PadButton.X,
            Key.PageUp or Key.Q => PadButton.LB,
            Key.PageDown or Key.E => PadButton.RB,
            _ => null,
        };

        if (button is { } b)
        {
            _vm.Handle(b);
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    private readonly System.Windows.Threading.DispatcherTimer _videoDelay;

    private void StartPreview()
    {
        if (_vm.PreviewVideo is not { } path || _vm.IsPlaying || _vm.Page is not null) return;
        Video.Source = new Uri(path);
        Video.Play();
    }

    private void StopPreview()
    {
        Video.Stop();
        Video.Source = null;
        Video.BeginAnimation(OpacityProperty, null); // releases the fade animation value
        Video.Opacity = 0;
    }

    private static void Fade(UIElement element, double to) =>
        element.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(to, TimeSpan.FromMilliseconds(400)));

    private void Shelf_SelectionChanged(object sender, SelectionChangedEventArgs e) => ScrollToSelected();

    private void ScrollToSelected()
    {
        if (Shelf.SelectedItem is { } item)
            Shelf.ScrollIntoView(item);
    }

    private void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Maximized;
        // Windows only gives focus to whoever is already in the foreground; Topmost + Activate works around it
        // and covers the taskbar (borderless maximized window).
        Topmost = true;
        Activate();
        Focus();
    }
}
