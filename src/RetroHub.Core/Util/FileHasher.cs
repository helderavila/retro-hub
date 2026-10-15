using System.Security.Cryptography;

namespace RetroHub.Core.Util;

public static class FileHasher
{
    public static async Task<string> Sha256Async(string path, CancellationToken ct = default)
    {
        await using var stream = OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
    }

    public static async Task<string> Md5Async(string path, CancellationToken ct = default)
    {
        await using var stream = OpenRead(path);
        return Convert.ToHexString(await MD5.HashDataAsync(stream, ct)).ToLowerInvariant();
    }

    public static string Crc32(string path)
    {
        using var stream = OpenRead(path);
        var crc = new System.IO.Hashing.Crc32();
        crc.Append(stream);
        return crc.GetCurrentHashAsUInt32().ToString("x8");
    }

    /// <summary>
    /// Impressão digital rápida para arquivos grandes: SHA-1 de 3 trechos de 1 MiB (início, meio, fim) + tamanho.
    /// Duas cópias do mesmo .iso de 4 GB dão o mesmo valor lendo só 3 MiB de cada.
    /// </summary>
    public static string SampleFingerprint(string path)
    {
        const int Chunk = 1 << 20;
        using var stream = OpenRead(path);
        var length = stream.Length;
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buffer = new byte[Chunk];

        foreach (var offset in new[] { 0L, Math.Max(0, length / 2 - Chunk / 2), Math.Max(0, length - Chunk) }.Distinct())
        {
            stream.Position = offset;
            var read = stream.ReadAtLeast(buffer, Chunk, throwOnEndOfStream: false);
            sha.AppendData(buffer, 0, read);
        }

        return $"s:{Convert.ToHexString(sha.GetHashAndReset())[..20].ToLowerInvariant()}:{length}";
    }

    private static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan | FileOptions.Asynchronous);
}
