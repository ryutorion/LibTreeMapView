using LibTreeMapView.Core.Coff;
using LibTreeMapView.Core.Comparison;
using LibTreeMapView.Core.Model;

namespace LibTreeMapView.Core.Tests;

public class LibraryComparerTests
{
    private static SectionInfo Section(
        string name,
        long size,
        SectionKind kind = SectionKind.Code,
        bool uninitialized = false) => new()
        {
            Name = name,
            GroupName = SectionClassifier.GetGroupName(name),
            Size = size,
            RawDataSize = uninitialized ? 0 : size,
            VirtualSize = 0,
            Characteristics = 0,
            RelocationCount = 0,
            IsUninitialized = uninitialized,
            Kind = kind,
        };

    private static ObjectFileInfo Object(string name, params SectionInfo[] sections) => new()
    {
        Name = name,
        ShortName = name,
        MemberSize = sections.Sum(s => s.Size) + 100, // 100 バイト分がメタデータ相当
        Kind = ObjectFileKind.Coff,
        Machine = 0x8664,
        SymbolCount = 1,
        Sections = sections,
    };

    private static LibraryInfo Library(string fileName, params ObjectFileInfo[] objects) => new()
    {
        FilePath = $@"C:\libs\{fileName}",
        FileSize = objects.Sum(o => o.MemberSize),
        Objects = objects,
        Warnings = [],
    };

    private static ObjectDiff Find(LibraryDiff diff, string name) =>
        diff.Objects.Single(o => o.Name == name);

    [Fact]
    public void Compare_IdenticalLibrariesHaveNoDifferences()
    {
        LibraryInfo a = Library("a.lib", Object("alpha.obj", Section(".text$mn", 100), Section(".data", 40)));
        LibraryInfo b = Library("b.lib", Object("alpha.obj", Section(".text$mn", 100), Section(".data", 40)));

        LibraryDiff diff = LibraryComparer.Compare(a, b);

        Assert.Equal(0, diff.Delta);
        Assert.Equal(0, diff.ChangedObjectCount);
        Assert.Equal(1, diff.UnchangedCount);
        Assert.All(diff.Objects, o => Assert.Equal(DiffStatus.Unchanged, o.Status));
        Assert.All(diff.Objects.SelectMany(o => o.Sections), s => Assert.False(s.IsChanged));
    }

    [Fact]
    public void Compare_DetectsAddedAndRemovedObjects()
    {
        LibraryInfo a = Library("a.lib", Object("kept.obj", Section(".text$mn", 50)), Object("gone.obj", Section(".text$mn", 30)));
        LibraryInfo b = Library("b.lib", Object("kept.obj", Section(".text$mn", 50)), Object("new.obj", Section(".text$mn", 70)));

        LibraryDiff diff = LibraryComparer.Compare(a, b);

        Assert.Equal(DiffStatus.Removed, Find(diff, "gone.obj").Status);
        Assert.Equal(-30, Find(diff, "gone.obj").Delta);
        Assert.Equal(DiffStatus.Added, Find(diff, "new.obj").Status);
        Assert.Equal(70, Find(diff, "new.obj").Delta);
        Assert.Equal(DiffStatus.Unchanged, Find(diff, "kept.obj").Status);

        Assert.Equal(1, diff.AddedCount);
        Assert.Equal(1, diff.RemovedCount);
        Assert.Equal(1, diff.UnchangedCount);
        Assert.Equal(40, diff.Delta);
    }

    [Fact]
    public void Compare_DetectsSectionSizeChanges()
    {
        LibraryInfo a = Library("a.lib", Object("alpha.obj", Section(".text$mn", 100), Section(".rdata", 20)));
        LibraryInfo b = Library("b.lib", Object("alpha.obj", Section(".text$mn", 180), Section(".rdata", 20)));

        ObjectDiff alpha = Find(LibraryComparer.Compare(a, b), "alpha.obj");

        Assert.Equal(DiffStatus.Changed, alpha.Status);
        Assert.Equal(80, alpha.Delta);
        Assert.Equal(1, alpha.ChangedSectionCount);

        SectionDiff text = alpha.Sections.Single(s => s.Name == ".text$mn");
        Assert.Equal(DiffStatus.Changed, text.Status);
        Assert.Equal(100, text.BaselineSize);
        Assert.Equal(180, text.TargetSize);
        Assert.Equal(80, text.Delta);

        Assert.False(alpha.Sections.Single(s => s.Name == ".rdata").IsChanged);
    }

