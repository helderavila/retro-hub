using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;
using RetroHub.Core.Localization;
using RetroHub.Core.Models;

namespace RetroHub.App.Views;

/// <summary>A system chip in the dialog.</summary>
public sealed partial class SystemOption(SystemDefinition system) : ObservableObject
{
    public string Id { get; } = system.Id;
    public string Label { get; } = system.ShortName.Length > 0 ? L.T(system.ShortName) : system.Id.ToUpperInvariant();
    public string Name { get; } = L.T(system.Name);
    [ObservableProperty] private bool _isChecked;
}

public sealed record NewEmulator(string Name, string Executable, IReadOnlyList<string> Systems, string Args);

public partial class AddEmulatorDialog : Window
{
    private readonly List<SystemOption> _systems;
    private readonly Func<NewEmulator, string?> _submit;

    /// <param name="submit">Tries to add; returns the error message (or null on success).</param>
    private AddEmulatorDialog(IEnumerable<SystemDefinition> systems, Func<NewEmulator, string?> submit)
    {
        InitializeComponent();
        ArgsHint.Text = L.T("{rom} game file · {romDir} its folder · {romName} name without extension · {emuDir} emulator folder. E.g.: \"{rom}\" --fullscreen");
        _systems = systems.Select(s => new SystemOption(s)).OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        SystemsList.ItemsSource = _systems;
        _submit = submit;
    }

    public static bool Show(IEnumerable<SystemDefinition> systems, Func<NewEmulator, string?> submit)
    {
        var dialog = new AddEmulatorDialog(systems, submit)
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow,
        };
        return dialog.ShowDialog() == true;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = L.T("Emulator executable"), Filter = L.T("Programs (*.exe)|*.exe") };
        if (dialog.ShowDialog(this) != true)
            return;
        ExeBox.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(NameBox.Text))
            NameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var error = _submit(new NewEmulator(NameBox.Text, ExeBox.Text.Trim().Trim('"'),
            _systems.Where(s => s.IsChecked).Select(s => s.Id).ToList(), ArgsBox.Text));
        if (error is null)
        {
            DialogResult = true;
            return;
        }
        ErrorText.Text = error;
        ErrorBox.Visibility = Visibility.Visible;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
