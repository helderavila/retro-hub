using System.Runtime.InteropServices;

namespace RetroHub.Core.Input;

[Flags]
public enum XInputButtons : ushort
{
    None = 0,
    DPadUp = 0x0001,
    DPadDown = 0x0002,
    DPadLeft = 0x0004,
    DPadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftThumb = 0x0040,
    RightThumb = 0x0080,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}

public readonly record struct GamepadState(XInputButtons Buttons, short LeftX, short LeftY, byte LeftTrigger, byte RightTrigger);

/// <summary>Controles Xbox via XInput (slots 0–3). Windows 8+ (xinput1_4.dll). Funciona mesmo sem a janela em foco.</summary>
public static class XInput
{
    private const uint ErrorSuccess = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct State
    {
        public uint PacketNumber;
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX, ThumbLY, ThumbRX, ThumbRY;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint GetState(uint userIndex, out State state);

    /// <summary>Slots com controle conectado (0 = jogador 1).</summary>
    public static IReadOnlyList<int> ConnectedSlots()
    {
        var slots = new List<int>();
        for (var i = 0; i < 4; i++)
            if (TryGetState(i, out _))
                slots.Add(i);
        return slots;
    }

    public static bool TryGetState(int slot, out GamepadState state)
    {
        state = default;
        try
        {
            if (GetState((uint)slot, out var raw) != ErrorSuccess)
                return false;
            state = new GamepadState((XInputButtons)raw.Buttons, raw.ThumbLX, raw.ThumbLY, raw.LeftTrigger, raw.RightTrigger);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false; // sem XInput no sistema
        }
    }
}
