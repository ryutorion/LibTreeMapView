using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LibTreeMapView.Core.Model;

namespace LibTreeMapView.Core.Caching;

/// <summary>
/// 解析結果のうち UI が使う情報だけを保存しておく置き場。
/// 対象の .lib と一致するキャッシュがあれば、.lib を読み直さずに表示できる。
/// 失敗しても解析に切り替えられるよう、入出力のエラーは握りつぶす。
/// </summary>
public sealed class LibraryCache
{
    public const string FileExtension = ".ltmcache";

    private const int DefaultMaxEntries = 64;

    private static readonly Lazy<LibraryCache> Shared = new(() => new LibraryCache(DefaultDirectory));

    private readonly int maxEntries;

    public LibraryCache(string directory, int maxEntries = DefaultMaxEntries)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);

        Directory = directory;
        this.maxEntries = maxEntries;
    }

    /// <summary>%LOCALAPPDATA%\LibTreeMapView\cache を使う共有インスタンス。</summary>
    public static LibraryCache Default => Shared.Value;

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LibTreeMapView",
        "cache");

    public string Directory { get; }

    /// <summary>キャッシュがあり、対象ファイルと一致していれば読み込む。</summary>
    public LibraryInfo? TryLoad(string libraryPath, CacheKey key)
    {
        string cachePath = GetCacheFilePath(libraryPath);

        try
        {
            if (!File.Exists(cachePath))
            {
                return null;
            }

            using FileStream stream = File.OpenRead(cachePath);
            LibraryInfo? library = LibraryCacheFormat.TryRead(stream, key);

            if (library is null)
            {
                // 内容が合わない (再ビルドされた等)。次の保存で上書きされる。
                return null;
            }

            TouchQuietly(cachePath);
            return library;
        }
        catch (Exception ex) when (IsIoProblem(ex))
        {
            return null;
        }
    }

    /// <summary>解析結果を保存する。保存できたかどうかを返す。</summary>
    public bool Save(LibraryInfo library, CacheKey key)
    {
        ArgumentNullException.ThrowIfNull(library);

        string cachePath = GetCacheFilePath(library.FilePath);
        string tempPath = cachePath + ".tmp";

        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            using (FileStream stream = File.Create(tempPath))
            {
                LibraryCacheFormat.Write(stream, library, key);
            }

            File.Move(tempPath, cachePath, overwrite: true);
            Prune();
            return true;
        }
        catch (Exception ex) when (IsIoProblem(ex))
        {
            DeleteQuietly(tempPath);
            return false;
        }
    }

    /// <summary>キャッシュを全部消す。消したファイル数を返す。</summary>
    public int Clear()
    {
        int removed = 0;

        foreach (string path in EnumerateEntries())
        {
            if (DeleteQuietly(path))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>保存されているキャッシュの合計サイズ。</summary>
    public long GetTotalSize()
    {
        long total = 0;

        foreach (string path in EnumerateEntries())
        {
            try
            {
                total += new FileInfo(path).Length;
            }
            catch (Exception ex) when (IsIoProblem(ex))
            {
                // 途中で消えたファイルは数えない。
            }
        }

        return total;
    }

    /// <summary>対象の .lib に対応するキャッシュファイルの場所。</summary>
    public string GetCacheFilePath(string libraryPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(libraryPath);

        string name = Path.GetFileNameWithoutExtension(libraryPath);
        if (name.Length == 0)
        {
            name = "library";
        }

        // 同じファイルを別の綴りで指定されても 1 つのキャッシュに収まるよう、名前も小文字に揃える。
        return Path.Combine(Directory, $"{Sanitize(name).ToLowerInvariant()}-{HashPath(libraryPath)}{FileExtension}");
    }

    private IEnumerable<string> EnumerateEntries()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        try
        {
            return System.IO.Directory.EnumerateFiles(Directory, "*" + FileExtension).ToList();
        }
        catch (Exception ex) when (IsIoProblem(ex))
        {
            return [];
        }
    }

    /// <summary>件数が上限を超えたら、最後に使われたのが古いものから消す。</summary>
    private void Prune()
    {
        List<string> entries = EnumerateEntries().ToList();
        if (entries.Count <= maxEntries)
        {
            return;
        }

        foreach (string path in entries
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(maxEntries)
            .Select(f => f.FullName))
        {
            DeleteQuietly(path);
        }
    }

    /// <summary>最終使用日時を更新して、よく使うキャッシュが Prune で消えないようにする。</summary>
    private static void TouchQuietly(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception ex) when (IsIoProblem(ex))
        {
            // 更新できなくても読み込み自体には影響しない。
        }
    }

    private static bool DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (IsIoProblem(ex))
        {
            return false;
        }
    }

    private static bool IsIoProblem(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException;

    /// <summary>フルパスから一意なファイル名を作る (Windows なので大文字小文字は区別しない)。</summary>
    private static string HashPath(string libraryPath)
    {
        string normalized = Path.GetFullPath(libraryPath).ToLowerInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }

    private static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (char c in name)
        {
            builder.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
        }

        return builder.ToString().Trim().TrimEnd('.') is { Length: > 0 } sanitized
            ? sanitized[..Math.Min(sanitized.Length, 48)]
            : "library";
    }

    /// <summary>表示用のサイズ文字列 (デバッグやログ向け)。</summary>
    public override string ToString() =>
        string.Format(CultureInfo.CurrentCulture, "{0} ({1})", Directory, ByteSize.Format(GetTotalSize()));
}
