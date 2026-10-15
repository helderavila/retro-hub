using Avalonia.Controls;
using RetroHub.Core;
using RetroHub.Core.Models;
using RetroHub.Mac.Helpers;
using RetroHub.Mac.ViewModels;

namespace RetroHub.Mac.Views;

public partial class UnpackDialog : Window
{
    public UnpackDialog()
    {
        InitializeComponent();
        // The log follows the newest line.
        LogBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
        };
    }

    /// <summary>Opens the dialog; true when at least one game was extracted (the library reloads).</summary>
    /// <param name="only">Just this game checked (right-click › Extract).</param>
    public static async Task<bool> ShowAsync(HubServices services, IEnumerable<Game> games, long? only = null)
    {
        if (Dialogs.Owner is not { } owner)
            return false;
        var vm = new UnpackViewModel(services, games, only);
        var dialog = new UnpackDialog { DataContext = vm };
        vm.CloseRequested += () => dialog.Close();
        // Closing while extracting cancels; the dialog closes once the current game is rolled back.
        dialog.Closing += (_, e) =>
        {
            if (vm.IsWorking)
            {
                e.Cancel = true;
                vm.CancelCommand.Execute(null);
            }
        };
        // Development captures: RETROHUB_SNAPSHOT_FILES="a.zip;b.zip" as if picked with "Add files…".
        if (Snapshot.Requested && Environment.GetEnvironmentVariable("RETROHUB_SNAPSHOT_FILES") is { Length: > 0 } files)
            vm.AddArchives(files.Split(';'));
        // Development captures (RETROHUB_SNAPSHOT_RUN=1): start right away, as if Extract was clicked.
        if (Snapshot.Requested && Environment.GetEnvironmentVariable("RETROHUB_SNAPSHOT_RUN") == "1")
            dialog.Opened += (_, _) => vm.ExtractCommand.Execute(null);
        await dialog.ShowDialog(owner);
        return vm.Changed;
    }
}