    [Fact]
    public void Compare_DetectsAddedAndRemovedSections()
    {
        LibraryInfo a = Library("a.lib", Object("alpha.obj", Section(".text$mn", 100), Section(".gone", 10)));
        LibraryInfo b = Library("b.lib", Object("alpha.obj", Section(".text$mn", 100), Section(".fresh", 25)));

        ObjectDiff alpha = Find(LibraryComparer.Compare(a, b), "alpha.obj");

        Assert.Equal(DiffStatus.Removed, alpha.Sections.Single(s => s.Name == ".gone").Status);
        Assert.Equal(DiffStatus.Added, alpha.Sections.Single(s => s.Name == ".fresh").Status);
        Assert.Equal(2, alpha.ChangedSectionCount);
    }

    [Fact]
    public void Compare_TreatsMovedBytesAsChangedEvenWhenTheTotalMatches()
    {
        LibraryInfo a = Library("a.lib", Object("alpha.obj", Section(".text$mn", 100), Section(".data", 100)));
        LibraryInfo b = Library("b.lib", Object("alpha.obj", Section(".text$mn", 150), Section(".data", 50)));

        ObjectDiff alpha = Find(LibraryComparer.Compare(a, b), "alpha.obj");

        Assert.Equal(0, alpha.Delta);
        Assert.Equal(DiffStatus.Changed, alpha.Status);
        Assert.Equal(2, alpha.ChangedSectionCount);
    }

    [Fact]
    public void Compare_AggregatesDuplicateNames()
    {
        // COMDAT で同じセクション名が複数あるオブジェクトと、同名メンバーが 2 つあるライブラリ。
        LibraryInfo a = Library(
            "a.lib",
            Object("dup.obj", Section(".text$mn", 10), Section(".text$mn", 20)),
            Object("dup.obj", Section(".text$mn", 30)));
        LibraryInfo b = Library("b.lib", Object("dup.obj", Section(".text$mn", 100)));

        LibraryDiff diff = LibraryComparer.Compare(a, b);

        ObjectDiff dup = Assert.Single(diff.Objects);
        SectionDiff text = Assert.Single(dup.Sections);
        Assert.Equal(60, text.BaselineSize);
        Assert.Equal(100, text.TargetSize);
        Assert.Equal(40, dup.Delta);
    }

    [Fact]
    public void Compare_MatchesObjectNamesCaseInsensitively()
    {
        LibraryInfo a = Library("a.lib", Object("Alpha.obj", Section(".text$mn", 100)));
        LibraryInfo b = Library("b.lib", Object("alpha.obj", Section(".text$mn", 120)));

        LibraryDiff diff = LibraryComparer.Compare(a, b);

        ObjectDiff alpha = Assert.Single(diff.Objects);
        Assert.Equal(DiffStatus.Changed, alpha.Status);
        Assert.Equal(20, alpha.Delta);
    }

    [Fact]
    public void Compare_SortsObjectsByAbsoluteDelta()
    {
        LibraryInfo a = Library(
            "a.lib",
            Object("small.obj", Section(".text$mn", 100)),
            Object("big.obj", Section(".text$mn", 100)),
            Object("shrunk.obj", Section(".text$mn", 500)));
        LibraryInfo b = Library(
            "b.lib",
            Object("small.obj", Section(".text$mn", 110)),
            Object("big.obj", Section(".text$mn", 300)),
            Object("shrunk.obj", Section(".text$mn", 100)));

        List<string> order = LibraryComparer.Compare(a, b).Objects.Select(o => o.Name).ToList();

        Assert.Equal(["shrunk.obj", "big.obj", "small.obj"], order);
    }

