using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using RetroHub.Mac.ViewModels;

namespace RetroHub.Mac.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // Esc: first cancels editing the info; then goes back to the library home.
        if (e.Key == Key.Escape && DataContext is LibraryViewModel { Page: { IsEditing: true } page })
        {
            page.CancelEditCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && DataContext is LibraryViewModel { Page: not null } vm)
        {
            vm.CloseDetailsCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Click on a card opens its page (WPF: MouseBinding LeftClick). Clicks on its own buttons do not count.</summary>
    private void Card_Tapped(object? sender, TappedEventArgs e)
    {
        if (FromButton(e) || (sender as Control)?.DataContext is not GameItemViewModel item)
            return;
        item.OpenPageCommand.Execute(null);
    }

    /// <summary>Double click plays (card and sidebar row).</summary>
    private void Card_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (FromButton(e) || (sender as Control)?.DataContext is not GameItemViewModel item)
            return;
        item.PlayCommand.Execute(null);
    }

    private static bool FromButton(RoutedEventArgs e) =>
        e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null;

    /// <summary>Group headers are not selectable and get no highlight (WPF: DataTrigger on IsHeader).</summary>
    private void Sidebar_ContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        var isHeader = e.Container.DataContext is SidebarEntry { IsHeader: true };
        e.Container.Classes.Set("header", isHeader);
        e.Container.Focusable = !isHeader;
    }
}
