using System.Security.Cryptography;

namespace LibTreeMapView.Core.Caching;

/// <summary>
/// キャッシュが対象の .lib と一致しているかを判定するための情報。
/// サイズと更新日時に加え、ファイルの先頭と末尾から作った指紋も見る。
/// 全体をハッシュすると解析と変わらない時間がかかるため、読むのは最大 128 KB。
/// </summary>
public readonly record struct CacheKey(long FileSize, long LastWriteUtcTicks, ulong Fingerprint)
{
    private const int SampleSize = 64 * 1024;

    /// <summary>対象ファイルからキーを作る。</summary>
    public static CacheKey FromFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException($"ファイルが見つかりません: {path}", path);
        }

        using FileStream stream = File.OpenRead(path);
        return new CacheKey(info.Length, info.LastWriteTimeUtc.Ticks, ComputeFingerprint(stream, info.Length));
    }

    private static ulong ComputeFingerprint(Stream stream, long length)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        Span<byte> lengthBytes = stackalloc byte[8];
        BitConverter.TryWriteBytes(lengthBytes, length);
        hash.AppendData(lengthBytes);

        byte[] buffer = new byte[SampleSize];
        AppendChunk(hash, stream, buffer, 0);

        if (length > SampleSize)
        {
            AppendChunk(hash, stream, buffer, Math.Max(SampleSize, length - SampleSize));
        }

        Span<byte> digest = stackalloc byte[32];
        hash.GetHashAndReset(digest);
        return BitConverter.ToUInt64(digest);
    }

    private static void AppendChunk(IncrementalHash hash, Stream stream, byte[] buffer, long offset)
    {
        stream.Seek(offset, SeekOrigin.Begin);

        int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        if (read > 0)
        {
            hash.AppendData(buffer, 0, read);
        }
    }
}
