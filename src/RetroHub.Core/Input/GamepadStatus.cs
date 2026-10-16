using RetroHub.Core.Localization;

namespace RetroHub.Core.Input;

public enum PadModel { Generic, Xbox, PlayStation, Switch }

public enum PadPower
{
    /// <summary>The controller doesn't report it (many third-party ones).</summary>
    Unknown,
    /// <summary>Wired, no battery.</summary>
    Wired,
    Charging,
    /// <summary>Plugged in and full.</summary>
    Charged,
    /// <summary>Running on its battery.</summary>
    Battery,
}

/// <summary>
/// A connected controller as the user sees it: model, connection and battery. On macOS SDL reports an exact percentage;
/// on Windows XInput only gives four levels, kept as a rough percentage with <paramref name="Approximate"/> set.
/// </summary>
public sealed record GamepadStatus(int Slot, string Name, PadModel Model, bool Wireless, PadPower Power, int? Percent, bool Approximate = false)
{
    /// <summary>At or below this the controller is about to die mid-game: shown in amber and before launching.</summary>
    public const int LowPercent = 15;

    public bool IsLow => Power == PadPower.Battery && Percent <= LowPercent;

    /// <summary>1-based, as players count.</summary>
    public int Player => Slot + 1;

    /// <summary>"Controller battery low · 12%" when it may not last the session, otherwise null.</summary>
    public static string? LowWarning(IEnumerable<GamepadStatus> pads) =>
        Weakest(pads) is { IsLow: true } weak ? L.F("Controller battery low · {0}", weak.BatteryText) : null;

    /// <summary>"73%", "Charging · 40%", "Wired", "Low"… Empty when there is nothing to tell.</summary>
    public string BatteryText => Power switch
    {
        PadPower.Wired => L.T("Wired"),
        PadPower.Charged => L.T("Charged"),
        PadPower.Charging when Percent is { } p && !Approximate => L.F("Charging · {0}%", p),
        PadPower.Charging => L.T("Charging"),
        PadPower.Battery when Percent is { } p => Approximate ? LevelName(p) : $"{p}%",
        _ => "",
    };

    /// <summary>"Xbox Wireless Controller · 73%", one line per controller in tooltips and logs.</summary>
    public string Summary => BatteryText.Length == 0 ? Name : $"{Name} · {BatteryText}";

    /// <summary>The controller to warn about: the one on battery with the least charge.</summary>
    public static GamepadStatus? Weakest(IEnumerable<GamepadStatus> pads) =>
        pads.Where(p => p.Power == PadPower.Battery && p.Percent is not null).MinBy(p => p.Percent);

    private static string LevelName(int percent) => percent switch
    {
        <= XInputLevel.Empty => L.T("Battery empty"),
        <= XInputLevel.Low => L.T("Battery low"),
        <= XInputLevel.Medium => L.T("Battery medium"),
        _ => L.T("Battery full"),
    };

    /// <summary>Model from the USB vendor (XInput has no product names).</summary>
    public static PadModel ModelFromVendor(ushort vendorId) => vendorId switch
    {
        0x045E => PadModel.Xbox,
        0x054C => PadModel.PlayStation,
        0x057E => PadModel.Switch,
        _ => PadModel.Generic,
    };

    /// <summary>XInputGetBatteryInformation: type (0 disconnected, 1 wired, 2 alkaline, 3 NiMH, 0xFF unknown) and level 0–3.</summary>
    public static GamepadStatus FromXInput(int slot, ushort vendorId, byte batteryType, byte batteryLevel)
    {
        var model = ModelFromVendor(vendorId);
        var name = model switch
        {
            PadModel.Xbox => "Xbox Controller",
            PadModel.PlayStation => "PlayStation Controller",
            PadModel.Switch => "Nintendo Controller",
            _ => L.T("Controller"),
        };
        return batteryType switch
        {
            1 => new GamepadStatus(slot, name, model, Wireless: false, PadPower.Wired, null),
            2 or 3 => new GamepadStatus(slot, name, model, Wireless: true, PadPower.Battery, batteryLevel switch
            {
                0 => XInputLevel.Empty,
                1 => XInputLevel.Low,
                2 => XInputLevel.Medium,
                _ => XInputLevel.Full,
            }, Approximate: true),
            _ => new GamepadStatus(slot, name, model, Wireless: false, PadPower.Unknown, null),
        };
    }

    /// <summary>Rough percentage for each XInput level, so Windows and macOS share the same thresholds.</summary>
    private static class XInputLevel
    {
        public const int Empty = 5, Low = 15, Medium = 60, Full = 100;
    }
}
