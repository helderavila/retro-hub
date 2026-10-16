using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using RetroHub.Core.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.Core;
using RetroHub.Core.Install;
using RetroHub.Core.Localization;
using RetroHub.Core.Models;

namespace RetroHub.App.ViewModels;

public enum EmulatorState { NotInstalled, Installing, Installed, Error }

/// <summary>A graphics on/off switch (widescreen, HD textures, CRT).</summary>
public sealed partial class GraphicsToggleItem(GraphicsToggle toggle, bool isOn, Action<GraphicsToggle, bool> changed) : ObservableObject
{
    public string Label => L.T(toggle.Label);
    public string? Hint => toggle.Hint is { } hint ? L.T(hint) : null;
    [ObservableProperty] private bool _isOn = isOn;
    partial void OnIsOnChanged(bool value) => changed(toggle, value);
}

public partial class EmulatorItemViewModel : ObservableObject
{
    public EmulatorManifest Manifest { get; }
    public string Name => Manifest.Name;
    public string Description => L.T(Manifest.Description);
    public IReadOnlyList<string> Systems { get; }
    public string ControllerText => L.F("controller · {0}",
        Manifest.Controller?.Description is { } description ? L.T(description)
        : Manifest.ControllerNote is { } note ? L.T(note)
        : L.T("native Xbox support"));

    public string SourceText => Manifest.Source.Type switch
    {
        "github" => $"github.com/{Manifest.Source.Repo}",
        "forgejo" => new Uri(Manifest.Source.Api ?? "about:blank").Host,
        "local" => L.F("on your PC · {0}", Manifest.Source.Path),
        _ => new Uri(Manifest.Source.Url ?? "about:blank").Host,
    };

    /// <summary>Added by the user (manifest in their folder): can be removed from here.</summary>
    public bool IsCustom => Manifest.IsCustom;
    public bool IsLocal => Manifest.Source.Type == "local";

