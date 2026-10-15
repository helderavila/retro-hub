using System.Windows;

namespace RetroHub.App.Views;

public partial class ConfirmDialog : Window
{
    private ConfirmDialog() => InitializeComponent();

    /// <summary>Pergunta antes de algo destrutivo. Retorna true se o usuário confirmou.</summary>
    public static bool Ask(string title, string message, string confirmLabel, string? detail = null)
    {
        var dialog = new ConfirmDialog
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow,
        };
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.ConfirmButton.Content = confirmLabel;
        if (detail is not null)
        {
            dialog.DetailText.Text = detail;
            dialog.DetailBox.Visibility = Visibility.Visible;
        }
        return dialog.ShowDialog() == true;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