    [Fact]
    public void Compare_SortsSectionsByAbsoluteDelta()
    {
        LibraryInfo a = Library("a.lib", Object("alpha.obj", Section(".text$mn", 100), Section(".data", 100), Section(".rdata", 10)));
        LibraryInfo b = Library("b.lib", Object("alpha.obj", Section(".text$mn", 120), Section(".data", 400), Section(".rdata", 10)));

        List<string> order = Find(LibraryComparer.Compare(a, b), "alpha.obj").Sections.Select(s => s.Name).ToList();

        Assert.Equal([".data", ".text$mn", ".rdata"], order);
    }

    [Fact]
    public void Compare_CanExcludeUninitializedSections()
    {
        LibraryInfo a = Library("a.lib", Object("alpha.obj", Section(".text$mn", 100), Section(".bss", 1024, SectionKind.UninitializedData, uninitialized: true)));
        LibraryInfo b = Library("b.lib", Object("alpha.obj", Section(".text$mn", 100), Section(".bss", 2048, SectionKind.UninitializedData, uninitialized: true)));

        LibraryDiff withBss = LibraryComparer.Compare(a, b);
        LibraryDiff withoutBss = LibraryComparer.Compare(a, b, new LibraryCompareOptions { IncludeUninitialized = false });

        Assert.Equal(1024, withBss.Delta);
        Assert.Equal(DiffStatus.Changed, Find(withBss, "alpha.obj").Status);

        Assert.Equal(0, withoutBss.Delta);
        Assert.Equal(DiffStatus.Unchanged, Find(withoutBss, "alpha.obj").Status);
        Assert.DoesNotContain(Find(withoutBss, "alpha.obj").Sections, s => s.Name == ".bss");
    }

    [Fact]
    public void Compare_SkipsObjectsWithNothingToCompare()
    {
        // 比較対象のセクションを持たないメンバー (アーカイブのリンカーメンバー相当)。
        LibraryInfo a = Library("a.lib", Object("empty.obj"), Object("alpha.obj", Section(".text$mn", 10)));
        LibraryInfo b = Library("b.lib", Object("empty.obj"), Object("alpha.obj", Section(".text$mn", 10)));

        LibraryDiff diff = LibraryComparer.Compare(a, b);

        Assert.DoesNotContain(diff.Objects, o => o.Name == "empty.obj");
        Assert.Single(diff.Objects);
    }

    [Fact]
    public void Compare_CanIncludeArchiveMetadata()
    {
        LibraryInfo a = Library("a.lib", Object("alpha.obj", Section(".text$mn", 100)));
        LibraryInfo b = Library("b.lib", Object("alpha.obj", Section(".text$mn", 100)));

        LibraryDiff diff = LibraryComparer.Compare(a, b, new LibraryCompareOptions { IncludeMetadata = true });

        // MemberSize はセクション合計 + 100 バイトにしてあるので、メタデータが 100 バイト分現れる。
        SectionDiff metadata = Find(diff, "alpha.obj").Sections.Single(s => s.Name == LibraryComparer.MetadataSectionName);
        Assert.Equal(100, metadata.BaselineSize);
        Assert.Equal(100, metadata.TargetSize);
        Assert.False(metadata.IsChanged);
    }

    [Fact]
    public void Compare_RealLibraries()
    {
        LibraryInfo fixture = LibReader.Read(Path.Combine(AppContext.BaseDirectory, "TestData", "fixture.lib"));
        LibraryInfo special = LibReader.Read(Path.Combine(AppContext.BaseDirectory, "TestData", "special.lib"));

        LibraryDiff same = LibraryComparer.Compare(fixture, fixture);
        Assert.Equal(0, same.ChangedObjectCount);
        Assert.Equal(0, same.Delta);

        LibraryDiff different = LibraryComparer.Compare(fixture, special);

        // 共通のオブジェクトは 1 つも無い (リンカーメンバーは比較対象のセクションを持たないので並ばない)。
        Assert.Equal(0, different.UnchangedCount);
        Assert.DoesNotContain(different.Objects, o => o.Name.Contains("リンカーメンバー"));
        Assert.Contains(different.Objects, o => o.Name == "alpha.obj" && o.Status == DiffStatus.Removed);
        Assert.Contains(different.Objects, o => o.Name == "epsilon.obj" && o.Status == DiffStatus.Added);
        Assert.Equal(different.TargetSize - different.BaselineSize, different.Delta);
    }
}
