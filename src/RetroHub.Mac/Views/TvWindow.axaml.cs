using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RetroHub.Mac.Input;
using RetroHub.Mac.ViewModels;

namespace RetroHub.Mac.Views;

/// <summary>TV mode window: full screen, no mouse, driven by controller (or keyboard).</summary>
public partial class TvWindow : Window
{
    private readonly TvViewModel _vm = null!;
    private readonly GamepadNavigator _pad = new();

    public TvWindow() => InitializeComponent();

    public TvWindow(TvViewModel vm) : this()
    {
        DataContext = _vm = vm;
        if (Helpers.Snapshot.Requested)
        {
            // Development captures: a normal window instead of taking over the screen.
            SystemDecorations = SystemDecorations.Full;
            Width = 1600;
            Height = 900;
        }
        else
        {
            // Borderless window over the whole screen, menu bar and Dock hidden (like the borderless maximized window on Windows).
            // Not macOS native full screen: that one leaves a borderless window small in a black Space, and takes Esc for itself.
            Opened += (_, _) => CoverScreen();
            Activated += (_, _) => Helpers.MacPresentation.SetKiosk(true);
            Deactivated += (_, _) => Helpers.MacPresentation.SetKiosk(false);
            Closed += (_, _) => Helpers.MacPresentation.SetKiosk(false);
        }

        _pad.Pressed += _vm.Handle;
        _pad.ExitComboHeld += _vm.ExitGame;
        _vm.CloseRequested += Close;
        _vm.GameEnded += BringToFront;
        _vm.ScrollRequested += delta => PageScroll.Offset = PageScroll.Offset.WithY(Math.Max(0, PageScroll.Offset.Y + delta * 160));
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TvViewModel.Page))
                PageScroll.Offset = default; // another game: start from the top
        };

        Opened += (_, _) => { _pad.Start(); ScrollToSelected(); BringToFront(); };
        Closed += (_, _) => { _pad.Dispose(); _vm.Stop(); };
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>The keyboard navigates too: arrows, Return, Esc/Delete, F (favorite), C (continue), PgUp/PgDn or Q/E (system).</summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
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
    }

    private void Shelf_SelectionChanged(object? sender, SelectionChangedEventArgs e) => ScrollToSelected();

    private void ScrollToSelected()
    {
        if (Shelf.SelectedItem is { } item)
            Shelf.ScrollIntoView(item);
    }

    /// <summary>Back from a game: the emulator had the focus (and its own full-screen space).</summary>
    private void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    /// <summary>Fills the screen the window is on (the whole screen: menu bar and Dock get hidden while it is active).</summary>
    private void CoverScreen()
    {
        if ((Screens.ScreenFromWindow(this) ?? Screens.Primary) is not { } screen)
            return;
        Helpers.MacPresentation.SetKiosk(true);
        Position = screen.Bounds.Position;
        Width = screen.Bounds.Width / screen.Scaling;
        Height = screen.Bounds.Height / screen.Scaling;
    }
}
