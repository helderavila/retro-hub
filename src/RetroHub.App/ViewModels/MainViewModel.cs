using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Threading;
using RetroHub.Core;
using RetroHub.Core.Input;
using RetroHub.Core.Localization;

namespace RetroHub.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public LibraryViewModel Library { get; }
    public EmulatorsViewModel Emulators { get; }
    public BiosViewModel Bios { get; }
    public FoldersViewModel Folders { get; }
    public StatsViewModel Stats { get; }

    /// <summary>Executable version (Directory.Build.props), e.g. "v1.0.0".</summary>
    public string Version => "v" + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?");

    /// <summary>Key of the current page; the nav RadioButtons bind to it.</summary>
    [ObservableProperty]
    private string _currentKey = "library";

    [ObservableProperty]
    private ObservableObject _currentPage;

    /// <summary>Connected Xbox controllers (XInput) with connection and battery, refreshed every 2s.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ControllerText), nameof(ControllerKind), nameof(BatteryText), nameof(BatteryKind),
        nameof(HasBattery), nameof(IsCharging), nameof(ControllerTip))]
    private IReadOnlyList<GamepadStatus> _controllers = [];

    public string ControllerText => Controllers.Count switch
    {
        0 => L.T("No controller"),
        1 => L.T("Xbox controller connected"),
        var n => L.F("{0} Xbox controllers", n),
    };

    public string ControllerKind => Controllers.Count == 0 ? "idle" : Controllers.Any(c => c.IsLow) ? "warn" : "ok";

    /// <summary>The controller worth showing: the only one, or the one on battery with the least charge.</summary>
    private GamepadStatus? ShownPad => Controllers.Count == 1 ? Controllers[0] : GamepadStatus.Weakest(Controllers);

    public string BatteryText => ShownPad?.BatteryText ?? "";

    public string BatteryKind => ShownPad?.IsLow == true ? "warn" : "idle";

    public bool HasBattery => ShownPad?.Power is PadPower.Battery;

    public bool IsCharging => ShownPad?.Power is PadPower.Charging or PadPower.Charged;

    /// <summary>One line per controller, then how setup works.</summary>
    public string ControllerTip => string.Join("\n", Controllers.Select(c => L.F("Player {0}", c.Player) + " — " + c.Summary)
        .Append((Controllers.Count > 0 ? "\n" : "") + L.T("Your controller is set up automatically the first time you play on each emulator")));

    private readonly DispatcherTimer _controllerTimer;

    private readonly HubServices _services;

    public MainViewModel(HubServices services)
    {
        _services = services;
        Library = new LibraryViewModel(services, Navigate);
        Emulators = new EmulatorsViewModel(services);
        Bios = new BiosViewModel(services);
        Folders = new FoldersViewModel(services);
        Stats = new StatsViewModel(services);
        _currentPage = Library;
        Library.OnActivated();

        // Daily save backup, in the background (does not delay startup).
        if (services.Backup.IsDue)
            _ = Task.Run(async () =>
            {
                try { await services.Backup.CreateAsync(); }
                catch (Exception) { /* retries tomorrow; the manual button shows the error */ }
            });

        RefreshControllers();
        _controllerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _controllerTimer.Tick += (_, _) => RefreshControllers();
        _controllerTimer.Start();
    }

    private void RefreshControllers()
    {
        var now = XInputDevices.Statuses();
        if (!now.SequenceEqual(Controllers))
            Controllers = now;
    }

    public void Navigate(string key) => CurrentKey = key;

    public const string StartInTvSetting = "start_tv";

    /// <summary>Bound to the "TV Mode" button. The window (MainWindow) opens/closes TV mode.</summary>
    [ObservableProperty] private bool _isTvOpen;

    public event Action<bool>? TvModeChanged;

    partial void OnIsTvOpenChanged(bool value) => TvModeChanged?.Invoke(value);

    public TvViewModel CreateTv()
    {
        Library.Load(); // picks up updated games/covers
        return new TvViewModel(Library, _services);
    }

    partial void OnCurrentKeyChanged(string value)
    {
        CurrentPage = value switch
        {
            "emulators" => Emulators,
            "bios" => Bios,
            "folders" => Folders,
            "stats" => Stats,
            _ => Library,
        };
        (CurrentPage as IActivatable)?.OnActivated();
    }
}
