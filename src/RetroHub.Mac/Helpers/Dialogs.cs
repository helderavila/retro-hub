using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace RetroHub.Mac.Helpers;

/// <summary>Open panels (the native macOS ones) and the owner window for dialogs. Avalonia dialogs are async.</summary>
public static class Dialogs
{
    /// <summary>Active window, else the main one: dialogs open on top of whatever the user is looking at.</summary>
    public static Window? Owner
    {
        get
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return null;
            return desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow;
        }
    }

    public static async Task<IReadOnlyList<string>> PickFoldersAsync(string title, bool multiple = false)
    {
        if (Owner?.StorageProvider is not { } storage)
            return [];
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = multiple });
        return folders.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public static async Task<string?> PickFolderAsync(string title) => (await PickFoldersAsync(title)).FirstOrDefault();

    /// <param name="filters">(name, patterns like "*.json"); none = any file.</param>
    public static async Task<IReadOnlyList<string>> PickFilesAsync(string title, bool multiple, params (string Name, string[] Patterns)[] filters)
    {
        if (Owner?.StorageProvider is not { } storage)
            return [];
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = multiple,
            FileTypeFilter = filters.Length == 0 ? null : filters.Select(f => new FilePickerFileType(f.Name) { Patterns = f.Patterns }).ToList(),
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public static async Task<string?> PickFileAsync(string title, params (string Name, string[] Patterns)[] filters) =>
        (await PickFilesAsync(title, false, filters)).FirstOrDefault();
}
