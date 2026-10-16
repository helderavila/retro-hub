using System.Runtime.InteropServices;
using RetroHub.Core.Input;
using SDL;
using static SDL.SDL3;

namespace RetroHub.Mac.Input;

/// <summary>
/// Game controllers through SDL3 (macOS has no XInput). Plays the role of Core's XInput on Windows:
/// connected list, button state (as <see cref="GamepadState"/>, so navigation code is shared) and the SDL GUID
/// emulators store in their config. Call from the UI thread; <see cref="Update"/> refreshes everything.
/// </summary>
public static unsafe class SdlGamepads
{
    private static bool _tried;
    private static bool _available;
    private static readonly List<(SDL_JoystickID Id, nint Pad)> Open = [];

    /// <summary>Starts SDL's gamepad subsystem once. false if libSDL3 is missing or fails to start.</summary>
    public static bool EnsureInitialized()
    {
        if (_tried)
            return _available;
        _tried = true;
        try
        {
            // Keep reading while an emulator has the focus (Back + Start = quit game in TV mode).
            fixed (byte* name = SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS)
            fixed (byte* one = "1"u8)
                SDL_SetHint(name, one);
            _available = SDL_Init(SDL_InitFlags.SDL_INIT_GAMEPAD);
            if (_available)
            {
                // Polled, not event-driven: nothing drains SDL's queue.
                SDL_SetGamepadEventsEnabled(false);
                SDL_SetJoystickEventsEnabled(false);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _available = false;
        }
        return _available;
    }

    /// <summary>Reads the hardware and opens/closes controllers that appeared or left.</summary>
    public static void Update()
    {
        if (!EnsureInitialized())
            return;

        SDL_UpdateGamepads();
        var ids = new List<SDL_JoystickID>();
        using (var list = SDL_GetGamepads())
        {
            if (list is not null)
                for (var i = 0; i < list.Count; i++)
                    ids.Add(list[i]);
        }

        for (var i = Open.Count - 1; i >= 0; i--)
        {
            if (ids.Contains(Open[i].Id))
                continue;
            SDL_CloseGamepad((SDL_Gamepad*)Open[i].Pad);
            Open.RemoveAt(i);
        }
        foreach (var id in ids)
        {
            if (Open.Any(o => o.Id == id))
                continue;
            var pad = SDL_OpenGamepad(id);
            if (pad != null)
                Open.Add((id, (nint)pad));
        }
    }

    public static int ConnectedCount => Open.Count;

    /// <summary>Current state of each open controller, in the same shape XInput gives on Windows (Y up = positive).</summary>
    public static IReadOnlyList<GamepadState> States() => Open.Select(o => Read(o.Pad)).ToList();

    /// <summary>SDL names buttons by position (south = A on Xbox, Cross on PlayStation); XInput by the Xbox labels.</summary>
    private static readonly (SDL_GamepadButton Sdl, XInputButtons XInput)[] ButtonMap =
    [
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP, XInputButtons.DPadUp),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN, XInputButtons.DPadDown),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT, XInputButtons.DPadLeft),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT, XInputButtons.DPadRight),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START, XInputButtons.Start),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK, XInputButtons.Back),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK, XInputButtons.LeftThumb),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK, XInputButtons.RightThumb),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER, XInputButtons.LeftShoulder),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER, XInputButtons.RightShoulder),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH, XInputButtons.A),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST, XInputButtons.B),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST, XInputButtons.X),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH, XInputButtons.Y),
    ];

    private static GamepadState Read(nint handle)
    {
        var pad = (SDL_Gamepad*)handle;
        var buttons = XInputButtons.None;
        foreach (var (sdl, xinput) in ButtonMap)
            if (SDL_GetGamepadButton(pad, sdl))
                buttons |= xinput;

        var lx = SDL_GetGamepadAxis(pad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX);
        var ly = SDL_GetGamepadAxis(pad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY);
        var lt = SDL_GetGamepadAxis(pad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER);
        var rt = SDL_GetGamepadAxis(pad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER);
        // SDL's Y axis grows downward; XInput's grows upward. Triggers: 0–32767 → 0–255.
        return new GamepadState(buttons, lx, (short)Math.Clamp(-(int)ly, short.MinValue, short.MaxValue),
            (byte)(Math.Max((int)lt, 0) >> 7), (byte)(Math.Max((int)rt, 0) >> 7));
    }

    /// <summary>Connected controllers with model, connection and battery, as SDL reports them (exact percentage).</summary>
    public static IReadOnlyList<GamepadStatus> Statuses()
    {
        if (Environment.GetEnvironmentVariable("RETROHUB_SNAPSHOT_PADS") is { Length: > 0 } fake)
            return FakeStatuses(fake);
        Update();
        var result = new List<GamepadStatus>();
        for (var slot = 0; slot < Open.Count; slot++)
        {
            var (id, handle) = Open[slot];
            var pad = (SDL_Gamepad*)handle;
            int percent;
            var state = SDL_GetGamepadPowerInfo(pad, &percent);
            var power = state switch
            {
                SDL_PowerState.SDL_POWERSTATE_ON_BATTERY => PadPower.Battery,
                SDL_PowerState.SDL_POWERSTATE_CHARGING => PadPower.Charging,
                SDL_PowerState.SDL_POWERSTATE_CHARGED => PadPower.Charged,
                SDL_PowerState.SDL_POWERSTATE_NO_BATTERY => PadPower.Wired,
                _ => PadPower.Unknown,
            };
            var connection = SDL_GetGamepadConnectionState(pad);
            var wireless = connection == SDL_JoystickConnectionState.SDL_JOYSTICK_CONNECTION_WIRELESS;
            // A USB controller often reports no power state at all (macOS's driver doesn't): the cable says enough.
            if (power == PadPower.Unknown && connection == SDL_JoystickConnectionState.SDL_JOYSTICK_CONNECTION_WIRED)
                power = PadPower.Wired;
            var model = SDL_GetGamepadType(pad) switch
            {
                SDL_GamepadType.SDL_GAMEPAD_TYPE_XBOX360 or SDL_GamepadType.SDL_GAMEPAD_TYPE_XBOXONE => PadModel.Xbox,
                SDL_GamepadType.SDL_GAMEPAD_TYPE_PS3 or SDL_GamepadType.SDL_GAMEPAD_TYPE_PS4 or SDL_GamepadType.SDL_GAMEPAD_TYPE_PS5 => PadModel.PlayStation,
                SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_PRO or SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_LEFT
                    or SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_RIGHT
                    or SDL_GamepadType.SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_PAIR => PadModel.Switch,
                _ => PadModel.Generic,
            };
            var name = Marshal.PtrToStringUTF8((nint)Unsafe_SDL_GetGamepadNameForID(id)) ?? "";
            result.Add(new GamepadStatus(slot, name, model, wireless, power,
                percent >= 0 && power is PadPower.Battery or PadPower.Charging ? percent : null));
        }
        return result;
    }

    /// <summary>Dev snapshots only: "Name:Power:Percent;…" (e.g. "DualSense:Battery:12") stands in for real controllers.</summary>
    private static List<GamepadStatus> FakeStatuses(string spec) => spec.Split(';').Select((pad, slot) =>
    {
        var parts = pad.Split(':');
        var power = Enum.Parse<PadPower>(parts[1]);
        return new GamepadStatus(slot, parts[0], PadModel.Generic, power != PadPower.Wired, power,
            parts.Length > 2 ? int.Parse(parts[2]) : null);
    }).ToList();

    /// <summary>Connected controllers with the GUID SDL computes — the same one SDL-based emulators write in their config.</summary>
    public static IReadOnlyList<IGamepadDevice> Devices()
    {
        Update();
        var result = new List<IGamepadDevice>();
        var buffer = stackalloc byte[33];
        for (var slot = 0; slot < Open.Count; slot++)
        {
            var id = Open[slot].Id;
            SDL_GUIDToString(SDL_GetGamepadGUIDForID(id), buffer, 33);
            var guid = Marshal.PtrToStringUTF8((nint)buffer) ?? "";
            var name = Marshal.PtrToStringUTF8((nint)Unsafe_SDL_GetGamepadNameForID(id)) ?? "";
            result.Add(new SdlGamepadDevice(slot, guid, name));
        }
        return result;
    }
}
