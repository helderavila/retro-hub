using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace RetroHub.Core.Scanning;

/// <param name="IsVideoDisc">Movie DVD (VIDEO_TS at the root, no SYSTEM.CNF), e.g. bonus discs. Not a game.</param>
public sealed record DiscInfo(string? SystemId, string? Serial, bool IsVideoDisc = false)
{
    public static readonly DiscInfo Unknown = new(null, null);
}

/// <summary>
/// Identifies the system of disc images by reading headers; resolves ambiguous extensions
/// (.iso, .bin, .chd, .cue…) without relying on the file name.
/// </summary>
public static partial class DiscInspector
{
    private const uint GameCubeMagic = 0xC2339F3D; // offset 0x1C
    private const uint WiiMagic = 0x5D1C9EA3;      // offset 0x18
    private const uint GczMagic = 0xB10BC001;

    [GeneratedRegex(@"BOOT2\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex Boot2();

    [GeneratedRegex(@"([A-Z]{4})[_-](\d{3})\.(\d{2})")]
    private static partial Regex PlayStationSerial();

    [GeneratedRegex(@"^[A-Z0-9]{4,6}$")]
    private static partial Regex NintendoGameId();

    public static DiscInfo Inspect(string path) => Inspect(path, depth: 0);

    private static DiscInfo Inspect(string path, int depth)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".cue" or ".m3u" => depth < 2 && SheetParser.References(path).FirstOrDefault(File.Exists) is { } first
                    ? Inspect(first, depth + 1)
                    : DiscInfo.Unknown,
                ".gdi" or ".cdi" => new DiscInfo("dreamcast", null),
                ".rvz" or ".wia" => InspectWia(path),
                ".gcz" => InspectGcz(path),
                ".wbfs" => new DiscInfo("wii", NintendoId(ReadAt(path, 0x200, 6))),
                ".chd" => InspectChd(path),
                ".iso" or ".bin" or ".img" or ".gcm" => InspectImage(path),
                _ => DiscInfo.Unknown,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return DiscInfo.Unknown;
        }
    }

