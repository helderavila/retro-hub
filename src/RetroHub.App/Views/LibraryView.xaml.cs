using System.Windows.Controls;
using System.Windows.Input;
using RetroHub.App.ViewModels;

namespace RetroHub.App.Views;

public partial class LibraryView : UserControl
{
    public LibraryView() => InitializeComponent();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Esc: primeiro cancela a edição das informações; depois volta ao início da biblioteca.
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
        base.OnPreviewKeyDown(e);
    }
}
