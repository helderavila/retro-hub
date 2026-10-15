using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using RetroHub.Core;
using RetroHub.Core.Localization;
using RetroHub.Mac.Input;
using RetroHub.Mac.ViewModels;
using RetroHub.Mac.Views;

namespace RetroHub.Mac;

public partial class App : Application
{
    private HubServices? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            _ = MessageDialog.ShowAsync("Retro Hub", e.Exception.Message);
            e.Handled = true;
        };

        var args = desktop.Args ?? [];
        try
        {
            // Controllers for the emulator mappings come from SDL (no XInput on macOS).
            _services = HubServices.Create(AppContext.BaseDirectory, SdlGamepads.Devices);
            // Before any window: XAML reads the translations while it loads. The app setting wins; otherwise the system language.
            var language = _services.Database.GetSetting(L.Setting) ?? SystemLanguage();
            L.Load(language, Path.Combine(AppContext.BaseDirectory, "i18n"));
        }
        catch (Exception ex)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = MessageDialog.Standalone("Retro Hub", L.F("Retro Hub could not start.\n\n{0}", ex.Message));
            base.OnFrameworkInitializationCompleted();
            return;
        }

        desktop.Exit += (_, _) => _services?.Dispose();

        // Desktop shortcut: "RetroHub --play 42" opens the game directly, without showing Retro Hub.
        var playIndex = Array.FindIndex(args, a => a.Equals("--play", StringComparison.OrdinalIgnoreCase));
        if (playIndex >= 0 && playIndex + 1 < args.Length && long.TryParse(args[playIndex + 1], out var gameId))
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = PlayDirectAsync(desktop, gameId);
            base.OnFrameworkInitializationCompleted();
            return;
        }

        var vm = new MainViewModel(_services);
        desktop.MainWindow = new MainWindow { DataContext = vm };
        if (Helpers.Snapshot.Requested)
            Helpers.Snapshot.Schedule(desktop.MainWindow,
                tab =>
                {
                    if (tab == "game" && vm.Library.AllGames.Count > 0) vm.Library.OpenPage(vm.Library.AllGames[0]);
                    else if (tab == "tv") vm.IsTvOpen = true;
                    else if (tab == "play" && vm.Library.AllGames.Count > 0)
                        DispatcherTimer.RunOnce(() => _ = vm.Library.PlayAsync(vm.Library.AllGames[0]), TimeSpan.FromSeconds(3));
                    else if (tab == "extract") Dispatcher.UIThread.Post(() => vm.Library.ExtractGamesCommand.Execute(null), DispatcherPriority.Background);
                    else vm.Navigate(tab);
                },
                () => vm.Library.ScanCommand.Execute(null), () => desktop.Shutdown());

        // "RetroHub --tv" or the "Start in TV mode" option (Folders › Preferences).
        if (args.Contains("--tv", StringComparer.OrdinalIgnoreCase)
            || _services.Database.GetSetting(MainViewModel.StartInTvSetting) == "1")
            Dispatcher.UIThread.Post(() => vm.IsTvOpen = true);

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>"pt-BR" when macOS is in Portuguese; anything else falls back to English.</summary>
    private static string? SystemLanguage() =>
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt" ? L.Portuguese : null;

    private async Task PlayDirectAsync(IClassicDesktopStyleApplicationLifetime desktop, long gameId)
    {
        try
        {
            var game = _services!.Database.GetGame(gameId)
                ?? throw new Core.Launch.LaunchException(L.T("This game is no longer in the library. Delete the shortcut and create a new one."));
            await _services.Launcher.LaunchAsync(game); // waits for it to close to count play time
        }
        catch (Exception ex)
        {
            await MessageDialog.ShowAsync("Retro Hub", ex.Message);
        }
        finally
        {
            desktop.Shutdown();
        }
    }
}
