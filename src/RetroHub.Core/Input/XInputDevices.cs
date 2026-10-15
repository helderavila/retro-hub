using System.Runtime.InteropServices;

namespace RetroHub.Core.Input;

/// <summary>A connected controller as emulators see it: player slot and the SDL GUID they store in their config.</summary>
public interface IGamepadDevice
{
    int Slot { get; }
    string SdlGuid { get; }
    /// <summary>Name the emulator shows for it (Dolphin on macOS addresses controllers as "SDL/0/&lt;name&gt;").</summary>
    string Name { get; }
}

/// <summary>A controller reported by SDL itself (macOS), which already knows the GUID.</summary>
public sealed record SdlGamepadDevice(int Slot, string SdlGuid, string Name) : IGamepadDevice;

/// <summary>A connected XInput controller: player (slot), vendor, product and version.</summary>
public sealed record XInputDeviceInfo(int Slot, ushort VendorId, ushort ProductId, ushort Version, byte SubType) : IGamepadDevice
{
    /// <summary>XInput has no product names; Dolphin calls every XInput controller "Gamepad".</summary>
    public string Name => "Gamepad";

    /// <summary>
    /// GUID SDL assigns to this controller, in the format SDL-based emulators store (name CRC zeroed, as SDL's GetGUID does for XInput):
    /// USB bus 0x0003 · 0000 · vendor · 0000 · product · 0000 · version · 'x' · subtype — all little-endian.
    /// E.g. Xbox Series controller (045E:0B13, version 0x0523) → 030000005e040000130b000023057801.
    /// </summary>
    public string SdlGuid
    {
        get
        {
            Span<byte> g = stackalloc byte[16];
            g.Clear();
            g[0] = 0x03;
            BitConverter.TryWriteBytes(g[4..], VendorId);
            BitConverter.TryWriteBytes(g[8..], ProductId);
            BitConverter.TryWriteBytes(g[12..], Version);
            g[14] = (byte)'x';
            g[15] = SubType;
            return Convert.ToHexString(g).ToLowerInvariant();
        }
    }
}

/// <summary>
/// Reads vendor/product/version of each controller via XInputGetCapabilitiesEx — the same call SDL uses
/// (GuessXInputDevice) to build the GUID. So the identifier matches exactly what the emulator computes.
/// </summary>
public static class XInputDevices
{
    [StructLayout(LayoutKind.Sequential)]
    private struct CapabilitiesEx
    {
        public byte Type, SubType;
        public ushort Flags;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short ThumbLX, ThumbLY, ThumbRX, ThumbRY;
        public ushort LeftMotor, RightMotor;
        public ushort VendorId, ProductId, ProductVersion, Unknown1;
        public uint Unknown2;
    }

    // Exported only by ordinal (#108) in xinput1_4.dll, Windows 10+.
    [DllImport("xinput1_4.dll", EntryPoint = "#108")]
    private static extern uint XInputGetCapabilitiesEx(uint unknown, uint userIndex, uint flags, out CapabilitiesEx capabilities);

    public static IReadOnlyList<XInputDeviceInfo> List()
    {
        var result = new List<XInputDeviceInfo>();
        try
        {
            for (uint slot = 0; slot < 4; slot++)
            {
                if (XInputGetCapabilitiesEx(1, slot, 0, out var caps) != 0 || caps.VendorId == 0)
                    continue;
                result.Add(new XInputDeviceInfo((int)slot, caps.VendorId, caps.ProductId, caps.ProductVersion, caps.SubType));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        return result;
    }
}
