using System.IO;
using System.Windows;
using System.Windows.Threading;
using RetroHub.App.ViewModels;
using RetroHub.Core;
using RetroHub.Core.Localization;

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
            // Before any window: XAML reads the translations while it loads.
            // The app setting wins; otherwise the language picked in the installer (HKCU\Software\RetroHub\Language); otherwise English.
            var language = _services.Database.GetSetting(L.Setting)
                           ?? Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\RetroHub", "Language", null) as string;
            L.Load(language, Path.Combine(AppContext.BaseDirectory, "i18n"));
        }
        catch (Exception ex)
        {
            MessageBox.Show(L.F("Retro Hub could not start.\n\n{0}", ex.Message), "Retro Hub",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // Desktop shortcut: "RetroHub.exe --play 42" opens the game directly, without showing Retro Hub.
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

        // "RetroHub.exe --tv" or the "Start in TV mode" option (Folders › Preferences).
        if (e.Args.Contains("--tv", StringComparer.OrdinalIgnoreCase)
            || _services.Database.GetSetting(MainViewModel.StartInTvSetting) == "1")
            vm.IsTvOpen = true;
    }

    private async Task PlayDirectAsync(long gameId)
    {
        try
        {
            var game = _services!.Database.GetGame(gameId)
                ?? throw new Core.Launch.LaunchException(L.T("This game is no longer in the library. Delete the shortcut and create a new one."));
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
