using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RetroHub.Core.Library;
using RetroHub.Core.Models;

namespace RetroHub.Core.Input;

/// <summary>
/// Grava o mapeamento de controle do manifesto na configuração do emulador.
/// Automático: uma vez por emulador (e de novo se o manifesto ou o controle mudar) — se o usuário remapear no emulador, não é desfeito.
/// Forçado: botão "Configurar controle".
/// Placeholders resolvidos na hora: {guid0}/{port0} (jogador 1) e {guid1}/{port1} (jogador 2) — identificador SDL do controle XInput
/// conectado em cada slot. Chaves que dependem de um jogador sem controle são puladas.
/// </summary>
public sealed class ControllerConfigurator(LibraryDatabase db)
{
    private const string SettingPrefix = "controller_configured:";

    /// <summary>Lê os controles conectados. Substituível nos testes.</summary>
    public Func<IReadOnlyList<XInputDeviceInfo>> Devices { get; init; } = XInputDevices.List;

    /// <summary>Aplica se ainda não foi aplicado com esta versão do mapeamento. Retorna true se gravou.</summary>
    public bool EnsureConfigured(EmulatorManifest manifest, InstalledEmulator installed)
    {
        if (manifest.Controller is not { } config || Resolve(config) is not { } settings)
            return false;
        if (db.GetSetting(SettingPrefix + manifest.Id) == Fingerprint(settings))
            return false;

        Write(manifest, installed, config, settings);
        return true;
    }

    /// <summary>
    /// Grava o mapeamento (sobrescrevendo só as chaves do mapeamento). Retorna o arquivo alterado,
    /// ou null se não há mapeamento — ou se ele depende de um controle e nenhum está conectado.
    /// </summary>
    public string? Apply(EmulatorManifest manifest, InstalledEmulator installed)
    {
        if (manifest.Controller is not { } config || Resolve(config) is not { } settings)
            return null;
        return Write(manifest, installed, config, settings);
    }

    /// <summary>true se o mapeamento deste emulador precisa de um controle conectado (ex.: emuladores que guardam o identificador SDL do controle).</summary>
    public static bool NeedsConnectedController(ControllerConfig config) =>
        config.Settings.Values.SelectMany(s => s.Values).Any(v => v.Contains("{guid"));

    private string Write(EmulatorManifest manifest, InstalledEmulator installed, ControllerConfig config,
        Dictionary<string, Dictionary<string, string>> settings)
    {
        var root = Path.GetFullPath(installed.InstallDir) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(installed.InstallDir, config.File));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{manifest.Id}: arquivo de controle fora da pasta do emulador ({config.File}).");

        IniMerger.Merge(path, config.Format, settings);
        db.SetSetting(SettingPrefix + manifest.Id, Fingerprint(settings));
        return path;
    }

    /// <summary>Troca os placeholders pelos controles conectados; null se o jogador 1 é necessário e não há controle.</summary>
    internal Dictionary<string, Dictionary<string, string>>? Resolve(ControllerConfig config)
    {
        if (!NeedsConnectedController(config))
            return config.Settings;

        var devices = Devices().OrderBy(d => d.Slot).ToList();
        if (devices.Count == 0)
            return null;

        var values = new Dictionary<string, string>();
        for (var player = 0; player < Math.Min(devices.Count, 2); player++)
        {
            var guid = devices[player].SdlGuid;
            // Dois controles idênticos têm o mesmo GUID: o SDL diferencia pela "porta" (ordem entre iguais).
            var port = devices.Take(player).Count(d => d.SdlGuid == guid);
            values[$"{{guid{player}}}"] = guid;
            values[$"{{port{player}}}"] = port.ToString();
        }

        var resolved = new Dictionary<string, Dictionary<string, string>>();
        foreach (var (section, keys) in config.Settings)
        {
            var target = resolved[section] = new Dictionary<string, string>();
            foreach (var (key, value) in keys)
            {
                var playerToken = PlayerOf(key) ?? PlayerOf(value);
                if (playerToken is { } p && p >= devices.Count)
                    continue; // jogador sem controle: deixa como está
                var text = value;
                foreach (var (token, replacement) in values)
                    text = text.Replace(token, replacement);
                if (!text.Contains("{guid") && !text.Contains("{port"))
                    target[key] = text;
            }
        }
        return resolved;
    }

    /// <summary>Jogador a que uma chave/valor se refere: "player_1_…" ou "{guid1}" → 1.</summary>
    private static int? PlayerOf(string text)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text, @"player_(\d)_|\{(?:guid|port)(\d)\}");
        if (!m.Success) return null;
        return int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
    }

    private static string Fingerprint(Dictionary<string, Dictionary<string, string>> settings) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings))))[..16];
}