    [ObservableProperty] private bool _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalled), nameof(VersionText), nameof(ActionLabel), nameof(StatusKind), nameof(StatusText), nameof(StatusTooltip), nameof(HasGraphics))]
    private InstalledEmulator? _installed;

    /// <summary>New version available (null = up to date or not checked yet).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(ActionLabel), nameof(StatusText), nameof(StatusKind))]
    private string? _updateVersion;

    public bool HasUpdate => UpdateVersion is not null && IsInstalled;

    // ── Graphics (level, resolution, aspect ratio, on/off switches) ───────

    [ObservableProperty] private bool _showSettings;
    [ObservableProperty] private string? _selectedPreset;
    [ObservableProperty] private string? _selectedResolution;
    [ObservableProperty] private string? _selectedAspect;
    private bool _loadingSettings;

    public ObservableCollection<GraphicsToggleItem> Toggles { get; } = [];

    public bool HasGraphics => IsInstalled && Manifest.Graphics is not null;
    public IReadOnlyList<string> PresetChoices => Manifest.Graphics?.Presets?.Values.Keys.ToList() ?? [];
    public IReadOnlyList<string> ResolutionChoices => Manifest.Graphics?.Resolution?.Values.Keys.ToList() ?? [];
    public IReadOnlyList<string> AspectChoices => Manifest.Graphics?.Aspect?.Values.Keys.ToList() ?? [];
    public bool HasPresets => PresetChoices.Count > 0;
    public bool HasResolution => ResolutionChoices.Count > 0;
    public bool HasAspect => AspectChoices.Count > 0;
    public string RecommendedText => HasPresets
        ? L.F("recommended for your {0}: {1}", Helpers.GpuInfo.BestAdapter ?? L.T("graphics card"), L.T(Helpers.GpuInfo.RecommendedPreset))
        : "";

    partial void OnShowSettingsChanged(bool value)
    {
        if (value) LoadSettings();
    }

    /// <summary>Re-reads what is active from the emulator's file (the user may have changed it inside the emulator).</summary>
    private void LoadSettings()
    {
        if (Installed is null || Manifest.Graphics is not { } g) return;
        _loadingSettings = true;
        SelectedPreset = EmulatorSettings.Current(Installed, g, g.Presets);
        SelectedResolution = EmulatorSettings.Current(Installed, g, g.Resolution);
        SelectedAspect = EmulatorSettings.Current(Installed, g, g.Aspect);
        Toggles.Clear();
        foreach (var toggle in g.Toggles)
            Toggles.Add(new GraphicsToggleItem(toggle, EmulatorSettings.IsOn(Installed, g, toggle), SetToggle));
        _loadingSettings = false;
    }

    partial void OnSelectedPresetChanged(string? value)
    {
        ApplyGraphics(Manifest.Graphics?.Presets, value);
        if (!_loadingSettings) LoadSettings(); // the level changes the resolution: refresh the chips below
    }

    partial void OnSelectedResolutionChanged(string? value)
    {
        ApplyGraphics(Manifest.Graphics?.Resolution, value);
        if (!_loadingSettings) LoadSettings(); // manual resolution: the level becomes "custom"
    }

    partial void OnSelectedAspectChanged(string? value) => ApplyGraphics(Manifest.Graphics?.Aspect, value);

    /// <summary>Applies a level ("Original", "Enhanced", "Maximum"); also used on install and by the "Recommended graphics" button.</summary>
    public bool ApplyPreset(string preset)
    {
        if (Installed is null || Manifest.Graphics?.Presets is not { } presets || !presets.Values.ContainsKey(preset)) return false;
        EmulatorSettings.Apply(Installed, Manifest.Graphics, presets, preset);
        if (ShowSettings) LoadSettings();
        return true;
    }

    private void ApplyGraphics(GraphicsOption? option, string? choice)
    {
        if (_loadingSettings || choice is null || option is null || Installed is null || Manifest.Graphics is null) return;
        try { EmulatorSettings.Apply(Installed, Manifest.Graphics, option, choice); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Error = ex.Message; }
    }

    private void SetToggle(GraphicsToggle toggle, bool on)
    {
        if (_loadingSettings || Installed is null || Manifest.Graphics is null) return;
        try
        {
            EmulatorSettings.SetToggle(Installed, Manifest.Graphics, toggle, on);
            LoadSettings(); // widescreen changes the aspect ratio
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Error = ex.Message; }
    }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusKind), nameof(StatusText), nameof(StatusTooltip))]
    private EmulatorState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusTooltip))]
    private string? _error;

    public EmulatorItemViewModel(EmulatorManifest manifest, IReadOnlyList<string> systems, InstalledEmulator? installed)
    {
        Manifest = manifest;
        Systems = systems;
        _installed = installed;
        _state = installed is null ? EmulatorState.NotInstalled : EmulatorState.Installed;
    }

    public bool IsInstalled => Installed is not null;
    public string VersionText => Installed?.Version ?? "—";
    public string ActionLabel => HasUpdate ? L.T("Update")
        : IsLocal ? (IsInstalled ? L.T("Locate again") : L.T("Use"))
        : IsInstalled ? L.T("Reinstall") : L.T("Install");

    public string StatusKind => State switch
    {
        EmulatorState.Installing => "busy",
        EmulatorState.Error => "error",
        _ when HasUpdate => "warn",
        _ => IsInstalled ? "ok" : "idle",
    };

    public string StatusText => State switch
    {
        EmulatorState.Installing => L.T("Installing…"),
        EmulatorState.Error => L.T("Error"),
        _ when HasUpdate => L.F("New version: {0}", UpdateVersion),
        _ when IsLocal && IsInstalled => L.T("Ready · your folder"),
        _ when Installed is { Verified: false } => L.T("Installed · no hash"),
        _ => IsInstalled ? L.T("Installed") : L.T("Not installed"),
    };

    public string? StatusTooltip => State == EmulatorState.Error
        ? Error
        : Installed is { } i ? $"sha256 {i.Sha256}\n{i.InstallDir}" : null;
}

public partial class EmulatorsViewModel : ObservableObject, IActivatable
{
    private readonly HubServices _services;
    private CancellationTokenSource? _cts;

    public ObservableCollection<EmulatorItemViewModel> Items { get; } = [];

    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private bool _showPanel;
    [ObservableProperty] private string _panelTitle = "";
    [ObservableProperty] private string _stage = "";
    [ObservableProperty] private double _fraction;
    [ObservableProperty] private string _log = "";

    public int InstalledCount => Items.Count(i => i.IsInstalled);

    public EmulatorsViewModel(HubServices services)
    {
        _services = services;
        Load();
    }

    private bool _updatesChecked;

    public void OnActivated()
    {
        if (!IsInstalling)
            Load();
        // Once per session, in the background: flags when a new version is out.
        if (!_updatesChecked)
        {
            _updatesChecked = true;
            _ = CheckUpdatesAsync(silent: true);
        }
    }