    private static DiscInfo InspectImage(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var head = new byte[0x120];
        if (fs.Read(head, 0, head.Length) < head.Length)
            return DiscInfo.Unknown;

        if (BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(0x1C)) == GameCubeMagic)
            return new DiscInfo("gamecube", NintendoId(head.AsSpan(0, 6)));
        if (BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(0x18)) == WiiMagic)
            return new DiscInfo("wii", NintendoId(head.AsSpan(0, 6)));

        // Sega: header in sector 0 (offset 0 in .iso, 0x10 in raw Mode 1 .bin).
        foreach (var offset in new[] { 0, 0x10 })
        {
            var text = Iso9660Reader.Ascii(head, offset, 16);
            if (text.StartsWith("SEGADISCSYSTEM")) return new DiscInfo("segacd", null);
            if (text.StartsWith("SEGA SEGASATURN")) return new DiscInfo("saturn", null);
            if (text.StartsWith("SEGA SEGAKATANA")) return new DiscInfo("dreamcast", null);
        }

        // Mega Drive cartridge in .bin: "SEGA MEGA DRIVE" / "SEGA GENESIS" at 0x100.
        if (Iso9660Reader.Ascii(head, 0x100, 16).TrimStart().StartsWith("SEGA"))
            return new DiscInfo("genesis", null);

        var iso = Iso9660Reader.TryOpen(fs);
        if (iso is null)
            return DiscInfo.Unknown;

        if (iso.SystemIdentifier.StartsWith("PSP GAME", StringComparison.OrdinalIgnoreCase))
        {
            var umd = iso.ReadRootFile("UMD_DATA.BIN", 64);
            var serial = umd is null ? null : Encoding.ASCII.GetString(umd).Split('|')[0].Trim('\0', ' ');
            return new DiscInfo("psp", string.IsNullOrEmpty(serial) ? null : serial);
        }

        if (iso.SystemIdentifier.StartsWith("PLAYSTATION", StringComparison.OrdinalIgnoreCase))
        {
            var cnf = iso.ReadRootFile("SYSTEM.CNF", 4096);
            if (cnf is null)
                return new DiscInfo("ps1", null);

            var text = Encoding.ASCII.GetString(cnf);
            var system = Boot2().IsMatch(text) ? "ps2" : "ps1";
            var m = PlayStationSerial().Match(text.ToUpperInvariant());
            return new DiscInfo(system, m.Success ? $"{m.Groups[1].Value}-{m.Groups[2].Value}{m.Groups[3].Value}" : null);
        }

        // A PS2 game disc always has "PLAYSTATION" + SYSTEM.CNF (handled above).
        // Without it and with VIDEO_TS at the root, it is a movie DVD; the PS2 BIOS would try to open the DVD player.
        if (iso.RootEntries().Any(n => n.Equals("VIDEO_TS", StringComparison.OrdinalIgnoreCase)))
            return new DiscInfo(null, null, IsVideoDisc: true);

        return DiscInfo.Unknown;
    }

    /// <summary>WIA/RVZ: header 1 is 0x48 bytes; header 2 starts with disc_type (1 = GC, 2 = Wii) and holds the disc header at +0x10.</summary>
    private static DiscInfo InspectWia(string path)
    {
        var head = ReadAt(path, 0, 0x60);
        if (head.Length < 0x60)
            return DiscInfo.Unknown;

        var magic = Encoding.ASCII.GetString(head, 0, 3);
        if (magic is not ("WIA" or "RVZ"))
            return DiscInfo.Unknown;

        var id = NintendoId(head.AsSpan(0x58, 6));
        return BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(0x48)) switch
        {
            1 => new DiscInfo("gamecube", id),
            2 => new DiscInfo("wii", id),
            _ => DiscInfo.Unknown,
        };
    }

    private static DiscInfo InspectGcz(string path)
    {
        var head = ReadAt(path, 0, 8);
        if (head.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(head) != GczMagic)
            return DiscInfo.Unknown;
        return BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(4)) == 1
            ? new DiscInfo("wii", null)
            : new DiscInfo("gamecube", null);
    }

    /// <summary>
    /// CHD: walks the metadata tags. GD-ROM → Dreamcast; DVD → PS2.
    /// CDs (CHT2) are ambiguous and fall back to the folder name.
    /// </summary>
    private static DiscInfo InspectChd(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var header = new byte[0x40];
        if (fs.Read(header, 0, header.Length) < header.Length || Encoding.ASCII.GetString(header, 0, 8) != "MComprHD")
            return DiscInfo.Unknown;

        var version = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12));
        var metaOffset = version switch
        {
            5 => BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(0x30)),
            3 or 4 => BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(0x24)),
            _ => 0UL,
        };

        var entry = new byte[16];
        for (var i = 0; i < 256 && metaOffset != 0 && metaOffset + 16 <= (ulong)fs.Length; i++)
        {
            fs.Position = (long)metaOffset;
            fs.ReadExactly(entry);
            var tag = Encoding.ASCII.GetString(entry, 0, 4);
            if (tag == "CHGD") return new DiscInfo("dreamcast", null);
            if (tag == "DVD ") return new DiscInfo("ps2", null);
            metaOffset = BinaryPrimitives.ReadUInt64BigEndian(entry.AsSpan(8));
        }

        return DiscInfo.Unknown;
    }

    private static byte[] ReadAt(string path, long offset, int count)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (offset >= fs.Length)
            return [];
        fs.Position = offset;
        var buffer = new byte[Math.Min(count, fs.Length - offset)];
        fs.ReadExactly(buffer);
        return buffer;
    }

    private static string? NintendoId(ReadOnlySpan<byte> bytes)
    {
        var id = Encoding.ASCII.GetString(bytes).TrimEnd('\0');
        return NintendoGameId().IsMatch(id) ? id : null;
    }
}
