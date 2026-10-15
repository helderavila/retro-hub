using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using RetroHub.Mac.ViewModels;

namespace RetroHub.Mac.Views;

public partial class BiosView : UserControl
{
    public BiosView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetDropHighlight(false));
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var accepts = e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = accepts ? DragDropEffects.Copy : DragDropEffects.None;
        SetDropHighlight(accepts);
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        SetDropHighlight(false);
        var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths is { Count: > 0 } && DataContext is BiosViewModel vm)
            await vm.ImportAsync(paths);
    }

    private void SetDropHighlight(bool on)
    {
        if (this.TryFindResource(on ? "Brush.Surface.Raised" : "Brush.Bg.Subtle", out var brush) && brush is IBrush b)
            DropZone.Background = b;
    }
}