    [RelayCommand]
    private Task CheckUpdates() => CheckUpdatesAsync(silent: false);

    private async Task CheckUpdatesAsync(bool silent)
    {
        var installed = Items.Where(i => i.Installed is not null).ToList();
        var found = new List<string>();
        foreach (var item in installed)
        {
            try
            {
                item.UpdateVersion = await _services.Installer.CheckUpdateAsync(item.Manifest, item.Installed!);
                if (item.UpdateVersion is { } v) found.Add($"{item.Name} {v}");
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) { /* offline: try again later */ }
        }

        if (silent) return;
        ShowPanel = true;
        PanelTitle = L.T("Updates");
        Fraction = 1;
        Stage = found.Count == 0 ? L.T("Everything is up to date.") : L.F("{0} update(s) available — click Update on the emulator row.", found.Count);
        Log = "";
        foreach (var line in found) Append($"↑ {line}");
        if (installed.Count == 0) Append(L.T("No emulators installed yet."));
        Append(L.T("Your settings (controller, graphics, saves) are kept when updating."));
    }

    // ── Achievements (RetroAchievements) ────────────────────────────────────

    [ObservableProperty] private string _achievementsUser = "";

    /// <summary>Called by the view (the password comes from the PasswordBox, which does not support binding). Retro Hub does not store the password.</summary>
    public void EnableAchievements(string password)
    {
        ShowPanel = true;
        PanelTitle = L.T("Achievements (RetroAchievements)");
        Fraction = 1;
        Log = "";

        var targets = Items.Where(i => i.Installed is not null && i.Manifest.Achievements is not null).ToList();
        if (targets.Count == 0)
        {
            Stage = L.T("Install RetroArch, PCSX2 or DuckStation first.");
            return;
        }
        if (string.IsNullOrWhiteSpace(AchievementsUser) || string.IsNullOrEmpty(password))
        {
            Stage = L.T("Enter the username and password of your retroachievements.org account.");
            return;
        }

        Stage = L.T("Achievements enabled");
        foreach (var item in targets)
        {
            try
            {
                EmulatorSettings.EnableAchievements(item.Installed!, item.Manifest.Achievements!, AchievementsUser.Trim(), password);
                Append(item.Manifest.Achievements!.TakesCredentials
                    ? L.F("✓ {0}: signed in as {1} (logs in automatically on the next game)", item.Name, AchievementsUser.Trim())
                    : L.F("✓ {0}: enabled — sign in once inside it (Settings › Achievements); it remembers the session", item.Name));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Append($"✗ {item.Name}: {ex.Message}");
            }
        }
    }

    private void Load()
    {
        var installed = _services.Database.GetInstalledEmulators().ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
        Items.Clear();
        foreach (var manifest in _services.Manifests.Emulators)
        {
            var systems = manifest.Systems
                .Select(id => _services.Manifests.GetSystem(id)?.ShortName is { } shortName ? L.T(shortName) : id.ToUpperInvariant())
                .ToList();
            Items.Add(new EmulatorItemViewModel(manifest, systems, installed.GetValueOrDefault(manifest.Id)));
        }
        OnPropertyChanged(nameof(InstalledCount));
    }

    [RelayCommand]
    private Task InstallSelected()
    {
        var targets = Items.Where(i => i.Selected).ToList();
        if (targets.Count == 0)
            targets = Items.Where(i => !i.IsInstalled).ToList();
        return RunInstallAsync(targets);
    }

    [RelayCommand]
    private Task Install(EmulatorItemViewModel item) => RunInstallAsync([item]);

