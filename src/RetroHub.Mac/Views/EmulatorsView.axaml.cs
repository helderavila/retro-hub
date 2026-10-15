using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RetroHub.Mac.Views;

public partial class EmulatorsView : UserControl
{
    public EmulatorsView()
    {
        InitializeComponent();
        // The install log follows the newest line.
        LogBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
        };
    }

    private void EnableAchievements_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.EmulatorsViewModel vm)
        {
            vm.EnableAchievements(AchievementsPassword.Text ?? "");
            AchievementsPassword.Text = "";
        }
    }
}
