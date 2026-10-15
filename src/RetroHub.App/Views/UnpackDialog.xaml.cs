using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using RetroHub.App.ViewModels;
using RetroHub.Core;
using RetroHub.Core.Models;

namespace RetroHub.App.Views;

public partial class UnpackDialog : Window
{
    private readonly UnpackViewModel _vm;

    private UnpackDialog(UnpackViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        vm.CloseRequested += Close;
    }

    /// <summary>Opens the dialog; true when at least one game was extracted (the library reloads).</summary>
    /// <param name="only">Just this game checked (right-click › Extract).</param>
    public static bool Show(HubServices services, IEnumerable<Game> games, long? only = null)
    {
        var dialog = new UnpackDialog(new UnpackViewModel(services, games, only))
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow,
        };
        dialog.ShowDialog();
        return dialog._vm.Changed;
    }

    /// <summary>Closing while extracting cancels; the dialog closes once the current game is rolled back.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_vm.IsWorking)
        {
            e.Cancel = true;
            _vm.CancelCommand.Execute(null);
        }
        base.OnClosing(e);
    }

    private void LogBox_TextChanged(object sender, TextChangedEventArgs e) => LogBox.ScrollToEnd();
}
