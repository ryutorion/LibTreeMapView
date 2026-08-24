using LibTreeMapView.Core;
using LibTreeMapView.Core.Caching;
using LibTreeMapView.Core.Coff;
using LibTreeMapView.Core.Model;

namespace LibTreeMapView.Core.Tests;

/// <summary>UI 表示に必要な情報だけを保存するキャッシュの検証。</summary>
public sealed class LibraryCacheTests : IDisposable
{
    private readonly string workDirectory;
    private readonly string cacheDirectory;
    private readonly string libraryPath;

    public LibraryCacheTests()
    {
        workDirectory = Path.Combine(Path.GetTempPath(), "ltmv-cache-tests", Guid.NewGuid().ToString("N"));
        cacheDirectory = Path.Combine(workDirectory, "cache");
        Directory.CreateDirectory(workDirectory);

        // 元のフィクスチャを触らないよう作業用にコピーする。
        libraryPath = Path.Combine(workDirectory, "fixture.lib");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "fixture.lib"), libraryPath);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(workDirectory, recursive: true);
        }
        catch (IOException)
        {
            // 後始末に失敗してもテスト結果には影響しない。
        }
    }

    private LibraryCache CreateCache(int maxEntries = 64) => new(cacheDirectory, maxEntries);

    [Fact]
    public void SaveAndLoad_RestoresEverythingTheUiNeeds()
    {
        LibraryCache cache = CreateCache();
        LibraryInfo original = LibReader.Read(libraryPath);

        Assert.True(cache.Save(original, CacheKey.FromFile(libraryPath)));
        LibraryInfo? restored = cache.TryLoad(libraryPath, CacheKey.FromFile(libraryPath));

        Assert.NotNull(restored);
        AssertSameLibrary(original, restored!);
    }

    [Fact]
    public void SaveAndLoad_KeepsSyntheticAndImportInformation()
    {
        LibraryCache cache = CreateCache();
        string importPath = Path.Combine(workDirectory, "import.lib");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "import.lib"), importPath);

        LibraryInfo original = LibReader.Read(importPath);
        cache.Save(original, CacheKey.FromFile(importPath));
        LibraryInfo restored = cache.TryLoad(importPath, CacheKey.FromFile(importPath))!;

        AssertSameLibrary(original, restored);
        Assert.All(
            restored.Objects.Where(o => o.Kind == ObjectFileKind.Import),
            o =>
            {
                Assert.Equal("sampledll.dll", o.ImportDllName, ignoreCase: true);
                Assert.True(o.Sections.Single().IsSynthetic);
            });
    }

    [Fact]
    public void CacheFileIsMuchSmallerThanTheLibrary()
    {
        LibraryCache cache = CreateCache();
        LibraryInfo library = LibReader.Read(libraryPath);
        cache.Save(library, CacheKey.FromFile(libraryPath));

        long cacheSize = new FileInfo(cache.GetCacheFilePath(libraryPath)).Length;

        // セクションの実体やシンボルを持たないので、元の .lib よりずっと小さくなる。
        Assert.True(cacheSize < new FileInfo(libraryPath).Length / 2, $"キャッシュが大きすぎます: {cacheSize} バイト");
    }

    [Fact]
    public void TryLoad_ReturnsNullWhenTheLibraryWasRebuilt()
    {
        LibraryCache cache = CreateCache();
        cache.Save(LibReader.Read(libraryPath), CacheKey.FromFile(libraryPath));

        // 中身が変わればキーが変わる。
        byte[] data = File.ReadAllBytes(libraryPath);
        data[^1] ^= 0xFF;
        File.WriteAllBytes(libraryPath, data);

        Assert.Null(cache.TryLoad(libraryPath, CacheKey.FromFile(libraryPath)));
    }

    [Fact]
    public void TryLoad_ReturnsNullWhenOnlyTheTimestampChanged()
    {
        LibraryCache cache = CreateCache();
        cache.Save(LibReader.Read(libraryPath), CacheKey.FromFile(libraryPath));

        File.SetLastWriteTimeUtc(libraryPath, File.GetLastWriteTimeUtc(libraryPath).AddMinutes(1));

        Assert.Null(cache.TryLoad(libraryPath, CacheKey.FromFile(libraryPath)));
    }

    [Fact]
    public void TryLoad_ReturnsNullForBrokenCacheFile()
    {
        LibraryCache cache = CreateCache();
        cache.Save(LibReader.Read(libraryPath), CacheKey.FromFile(libraryPath));

        string cachePath = cache.GetCacheFilePath(libraryPath);
        byte[] broken = File.ReadAllBytes(cachePath)[..40];
        File.WriteAllBytes(cachePath, broken);

        Assert.Null(cache.TryLoad(libraryPath, CacheKey.FromFile(libraryPath)));
    }

    [Fact]
    public void TryLoad_ReturnsNullWhenNothingWasCached()
    {
        Assert.Null(CreateCache().TryLoad(libraryPath, CacheKey.FromFile(libraryPath)));
    }

    [Fact]
    public void GetCacheFilePath_IsStableAndCaseInsensitive()
    {
        LibraryCache cache = CreateCache();

        Assert.Equal(cache.GetCacheFilePath(libraryPath), cache.GetCacheFilePath(libraryPath.ToUpperInvariant()));
        Assert.EndsWith(LibraryCache.FileExtension, cache.GetCacheFilePath(libraryPath), StringComparison.Ordinal);
        Assert.Contains("fixture", Path.GetFileName(cache.GetCacheFilePath(libraryPath)), StringComparison.Ordinal);
    }

    [Fact]
    public void Clear_RemovesEveryEntry()
    {
        LibraryCache cache = CreateCache();
        cache.Save(LibReader.Read(libraryPath), CacheKey.FromFile(libraryPath));

        Assert.True(cache.GetTotalSize() > 0);
        Assert.Equal(1, cache.Clear());
        Assert.Equal(0, cache.GetTotalSize());
        Assert.Null(cache.TryLoad(libraryPath, CacheKey.FromFile(libraryPath)));
    }

    [Fact]
    public void Save_PrunesOldEntriesBeyondTheLimit()
    {
        LibraryCache cache = CreateCache(maxEntries: 2);
        LibraryInfo library = LibReader.Read(libraryPath);

        for (int i = 0; i < 4; i++)
        {
            string copy = Path.Combine(workDirectory, $"copy{i}.lib");
            File.Copy(libraryPath, copy, overwrite: true);

            var forCopy = new LibraryInfo
            {
                FilePath = copy,
                FileSize = library.FileSize,
                Objects = library.Objects,
                Warnings = library.Warnings,
            };

            cache.Save(forCopy, CacheKey.FromFile(copy));
            Thread.Sleep(15); // 更新日時に差を付ける
        }

        Assert.Equal(2, Directory.GetFiles(cacheDirectory, "*" + LibraryCache.FileExtension).Length);
    }

    [Fact]
    public void Loader_UsesTheCacheOnTheSecondLoad()
    {
        var loader = new LibraryLoader(CreateCache());

        LibraryLoadResult first = loader.Load(libraryPath);
        Assert.False(first.FromCache);
        Assert.True(loader.SaveToCache(first));

        LibraryLoadResult second = loader.Load(libraryPath);

        Assert.True(second.FromCache);
        AssertSameLibrary(first.Library, second.Library);
    }

    [Fact]
    public void Loader_WithoutCacheAlwaysParses()
    {
        var loader = new LibraryLoader(CreateCache()) { UseCache = false };

        LibraryLoadResult first = loader.Load(libraryPath);
        Assert.False(loader.SaveToCache(first));
        Assert.False(loader.Load(libraryPath).FromCache);
    }

    [Fact]
    public void Loader_WithoutCacheDirectoryStillWorks()
    {
        var loader = new LibraryLoader();

        LibraryLoadResult result = loader.Load(libraryPath);

        Assert.False(result.FromCache);
        Assert.False(loader.SaveToCache(result));
        Assert.NotEmpty(result.Library.Objects);
    }

    private static void AssertSameLibrary(LibraryInfo expected, LibraryInfo actual)
    {
        Assert.Equal(expected.FilePath, actual.FilePath);
        Assert.Equal(expected.FileSize, actual.FileSize);
        Assert.Equal(expected.Warnings, actual.Warnings);
        Assert.Equal(expected.ObjectCount, actual.ObjectCount);
        Assert.Equal(expected.SectionCount, actual.SectionCount);
        Assert.Equal(expected.TotalSectionSize, actual.TotalSectionSize);
        Assert.Equal(expected.Machines, actual.Machines);

        foreach ((ObjectFileInfo left, ObjectFileInfo right) in expected.Objects.Zip(actual.Objects))
        {
            Assert.Equal(left.Name, right.Name);
            Assert.Equal(left.ShortName, right.ShortName);
            Assert.Equal(left.MemberSize, right.MemberSize);
            Assert.Equal(left.Kind, right.Kind);
            Assert.Equal(left.Machine, right.Machine);
            Assert.Equal(left.SymbolCount, right.SymbolCount);
            Assert.Equal(left.ImportDllName, right.ImportDllName);
            Assert.Equal(left.Warning, right.Warning);
            Assert.Equal(left.MetadataSize, right.MetadataSize);

            foreach ((SectionInfo a, SectionInfo b) in left.Sections.Zip(right.Sections))
            {
                Assert.Equal(a.Name, b.Name);
                Assert.Equal(a.GroupName, b.GroupName);
                Assert.Equal(a.Size, b.Size);
                Assert.Equal(a.RawDataSize, b.RawDataSize);
                Assert.Equal(a.VirtualSize, b.VirtualSize);
                Assert.Equal(a.Characteristics, b.Characteristics);
                Assert.Equal(a.RelocationCount, b.RelocationCount);
                Assert.Equal(a.IsUninitialized, b.IsUninitialized);
                Assert.Equal(a.IsComdat, b.IsComdat);
                Assert.Equal(a.IsSynthetic, b.IsSynthetic);
                Assert.Equal(a.Kind, b.Kind);
                Assert.Equal(a.Attributes, b.Attributes);
            }
        }
    }
}
