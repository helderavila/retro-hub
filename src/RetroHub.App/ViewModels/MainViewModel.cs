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

    /// <summary>Connected Xbox controllers (XInput), refreshed every 2s.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ControllerText), nameof(ControllerKind))]
    private int _controllersConnected;

    public string ControllerText => ControllersConnected switch
    {
        0 => L.T("No controller"),
        1 => L.T("Xbox controller connected"),
        var n => L.F("{0} Xbox controllers", n),
    };

    public string ControllerKind => ControllersConnected > 0 ? "ok" : "idle";

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

        ControllersConnected = XInput.ConnectedSlots().Count;
        _controllerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _controllerTimer.Tick += (_, _) => ControllersConnected = XInput.ConnectedSlots().Count;
        _controllerTimer.Start();
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
