using System.Buffers.Binary;
using System.Text;

namespace RetroHub.Core.Scanning;

/// <summary>
/// Minimal ISO 9660 reader: reads the Primary Volume Descriptor and root files.
/// Supports 2048-byte sectors (.iso) and 2352-byte sectors (raw .bin, Mode 1 and Mode 2/XA).
/// </summary>
internal sealed class Iso9660Reader
{
    private const int LogicalSector = 2048;
    private readonly Stream _stream;
    private readonly int _sectorSize;
    private readonly int _dataOffset;
    private readonly uint _rootLba;
    private readonly uint _rootSize;

    public string SystemIdentifier { get; }
    public string VolumeIdentifier { get; }

    private Iso9660Reader(Stream stream, int sectorSize, int dataOffset, byte[] pvd)
    {
        _stream = stream;
        _sectorSize = sectorSize;
        _dataOffset = dataOffset;
        SystemIdentifier = Ascii(pvd, 8, 32).Trim();
        VolumeIdentifier = Ascii(pvd, 40, 32).Trim();
        _rootLba = BinaryPrimitives.ReadUInt32LittleEndian(pvd.AsSpan(156 + 2));
        _rootSize = BinaryPrimitives.ReadUInt32LittleEndian(pvd.AsSpan(156 + 10));
    }

    public static Iso9660Reader? TryOpen(Stream stream)
    {
        foreach (var (size, offset) in new[] { (2048, 0), (2352, 16), (2352, 24) })
        {
            var pvd = ReadSector(stream, 16, size, offset);
            if (pvd is not null && pvd[0] == 1 && Ascii(pvd, 1, 5) == "CD001")
                return new Iso9660Reader(stream, size, offset, pvd);
        }
        return null;
    }

    /// <summary>Names of the root files and folders (without the ";1").</summary>
    public List<string> RootEntries()
    {
        var names = new List<string>();
        ForEachRootRecord((sector, pos, name) => { names.Add(name); return false; });
        return names;
    }

    public byte[]? ReadRootFile(string name, int maxBytes)
    {
        byte[]? result = null;
        ForEachRootRecord((sector, pos, recordName) =>
        {
            if (!recordName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return false;
            var lba = BinaryPrimitives.ReadUInt32LittleEndian(sector.AsSpan(pos + 2));
            var size = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(sector.AsSpan(pos + 10)), (uint)maxBytes);
            result = ReadFile(lba, size);
            return true;
        });
        return result;
    }

    /// <summary>Walks the root directory records; the callback returns true to stop.</summary>
    private void ForEachRootRecord(Func<byte[], int, string, bool> visit)
    {
        var sectors = (int)Math.Min((_rootSize + LogicalSector - 1) / LogicalSector, 32);
        for (var i = 0; i < sectors; i++)
        {
            var sector = ReadSector(_stream, _rootLba + i, _sectorSize, _dataOffset);
            if (sector is null)
                return;

            var pos = 0;
            while (pos < LogicalSector)
            {
                int length = sector[pos];
                if (length == 0 || pos + length > LogicalSector)
                    break;

                int nameLength = sector[pos + 32];
                var recordName = Ascii(sector, pos + 33, Math.Min(nameLength, length - 33));
                var semicolon = recordName.IndexOf(';');
                if (semicolon >= 0)
                    recordName = recordName[..semicolon];

                // "." and ".." records have names 0x00 / 0x01.
                if (recordName.Length > 0 && recordName[0] > 1 && visit(sector, pos, recordName))
                    return;

                pos += length;
            }
        }
    }

    private byte[]? ReadFile(uint lba, int size)
    {
        var result = new byte[size];
        var copied = 0;
        for (var i = 0; copied < size; i++)
        {
            var sector = ReadSector(_stream, lba + (uint)i, _sectorSize, _dataOffset);
            if (sector is null)
                return null;
            var n = Math.Min(LogicalSector, size - copied);
            Array.Copy(sector, 0, result, copied, n);
            copied += n;
        }
        return result;
    }

    private static byte[]? ReadSector(Stream stream, long lba, int sectorSize, int dataOffset)
    {
        var position = lba * sectorSize + dataOffset;
        if (position + LogicalSector > stream.Length)
            return null;

        var buffer = new byte[LogicalSector];
        stream.Position = position;
        stream.ReadExactly(buffer);
        return buffer;
    }

    internal static string Ascii(byte[] data, int offset, int count) =>
        offset + count <= data.Length ? Encoding.ASCII.GetString(data, offset, count) : "";
}
