using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using RetroHub.Core.Localization;
using RetroHub.Core.Models;
using RetroHub.Mac.Helpers;

namespace RetroHub.Mac.Views;

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
    private readonly List<SystemOption> _systems = [];
    private readonly Func<NewEmulator, string?> _submit = _ => null;

    public AddEmulatorDialog() => InitializeComponent();

    /// <param name="submit">Tries to add; returns the error message (or null on success).</param>
    private AddEmulatorDialog(IEnumerable<SystemDefinition> systems, Func<NewEmulator, string?> submit) : this()
    {
        ArgsHint.Text = L.T("{rom} game file · {romDir} its folder · {romName} name without extension · {emuDir} emulator folder. E.g.: \"{rom}\" --fullscreen");
        _systems = systems.Select(s => new SystemOption(s)).OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        SystemsList.ItemsSource = _systems;
        _submit = submit;
    }

    public static async Task<bool> ShowAsync(IEnumerable<SystemDefinition> systems, Func<NewEmulator, string?> submit)
    {
        if (Dialogs.Owner is not { } owner)
            return false;
        return await new AddEmulatorDialog(systems, submit).ShowDialog<bool>(owner);
    }

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        // An app bundle is picked like a file; Core resolves it to the binary inside.
        var app = await Dialogs.PickFileAsync(L.T("Emulator app"), (L.T("Applications"), ["*.app"]));
        if (app is null)
            return;
        ExeBox.Text = app;
        if (string.IsNullOrWhiteSpace(NameBox.Text))
            NameBox.Text = Path.GetFileNameWithoutExtension(app.TrimEnd('/'));
    }

    private void Add_Click(object? sender, RoutedEventArgs e)
    {
        var error = _submit(new NewEmulator(NameBox.Text ?? "", (ExeBox.Text ?? "").Trim().Trim('"'),
            _systems.Where(s => s.IsChecked).Select(s => s.Id).ToList(), ArgsBox.Text ?? ""));
        if (error is null)
        {
            Close(true);
            return;
        }
        ErrorText.Text = error;
        ErrorBox.IsVisible = true;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
