using System.Windows.Controls;

namespace RetroHub.App.Views;

public partial class EmulatorsView : UserControl
{
    public EmulatorsView() => InitializeComponent();

    private void EnableAchievements_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.EmulatorsViewModel vm)
        {
            vm.EnableAchievements(AchievementsPassword.Password);
            AchievementsPassword.Clear();
        }
    }

    private void LogBox_TextChanged(object sender, TextChangedEventArgs e) => LogBox.ScrollToEnd();
}
