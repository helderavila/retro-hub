using Avalonia.Controls;
using Avalonia.Interactivity;
using RetroHub.Core.Localization;
using RetroHub.Mac.Helpers;

namespace RetroHub.Mac.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog() => InitializeComponent();

    /// <summary>Asks before something destructive. Returns true if the user confirmed (texts come already translated).</summary>
    public static async Task<bool> AskAsync(string title, string message, string confirmLabel, string? detail = null)
    {
        var dialog = Create(title, message, confirmLabel, detail);
        return Dialogs.Owner is { } owner && await dialog.ShowDialog<bool>(owner);
    }

    internal static ConfirmDialog Create(string title, string message, string confirmLabel, string? detail = null)
    {
        var dialog = new ConfirmDialog();
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.ConfirmButton.Content = confirmLabel;
        if (detail is not null)
        {
            dialog.DetailText.Text = detail;
            dialog.DetailBox.IsVisible = true;
        }
        return dialog;
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e) => Close(true);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}

/// <summary>Plain message with an OK button (the WPF app's MessageBox), in the same themed dialog.</summary>
public static class MessageDialog
{
    public static async Task ShowAsync(string title, string message)
    {
        if (Dialogs.Owner is not { } owner)
            return;
        var dialog = Prepare(title, message);
        await dialog.ShowDialog<bool>(owner);
    }

    /// <summary>When there is no main window yet (startup failure): the dialog is the window, and closing it quits.</summary>
    public static Window Standalone(string title, string message)
    {
        var dialog = Prepare(title, message);
        dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowInTaskbar = true;
        return dialog;
    }

    private static ConfirmDialog Prepare(string title, string message)
    {
        var dialog = ConfirmDialog.Create(title, message, L.T("OK"));
        dialog.CancelButton.IsVisible = false;
        return dialog;
    }
}