    private async Task RunInstallAsync(IReadOnlyList<EmulatorItemViewModel> targets)
    {
        if (IsInstalling || targets.Count == 0)
            return;

        IsInstalling = true;
        ShowPanel = true;
        Log = "";
        _cts = new CancellationTokenSource();
        var progress = new Progress<InstallUpdate>(OnUpdate);

        try
        {
            for (var i = 0; i < targets.Count; i++)
            {
                var item = targets[i];
                PanelTitle = targets.Count == 1 ? L.F("Installing {0}", item.Name) : L.F("Installing {0} ({1}/{2})", item.Name, i + 1, targets.Count);
                Fraction = 0;
                item.State = EmulatorState.Installing;
                try
                {
                    item.Installed = await _services.Installer.InstallAsync(item.Manifest, progress, _cts.Token);
                    item.UpdateVersion = null;
                    if (item.ApplyPreset(Helpers.GpuInfo.RecommendedPreset))
                        Append(L.F("✓ graphics: {0} level (recommended for {1})", L.T(Helpers.GpuInfo.RecommendedPreset), Helpers.GpuInfo.BestAdapter));
                    item.State = EmulatorState.Installed;
                    item.Selected = false;
                    if (_services.Controllers.EnsureConfigured(item.Manifest, item.Installed))
                        Append(L.F("✓ controller set up ({0})", L.T(item.Manifest.Controller!.Description)));
                }
                catch (OperationCanceledException)
                {
                    item.State = item.IsInstalled ? EmulatorState.Installed : EmulatorState.NotInstalled;
                    Append(L.T("✗ canceled"));
                    break;
                }
                catch (Exception ex)
                {
                    item.Error = ex.Message;
                    item.State = EmulatorState.Error;
                    Append($"✗ {ex.Message}");
                }
            }

            PanelTitle = L.T("Installation complete");
            Stage = targets.All(t => t.State == EmulatorState.Installed) ? L.T("All set.") : L.T("Finished with issues — check the log.");
        }
        finally
        {
            IsInstalling = false;
            _cts.Dispose();
            _cts = null;
            OnPropertyChanged(nameof(InstalledCount));
        }
    }

    private void OnUpdate(InstallUpdate update)
    {
        if (update.Stage is { } stage) Stage = stage;
        if (update.Fraction is { } fraction) Fraction = fraction;
        if (update.Log is { } line) Append(line);
    }

    private void Append(string line) =>
        Log += (Log.Length == 0 ? "" : "\n") + $"{DateTime.Now:HH:mm:ss}  {line}";

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void ClosePanel()
    {
        if (!IsInstalling)
            ShowPanel = false;
    }

    [RelayCommand]
    private async Task CheckDependencies()
    {
        if (IsInstalling)
            return;

        IsInstalling = true;
        ShowPanel = true;
        PanelTitle = L.T("System dependencies");
        Stage = L.T("Checking winget");
        Fraction = 0;
        Log = "";
        try
        {
            if (!await _services.Dependencies.IsWingetAvailableAsync())
            {
                Append(L.T("! winget not found. Install \"App Installer\" from the Microsoft Store."));
                return;
            }

            var deps = _services.Manifests.Dependencies;
            for (var i = 0; i < deps.Count; i++)
            {
                var ok = await _services.Dependencies.IsInstalledAsync(deps[i]);
                Append(ok ? $"✓ {deps[i].Name}" : L.F("· {0} — will be installed with the emulator that needs it", deps[i].Name));
                Fraction = (i + 1.0) / deps.Count;
            }
            Stage = L.T("Check complete");
        }
        finally
        {
            IsInstalling = false;
        }
    }

    /// <summary>Applies the graphics level recommended for the graphics card to every installed emulator.</summary>
    [RelayCommand]
    private void ApplyRecommendedGraphics()
    {
        if (IsInstalling) return;
        ShowPanel = true;
        PanelTitle = L.T("Graphics quality");
        Fraction = 1;
        Log = "";
        var preset = Helpers.GpuInfo.RecommendedPreset;
        Stage = L.F("{0} level — recommended for {1}", L.T(preset), Helpers.GpuInfo.BestAdapter ?? L.T("your graphics card"));
        foreach (var item in Items.Where(i => i.Installed is not null))
        {
            try
            {
                Append(item.ApplyPreset(preset)
                    ? $"✓ {item.Name}: {L.T(preset)}"
                    : L.F("· {0}: no quality levels (use the Graphics button on its row)", item.Name));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Append($"✗ {item.Name}: {ex.Message}"); }
        }
        Append(L.T("Takes effect on the next game. Close the emulator first if it is open."));
    }

