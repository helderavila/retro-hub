using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using RetroHub.Core.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.Core;
using RetroHub.Core.Install;
using RetroHub.Core.Models;

namespace RetroHub.App.ViewModels;

public enum EmulatorState { NotInstalled, Installing, Installed, Error }

/// <summary>Um liga/desliga de imagem (widescreen, texturas HD, CRT).</summary>
public sealed partial class GraphicsToggleItem(GraphicsToggle toggle, bool isOn, Action<GraphicsToggle, bool> changed) : ObservableObject
{
    public string Label => toggle.Label;
    public string? Hint => toggle.Hint;
    [ObservableProperty] private bool _isOn = isOn;
    partial void OnIsOnChanged(bool value) => changed(toggle, value);
}

public partial class EmulatorItemViewModel : ObservableObject
{
    public EmulatorManifest Manifest { get; }
    public string Name => Manifest.Name;
    public string Description => Manifest.Description;
    public IReadOnlyList<string> Systems { get; }
    public string ControllerText => "controle · " + (Manifest.Controller?.Description ?? Manifest.ControllerNote ?? "suporte nativo a Xbox");

    public string SourceText => Manifest.Source.Type switch
    {
        "github" => $"github.com/{Manifest.Source.Repo}",
        "forgejo" => new Uri(Manifest.Source.Api ?? "about:blank").Host,
        "local" => $"no seu PC · {Manifest.Source.Path}",
        _ => new Uri(Manifest.Source.Url ?? "about:blank").Host,
    };

    /// <summary>Adicionado pelo usuário (manifesto na pasta dele): pode ser removido daqui.</summary>
    public bool IsCustom => Manifest.IsCustom;
    public bool IsLocal => Manifest.Source.Type == "local";

