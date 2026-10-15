using System.Buffers.Binary;
using System.Text;

namespace RetroHub.Core.Tests;

/// <summary>Imagens de disco sintéticas, só com os bytes que os detectores leem.</summary>
internal static class TestImages
{
    public static byte[] Iso9660(string systemId, string? rootFileName = null, string? rootFileContent = null)
    {
        const int sector = 2048;
        var image = new byte[sector * 24];

        var pvd = 16 * sector;
        image[pvd] = 1;
        Write(image, pvd + 1, "CD001");
        image[pvd + 6] = 1;
        Write(image, pvd + 8, systemId.PadRight(32));

        // Registro do diretório raiz no PVD → setor 18.
        image[pvd + 156] = 34;
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(pvd + 156 + 2), 18);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(pvd + 156 + 10), sector);

        if (rootFileName is not null && rootFileContent is not null)
        {
            var name = rootFileName + ";1";
            var dir = 18 * sector;
            var length = 33 + name.Length + (name.Length % 2 == 0 ? 1 : 0);
            image[dir] = (byte)length;
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(dir + 2), 20);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(dir + 10), (uint)rootFileContent.Length);
            image[dir + 32] = (byte)name.Length;
            Write(image, dir + 33, name);
            Write(image, 20 * sector, rootFileContent);
        }

        return image;
    }

    public static byte[] GameCube(string gameId)
    {
        var image = new byte[0x8000];
        Write(image, 0, gameId);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0x1C), 0xC2339F3D);
        return image;
    }

    public static byte[] Rvz(uint discType, string gameId)
    {
        var image = new byte[0x200];
        Write(image, 0, "RVZ\u0001");
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0x48), discType);
        Write(image, 0x58, gameId);
        return image;
    }

    private static void Write(byte[] target, int offset, string text) =>
        Encoding.ASCII.GetBytes(text).CopyTo(target, offset);
}
