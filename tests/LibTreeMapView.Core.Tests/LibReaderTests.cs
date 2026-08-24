using System.Text;
using LibTreeMapView.Core.Coff;
using LibTreeMapView.Core.Model;

namespace LibTreeMapView.Core.Tests;

/// <summary>
/// TestData/fixture.lib は alpha.cpp / gamma.cpp (cl /c /EHsc /Zi /GS-) と
/// delta.cpp (cl /c /EHsc /Gy /GS-) を lib.exe でまとめたもの。
/// 期待値は dumpbin /headers の出力から取っている。
/// .debug$S にはビルドしたディレクトリの絶対パスが入るため、フィクスチャを作り直したときは
/// そのサイズの期待値も更新すること。
/// </summary>
public class LibReaderTests
{
    private static string TestDataPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    private static LibraryInfo ReadFixture() => LibReader.Read(TestDataPath("fixture.lib"));

    private static ObjectFileInfo FindObject(LibraryInfo library, string shortName) =>
        library.Objects.Single(o => o.ShortName.Equals(shortName, StringComparison.OrdinalIgnoreCase));

    private static SectionInfo FindSection(ObjectFileInfo obj, string name) =>
        obj.Sections.Single(s => s.Name == name);

    [Fact]
    public void Read_ReportsFileSizeAndMembers()
    {
        LibraryInfo library = ReadFixture();

        Assert.Equal(new FileInfo(TestDataPath("fixture.lib")).Length, library.FileSize);
        Assert.Empty(library.Warnings);

        // 2 つのオブジェクトに加えてリンカーメンバー (とロングネームテーブル) が含まれる。
        Assert.Equal(3, library.Objects.Count(o => o.Kind == ObjectFileKind.Coff));
        Assert.Contains(library.Objects, o => o.Name.Contains("シンボルテーブル"));
    }

    [Fact]
    public void Read_ParsesSectionSizesFromCoffHeaders()
    {
        ObjectFileInfo alpha = FindObject(ReadFixture(), "alpha.obj");

        Assert.Equal(10, alpha.Sections.Count);
        Assert.Equal(0x400, FindSection(alpha, ".bss").Size);
        Assert.Equal(0x108, FindSection(alpha, ".data").Size);
        Assert.Equal(0x72, FindSection(alpha, ".text$mn").Size);
        Assert.Equal(0xC, FindSection(alpha, ".pdata").Size);
        Assert.Equal(0x8, FindSection(alpha, ".xdata").Size);
        Assert.Equal(0xA50, FindSection(alpha, ".debug$S").Size);
    }

    [Fact]
    public void Read_ResolvesLongSectionNamesFromStringTable()
    {
        ObjectFileInfo gamma = FindObject(ReadFixture(), "gamma.obj");

        // 8 文字を超える名前はセクションヘッダーに "/99" として入り、文字列テーブルを引く必要がある。
        SectionInfo section = FindSection(gamma, ".averylongsectionname");
        Assert.Equal(0x40, section.Size);
        Assert.Equal(".averylongsectionname", section.GroupName);
    }

    [Fact]
    public void Read_MarksBssAsUninitialized()
    {
        SectionInfo bss = FindSection(FindObject(ReadFixture(), "alpha.obj"), ".bss");

        Assert.True(bss.IsUninitialized);
        Assert.Equal(0, bss.RawDataSize); // ファイル上に実体はない
        Assert.Equal(SectionKind.UninitializedData, bss.Kind);
    }

    [Fact]
    public void Read_ClassifiesSectionsAndGroupNames()
    {
        ObjectFileInfo alpha = FindObject(ReadFixture(), "alpha.obj");

        Assert.Equal(SectionKind.Code, FindSection(alpha, ".text$mn").Kind);
        Assert.Equal(".text", FindSection(alpha, ".text$mn").GroupName);
        Assert.Equal(SectionKind.Debug, FindSection(alpha, ".debug$S").Kind);
        Assert.Equal(".debug", FindSection(alpha, ".debug$S").GroupName);
        Assert.Equal(SectionKind.ExceptionHandling, FindSection(alpha, ".pdata").Kind);
        Assert.Equal(SectionKind.Directive, FindSection(alpha, ".drectve").Kind);
        Assert.Equal(SectionKind.Data, FindSection(alpha, ".data").Kind);
        Assert.Equal(SectionKind.ReadOnlyData, FindSection(alpha, ".rdata").Kind);
    }

    [Fact]
    public void Read_DetectsMachine()
    {
        ObjectFileInfo alpha = FindObject(ReadFixture(), "alpha.obj");

        Assert.Equal(0x8664, alpha.Machine);
        Assert.Equal("x64", alpha.MachineName);
        Assert.True(FindSection(alpha, ".text$mn").Attributes.Contains('x'));
    }

    [Fact]
    public void Read_DetectsComdatSections()
    {
        // delta.cpp は /Gy でコンパイルしてあり、関数ごとに COMDAT の .text$mn を持つ。
        ObjectFileInfo delta = FindObject(ReadFixture(), "delta.obj");

        List<SectionInfo> text = delta.Sections.Where(s => s.Name == ".text$mn").ToList();
        Assert.Equal(3, text.Count);
        Assert.All(text, s => Assert.True(s.IsComdat));
        Assert.Equal(0xB + 0xC + 0xC, text.Sum(s => s.Size));
        Assert.False(FindSection(delta, ".drectve").IsComdat);
    }

    [Fact]
    public void Read_MemberSizeCoversSectionPayload()
    {
        ObjectFileInfo alpha = FindObject(ReadFixture(), "alpha.obj");

        long payload = alpha.Sections.Where(s => !s.IsUninitialized).Sum(s => s.RawDataSize);
        Assert.True(payload <= alpha.MemberSize);
        Assert.Equal(alpha.MemberSize - payload, alpha.MetadataSize);
    }

    [Fact]
    public void Read_ParsesImportLibrary()
    {
        LibraryInfo library = LibReader.Read(TestDataPath("import.lib"));

        List<ObjectFileInfo> imports = library.Objects.Where(o => o.Kind == ObjectFileKind.Import).ToList();
        Assert.Equal(3, imports.Count);
        Assert.All(imports, o => Assert.Equal("sampledll.dll", o.ImportDllName, ignoreCase: true));
        Assert.Contains(imports, o => o.Name.Contains("sample_alpha"));
        Assert.All(imports, o => Assert.Equal(SectionKind.Import, o.Sections.Single().Kind));
    }

    [Fact]
    public void Read_ThrowsForNonArchiveData()
    {
        byte[] data = Encoding.ASCII.GetBytes("MZ this is not an archive at all");

        LibFormatException ex = Assert.Throws<LibFormatException>(() => LibReader.Read(data, "bogus.lib"));
        Assert.Contains("署名", ex.Message);
    }

    [Fact]
    public void Read_TruncatedMemberProducesWarningInsteadOfThrowing()
    {
        byte[] data = File.ReadAllBytes(TestDataPath("fixture.lib"));
        byte[] truncated = data[..(data.Length / 2)];

        LibraryInfo library = LibReader.Read(truncated, "truncated.lib");

        Assert.NotEmpty(library.Warnings);
        Assert.NotEmpty(library.Objects);
    }

    [Fact]
    public void Read_ThrowsFileNotFoundForMissingPath()
    {
        Assert.Throws<FileNotFoundException>(() => LibReader.Read(TestDataPath("does-not-exist.lib")));
    }
}