    [ObservableProperty] private bool _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalled), nameof(VersionText), nameof(ActionLabel), nameof(StatusKind), nameof(StatusText), nameof(StatusTooltip), nameof(HasGraphics))]
    private InstalledEmulator? _installed;

    /// <summary>Versão nova disponível (null = em dia ou ainda não verificado).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(ActionLabel), nameof(StatusText), nameof(StatusKind))]
    private string? _updateVersion;

    public bool HasUpdate => UpdateVersion is not null && IsInstalled;

    // ── Imagem (nível, resolução, proporção, liga/desliga) ─────────────────

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
    public string RecommendedText => HasPresets ? $"recomendado para a sua {Helpers.GpuInfo.BestAdapter ?? "placa"}: {Helpers.GpuInfo.RecommendedPreset}" : "";

    partial void OnShowSettingsChanged(bool value)
    {
        if (value) LoadSettings();
    }

    /// <summary>Relê do arquivo do emulador o que está ativo (o usuário pode ter mudado dentro dele).</summary>
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
        if (!_loadingSettings) LoadSettings(); // o nível muda a resolução: atualiza os chips de baixo
    }

    partial void OnSelectedResolutionChanged(string? value)
    {
        ApplyGraphics(Manifest.Graphics?.Resolution, value);
        if (!_loadingSettings) LoadSettings(); // resolução manual: o nível vira "personalizado"
    }

    partial void OnSelectedAspectChanged(string? value) => ApplyGraphics(Manifest.Graphics?.Aspect, value);

    /// <summary>Aplica um nível ("Original", "Melhorado", "Máximo") — usado também ao instalar e no botão "Imagem recomendada".</summary>
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
            LoadSettings(); // widescreen muda a proporção
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
    public string ActionLabel => HasUpdate ? "Atualizar" : IsLocal ? (IsInstalled ? "Localizar de novo" : "Usar") : IsInstalled ? "Reinstalar" : "Instalar";

    public string StatusKind => State switch
    {
        EmulatorState.Installing => "busy",
        EmulatorState.Error => "error",
        _ when HasUpdate => "warn",
        _ => IsInstalled ? "ok" : "idle",
    };

    public string StatusText => State switch
    {
        EmulatorState.Installing => "Instalando…",
        EmulatorState.Error => "Erro",
        _ when HasUpdate => $"Nova versão: {UpdateVersion}",
        _ when IsLocal && IsInstalled => "Pronto · pasta sua",
        _ when Installed is { Verified: false } => "Instalado · sem hash",
        _ => IsInstalled ? "Instalado" : "Não instalado",
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
        // Uma vez por sessão, em segundo plano: avisa quando sai versão nova.
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
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) { /* sem internet: tenta depois */ }
        }

        if (silent) return;
        ShowPanel = true;
        PanelTitle = "Atualizações";
        Fraction = 1;
        Stage = found.Count == 0 ? "Tudo em dia." : $"{found.Count} atualização(ões) disponível(is) — clique em Atualizar na linha do emulador.";
        Log = "";
        foreach (var line in found) Append($"↑ {line}");
        if (installed.Count == 0) Append("Nenhum emulador instalado ainda.");
        Append("Suas configurações (controle, imagem, saves) são mantidas ao atualizar.");
    }

    // ── Conquistas (RetroAchievements) ──────────────────────────────────────

    [ObservableProperty] private string _achievementsUser = "";

    /// <summary>Chamado pela tela (a senha vem do PasswordBox, que não aceita binding). A senha não é guardada pelo Retro Hub.</summary>
    public void EnableAchievements(string password)
    {
        ShowPanel = true;
        PanelTitle = "Conquistas (RetroAchievements)";
        Fraction = 1;
        Log = "";

        var targets = Items.Where(i => i.Installed is not null && i.Manifest.Achievements is not null).ToList();
        if (targets.Count == 0)
        {
            Stage = "Instale RetroArch, PCSX2 ou DuckStation primeiro.";
            return;
        }
        if (string.IsNullOrWhiteSpace(AchievementsUser) || string.IsNullOrEmpty(password))
        {
            Stage = "Preencha usuário e senha da sua conta em retroachievements.org.";
            return;
        }

        Stage = "Conquistas ativadas";
        foreach (var item in targets)
        {
            try
            {
                EmulatorSettings.EnableAchievements(item.Installed!, item.Manifest.Achievements!, AchievementsUser.Trim(), password);
                Append(item.Manifest.Achievements!.TakesCredentials
                    ? $"✓ {item.Name}: conectado como {AchievementsUser.Trim()} (entra sozinho no próximo jogo)"
                    : $"✓ {item.Name}: ligado — faça o login uma vez dentro dele (Configurações › Conquistas); ele guarda a sessão");
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
                .Select(id => _services.Manifests.GetSystem(id)?.ShortName ?? id.ToUpperInvariant())
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
                PanelTitle = targets.Count == 1 ? $"Instalando {item.Name}" : $"Instalando {item.Name} ({i + 1}/{targets.Count})";
                Fraction = 0;
                item.State = EmulatorState.Installing;
                try
                {
                    item.Installed = await _services.Installer.InstallAsync(item.Manifest, progress, _cts.Token);
                    item.UpdateVersion = null;
                    if (item.ApplyPreset(Helpers.GpuInfo.RecommendedPreset))
                        Append($"✓ imagem: nível {Helpers.GpuInfo.RecommendedPreset} (recomendado para {Helpers.GpuInfo.BestAdapter})");
                    item.State = EmulatorState.Installed;
                    item.Selected = false;
                    if (_services.Controllers.EnsureConfigured(item.Manifest, item.Installed))
                        Append($"✓ controle configurado ({item.Manifest.Controller!.Description})");
                }
                catch (OperationCanceledException)
                {
                    item.State = item.IsInstalled ? EmulatorState.Installed : EmulatorState.NotInstalled;
                    Append("✗ cancelado");
                    break;
                }
                catch (Exception ex)
                {
                    item.Error = ex.Message;
                    item.State = EmulatorState.Error;
                    Append($"✗ {ex.Message}");
                }
            }

            PanelTitle = "Instalação concluída";
            Stage = targets.All(t => t.State == EmulatorState.Installed) ? "Tudo pronto." : "Terminou com pendências — veja o log.";
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
        PanelTitle = "Dependências do sistema";
        Stage = "Consultando winget";
        Fraction = 0;
        Log = "";
        try
        {
            if (!await _services.Dependencies.IsWingetAvailableAsync())
            {
                Append("! winget não encontrado. Instale o \"App Installer\" pela Microsoft Store.");
                return;
            }

            var deps = _services.Manifests.Dependencies;
            for (var i = 0; i < deps.Count; i++)
            {
                var ok = await _services.Dependencies.IsInstalledAsync(deps[i]);
                Append(ok ? $"✓ {deps[i].Name}" : $"· {deps[i].Name} — será instalado junto com o emulador que precisar");
                Fraction = (i + 1.0) / deps.Count;
            }
            Stage = "Verificação concluída";
        }
        finally
        {
            IsInstalling = false;
        }
    }

    /// <summary>Aplica o nível de imagem recomendado para a placa de vídeo em todos os emuladores instalados.</summary>
    [RelayCommand]
    private void ApplyRecommendedGraphics()
    {
        if (IsInstalling) return;
        ShowPanel = true;
        PanelTitle = "Qualidade de imagem";
        Fraction = 1;
        Log = "";
        var preset = Helpers.GpuInfo.RecommendedPreset;
        Stage = $"Nível {preset} — recomendado para {Helpers.GpuInfo.BestAdapter ?? "a sua placa de vídeo"}";
        foreach (var item in Items.Where(i => i.Installed is not null))
        {
            try
            {
                Append(item.ApplyPreset(preset)
                    ? $"✓ {item.Name}: {preset}"
                    : $"· {item.Name}: sem níveis (use o botão Imagem da linha)");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Append($"✗ {item.Name}: {ex.Message}"); }
        }
        Append("Vale a partir do próximo jogo. Feche o emulador antes, se estiver aberto.");
    }

    /// <summary>Regrava o mapeamento de controle em todos os emuladores instalados (desfaz remapeamentos manuais).</summary>
    [RelayCommand]
    private void ConfigureController()
    {
        if (IsInstalling)
            return;

        ShowPanel = true;
        PanelTitle = "Configurar controle";
        Fraction = 1;
        Log = "";
        var connected = XInput.ConnectedSlots().Count;
        Stage = connected > 0
            ? "Controle Xbox detectado — mapeando no jogador 1"
            : "Nenhum controle Xbox conectado agora — o mapeamento vale quando você conectar";

        var installed = Items.Where(i => i.Installed is not null).ToList();
        if (installed.Count == 0)
        {
            Append("Nenhum emulador instalado ainda. O controle é configurado sozinho ao instalar.");
            return;
        }

        foreach (var item in installed)
        {
            try
            {
                var file = _services.Controllers.Apply(item.Manifest, item.Installed!);
                Append(file is null
                    ? item.Manifest.Controller is not null
                        ? $"! {item.Name}: conecte o controle e clique de novo (ele precisa do identificador do controle)"
                        : $"· {item.Name}: suporte nativo, nada a configurar"
                    : $"✓ {item.Name}: {item.Manifest.Controller!.Description}  →  {Path.GetRelativePath(item.Installed!.InstallDir, file)}");
            }
            catch (Exception ex)
            {
                Append($"✗ {item.Name}: {ex.Message}");
            }
        }
        Append("Feche o emulador antes, se estiver aberto — ele pode regravar a configuração ao sair.");
    }

    // ── Emuladores do usuário ───────────────────────────────────────────────

    /// <summary>Aponta o .exe de um emulador que o usuário já tem.</summary>
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
        _ = RegisterAsync(added, $"{added.Name} adicionado");
    }

    /// <summary>Importa um manifesto .json (mesmo formato dos que vêm com o app) para a pasta do usuário.</summary>
    [RelayCommand]
    private void ImportManifest()
    {
        if (IsInstalling) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Manifesto de emulador", Filter = "Manifesto (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;
        ShowPanel = true;
        PanelTitle = "Importar manifesto";
        Fraction = 1;
        Log = "";
        try
        {
            var manifest = _services.CustomEmulators.Import(dialog.FileName);
            Stage = $"{manifest.Name} adicionado à lista";
            Append($"✓ {Path.GetFileName(dialog.FileName)} → {Path.Combine(_services.Paths.UserManifests, "emulators")}");
            foreach (var warning in _services.Manifests.Warnings.Where(w => w.StartsWith(manifest.Id + ":", StringComparison.OrdinalIgnoreCase)))
                Append($"! {warning}");
            Append(manifest.Source.Type == "local" ? "Clique em Usar na linha dele." : "Clique em Instalar na linha dele para baixar.");
            Load();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Stage = "Não foi possível importar";
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

    /// <summary>Tira um emulador adicionado pelo usuário. Os arquivos de um emulador "no seu PC" não são apagados.</summary>
    [RelayCommand]
    private void RemoveCustom(EmulatorItemViewModel item)
    {
        if (IsInstalling || !item.IsCustom) return;
        var detail = item.IsLocal
            ? "A pasta do emulador no seu PC continua como está."
            : "A cópia baixada pelo Retro Hub (pasta emulators) é apagada.";
        if (!Views.ConfirmDialog.Ask($"Remover {item.Name}?", "Ele sai da lista de emuladores do Retro Hub. Jogos e saves não são afetados.", "Remover", detail))
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

    /// <summary>Abre a pasta onde ficam os manifestos do usuário (para quem quiser editar à mão).</summary>
    [RelayCommand]
    private void OpenUserManifests() => Shell.OpenFolder(Path.Combine(_services.Paths.UserManifests, "emulators"));

    [RelayCommand]
    private void OpenFolder(EmulatorItemViewModel item)
    {
        if (item.Installed is { } installed)
            Shell.OpenFolder(installed.InstallDir);
    }
}
