using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Views;

public partial class BiosView : UserControl
{
    public BiosView() => InitializeComponent();

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var accepts = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = accepts ? DragDropEffects.Copy : DragDropEffects.None;
        DropZone.Background = accepts ? (Brush)FindResource("Brush.Surface.Raised") : (Brush)FindResource("Brush.Bg.Subtle");
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) =>
        DropZone.Background = (Brush)FindResource("Brush.Bg.Subtle");

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropZone.Background = (Brush)FindResource("Brush.Bg.Subtle");
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && DataContext is BiosViewModel vm)
            await vm.ImportAsync(paths);
    }
}
