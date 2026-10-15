using System.Windows;
using System.Windows.Threading;
using RetroHub.App.ViewModels;
using RetroHub.Core;

namespace RetroHub.App;

public partial class App : Application
{
    private HubServices? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        try
        {
            _services = HubServices.Create(AppContext.BaseDirectory);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível iniciar o Retro Hub.\n\n{ex.Message}", "Retro Hub",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // Atalho da área de trabalho: "RetroHub.exe --play 42" abre o jogo direto, sem mostrar o Retro Hub.
        var playIndex = Array.FindIndex(e.Args, a => a.Equals("--play", StringComparison.OrdinalIgnoreCase));
        if (playIndex >= 0 && playIndex + 1 < e.Args.Length && long.TryParse(e.Args[playIndex + 1], out var gameId))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = PlayDirectAsync(gameId);
            return;
        }

        var vm = new MainViewModel(_services);
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Show();

        // "RetroHub.exe --tv" ou a opção "Abrir direto no Modo TV" (Pastas › Preferências).
        if (e.Args.Contains("--tv", StringComparer.OrdinalIgnoreCase)
            || _services.Database.GetSetting(MainViewModel.StartInTvSetting) == "1")
            vm.IsTvOpen = true;
    }

    private async Task PlayDirectAsync(long gameId)
    {
        try
        {
            var game = _services!.Database.GetGame(gameId)
                ?? throw new Core.Launch.LaunchException("Este jogo não está mais na biblioteca. Apague o atalho e crie outro.");
            await _services.Launcher.LaunchAsync(game); // espera fechar para contar o tempo jogado
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Retro Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "Retro Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
