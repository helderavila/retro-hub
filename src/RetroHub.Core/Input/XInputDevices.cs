using System.Runtime.InteropServices;

namespace RetroHub.Core.Input;

/// <summary>Um controle XInput conectado: jogador (slot), fabricante, modelo e versão.</summary>
public sealed record XInputDeviceInfo(int Slot, ushort VendorId, ushort ProductId, ushort Version, byte SubType)
{
    /// <summary>
    /// GUID que o SDL dá a este controle, no formato que emuladores com SDL gravam (CRC do nome zerado, como o GetGUID do SDL faz para XInput):
    /// barramento USB 0x0003 · 0000 · fabricante · 0000 · modelo · 0000 · versão · 'x' · subtipo — tudo little-endian.
    /// Ex.: controle Xbox Series (045E:0B13, versão 0x0523) → 030000005e040000130b000023057801.
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
/// Lê fabricante/modelo/versão de cada controle pelo XInputGetCapabilitiesEx — a mesma chamada que o SDL usa
/// (GuessXInputDevice) para montar o GUID. Assim o identificador sai idêntico ao que o emulador calcula.
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

    // Exportado só por ordinal (#108) no xinput1_4.dll, Windows 10+.
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