    /// <summary>Rewrites the controller mapping in every installed emulator (undoes manual remaps).</summary>
    [RelayCommand]
    private void ConfigureController()
    {
        if (IsInstalling)
            return;

        ShowPanel = true;
        PanelTitle = L.T("Set up controller");
        Fraction = 1;
        Log = "";
        var connected = XInput.ConnectedSlots().Count;
        Stage = connected > 0
            ? L.T("Xbox controller detected — mapping to player 1")
            : L.T("No Xbox controller connected right now — the mapping applies once you connect one");
        foreach (var pad in XInputDevices.Statuses())
            Append(L.F("Player {0}", pad.Player) + " — " + pad.Summary);

        var installed = Items.Where(i => i.Installed is not null).ToList();
        if (installed.Count == 0)
        {
            Append(L.T("No emulators installed yet. The controller is set up automatically on install."));
            return;
        }

        foreach (var item in installed)
        {
            try
            {
                var file = _services.Controllers.Apply(item.Manifest, item.Installed!);
                Append(file is null
                    ? item.Manifest.Controller is not null
                        ? L.F("! {0}: connect the controller and click again (it needs the controller ID)", item.Name)
                        : L.F("· {0}: native support, nothing to set up", item.Name)
                    : $"✓ {item.Name}: {L.T(item.Manifest.Controller!.Description)}  →  {Path.GetRelativePath(item.Installed!.InstallDir, file)}");
            }
            catch (Exception ex)
            {
                Append($"✗ {item.Name}: {ex.Message}");
            }
        }
        Append(L.T("Close the emulator first if it is open — it may overwrite the settings when it exits."));
    }

    // ── User emulators ──────────────────────────────────────────────────────

    /// <summary>Points to the .exe of an emulator the user already has.</summary>
    [RelayCommand]
    private void AddEmulator()
    {
        if (IsInstalling) return;
        EmulatorManifest? added = null;
        var ok = Views.AddEmulatorDialog.Show(_services.Manifests.Systems, input =>
        {
            try
            {
                added = _services.CustomEmulators.CreateLocal(input.Name, input.Executable, input.Systems, input.Args);
                return null;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                return ex.Message;
            }
        });
        if (!ok || added is null) return;
        _ = RegisterAsync(added, L.F("{0} added", added.Name));
    }

    /// <summary>Imports a .json manifest (same format as the bundled ones) into the user's folder.</summary>
    [RelayCommand]
    private void ImportManifest()
    {
        if (IsInstalling) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = L.T("Emulator manifest"), Filter = L.T("Manifest (*.json)|*.json") };
        if (dialog.ShowDialog() != true) return;
        ShowPanel = true;
        PanelTitle = L.T("Import manifest");
        Fraction = 1;
        Log = "";
        try
        {
            var manifest = _services.CustomEmulators.Import(dialog.FileName);
            Stage = L.F("{0} added to the list", manifest.Name);
            Append($"✓ {Path.GetFileName(dialog.FileName)} → {Path.Combine(_services.Paths.UserManifests, "emulators")}");
            foreach (var warning in _services.Manifests.Warnings.Where(w => w.StartsWith(manifest.Id + ":", StringComparison.OrdinalIgnoreCase)))
                Append($"! {warning}");
            Append(manifest.Source.Type == "local" ? L.T("Click Use on its row.") : L.T("Click Install on its row to download it."));
            Load();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Stage = L.T("Could not import");
            Append($"✗ {ex.Message}");
        }
    }

    private async Task RegisterAsync(EmulatorManifest manifest, string title)
    {
        Load();
        if (Items.FirstOrDefault(i => i.Manifest.Id == manifest.Id) is { } item)
        {
            await RunInstallAsync([item]);
            PanelTitle = title;
        }
    }

    /// <summary>Removes an emulator added by the user. The files of an "on your PC" emulator are not deleted.</summary>
    [RelayCommand]
    private void RemoveCustom(EmulatorItemViewModel item)
    {
        if (IsInstalling || !item.IsCustom) return;
        var detail = item.IsLocal
            ? L.T("The emulator folder on your PC stays as it is.")
            : L.T("The copy downloaded by Retro Hub (emulators folder) is deleted.");
        if (!Views.ConfirmDialog.Ask(L.F("Remove {0}?", item.Name), L.T("It leaves the Retro Hub emulator list. Games and saves are not affected."), L.T("Remove"), detail))
            return;
        try
        {
            _services.CustomEmulators.Remove(item.Manifest, _services.Installer);
            Load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            item.Error = ex.Message;
            item.State = EmulatorState.Error;
        }
    }

    /// <summary>Opens the folder holding the user's manifests (for anyone who wants to edit them by hand).</summary>
    [RelayCommand]
    private void OpenUserManifests() => Shell.OpenFolder(Path.Combine(_services.Paths.UserManifests, "emulators"));

    [RelayCommand]
    private void OpenFolder(EmulatorItemViewModel item)
    {
        if (item.Installed is { } installed)
            Shell.OpenFolder(installed.InstallDir);
    }
}
