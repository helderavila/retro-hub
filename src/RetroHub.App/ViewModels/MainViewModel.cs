using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Threading;
using RetroHub.Core;
using RetroHub.Core.Input;

namespace RetroHub.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public LibraryViewModel Library { get; }
    public EmulatorsViewModel Emulators { get; }
    public BiosViewModel Bios { get; }
    public FoldersViewModel Folders { get; }
    public StatsViewModel Stats { get; }

    /// <summary>Versão do executável (Directory.Build.props), ex.: "v1.0.0".</summary>
    public string Version => "v" + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?");

    /// <summary>Chave da página atual; os RadioButtons da sidebar se ligam a ela.</summary>
    [ObservableProperty]
    private string _currentKey = "library";

    [ObservableProperty]
    private ObservableObject _currentPage;

    /// <summary>Controles Xbox conectados (XInput), atualizado a cada 2s.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ControllerText), nameof(ControllerKind))]
    private int _controllersConnected;

    public string ControllerText => ControllersConnected switch
    {
        0 => "Nenhum controle",
        1 => "Controle Xbox conectado",
        var n => $"{n} controles Xbox",
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

        // Backup diário dos saves, em segundo plano (não atrasa a abertura).
        if (services.Backup.IsDue)
            _ = Task.Run(async () =>
            {
                try { await services.Backup.CreateAsync(); }
                catch (Exception) { /* tenta de novo amanhã; o botão manual mostra o erro */ }
            });

        ControllersConnected = XInput.ConnectedSlots().Count;
        _controllerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _controllerTimer.Tick += (_, _) => ControllersConnected = XInput.ConnectedSlots().Count;
        _controllerTimer.Start();
    }

    public void Navigate(string key) => CurrentKey = key;

    public const string StartInTvSetting = "start_tv";

    /// <summary>Ligado ao item "Modo TV" da barra lateral. A janela (MainWindow) abre/fecha o Modo TV.</summary>
    [ObservableProperty] private bool _isTvOpen;

    public event Action<bool>? TvModeChanged;

    partial void OnIsTvOpenChanged(bool value) => TvModeChanged?.Invoke(value);

    public TvViewModel CreateTv()
    {
        Library.Load(); // pega jogos/capas atualizados
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
