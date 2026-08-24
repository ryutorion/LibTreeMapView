using System.Diagnostics;
using LibTreeMapView.Core.Caching;
using LibTreeMapView.Core.Coff;
using LibTreeMapView.Core.Model;

namespace LibTreeMapView.Core;

/// <summary>読み込み結果。表示に使った経路と所要時間も返す。</summary>
public sealed record LibraryLoadResult(LibraryInfo Library, CacheKey Key, bool FromCache, TimeSpan Elapsed);

/// <summary>
/// キャッシュを見てから .lib を解析する読み込み口。
/// キャッシュへの保存は表示を待たせないよう、呼び出し側が <see cref="SaveToCache"/> で後追いする。
/// </summary>
public sealed class LibraryLoader
{
    public LibraryLoader(LibraryCache? cache = null) => Cache = cache;

    public LibraryCache? Cache { get; }

    /// <summary>キャッシュを使うかどうか。切ると常に .lib を解析する。</summary>
    public bool UseCache { get; set; } = true;

    public LibraryLoadResult Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        long start = Stopwatch.GetTimestamp();
        CacheKey key = CacheKey.FromFile(path);

        if (UseCache && Cache?.TryLoad(path, key) is { } cached)
        {
            return new LibraryLoadResult(cached, key, FromCache: true, Stopwatch.GetElapsedTime(start));
        }

        LibraryInfo library = LibReader.Read(path);
        return new LibraryLoadResult(library, key, FromCache: false, Stopwatch.GetElapsedTime(start));
    }

    /// <summary>解析結果をキャッシュに書き出す。保存できたかどうかを返す。</summary>
    public bool SaveToCache(LibraryLoadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return UseCache && Cache is not null && Cache.Save(result.Library, result.Key);
    }
}
