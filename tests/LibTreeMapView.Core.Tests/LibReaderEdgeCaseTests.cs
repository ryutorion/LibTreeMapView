using LibTreeMapView.Core.Coff;
using LibTreeMapView.Core.Model;

namespace LibTreeMapView.Core.Tests;

/// <summary>
/// 実物のライブラリでは作りにくい入力に対する解析の挙動。
/// TestData/special.lib は epsilon.cpp (cl /c /bigobj) と zeta.cpp (cl /c /GL) を lib.exe でまとめたもの。
/// </summary>
public class LibReaderEdgeCaseTests
{
    /// <summary>ANON_OBJECT_HEADER_BIGOBJ の ClassID。</summary>
    private static ReadOnlySpan<byte> BigObjClassId =>
    [
        0xC7, 0xA1, 0xBA, 0xD1, 0xEE, 0xBA, 0xA9, 0x4B,
        0xAF, 0x20, 0xFA, 0xF6, 0x6A, 0xA4, 0xDC, 0xB8,
    ];

    private static LibraryInfo ReadSpecial() =>
        LibReader.Read(Path.Combine(AppContext.BaseDirectory, "TestData", "special.lib"));

    [Fact]
    public void Read_ParsesBigObjSections()
    {
        ObjectFileInfo epsilon = ReadSpecial().Objects.Single(o => o.ShortName == "epsilon.obj");

        Assert.Equal(ObjectFileKind.BigObj, epsilon.Kind);
        Assert.Equal("x64", epsilon.MachineName);
        Assert.Equal(5, epsilon.Sections.Count);
        Assert.Equal(0x48, epsilon.Sections.Single(s => s.Name == ".text$mn").Size);
        Assert.Equal(0x80, epsilon.Sections.Single(s => s.Name == ".rdata").Size);
        Assert.True(epsilon.SymbolCount > 0);
    }

    [Fact]
    public void Read_LtcgObjectBecomesOneOpaqueBlock()
    {
        ObjectFileInfo zeta = ReadSpecial().Objects.Single(o => o.ShortName == "zeta.obj");

        // /GL のオブジェクトは匿名オブジェクトなのでセクションに分解できない。
        Assert.Equal(ObjectFileKind.Anonymous, zeta.Kind);
        SectionInfo section = zeta.Sections.Single();
        Assert.True(section.IsSynthetic);
        Assert.Equal(zeta.MemberSize, section.Size);
    }

    [Fact]
    public void Read_ResolvesGnuStyleBase64SectionName()
    {
        // "//E" は base64 でオフセット 4 = 文字列テーブルの最初のエントリ。
        byte[] data = TestArchive.Archive(
            ("gnu.obj/", TestArchive.SingleSectionObject("//E", stringTableName: ".averylongsectionname")));

        SectionInfo section = LibReader.Read(data, "gnu.lib").Objects.Single().Sections.Single();

        Assert.Equal(".averylongsectionname", section.Name);
        Assert.Equal(".averylongsectionname", section.GroupName);
    }

    [Fact]
    public void Read_ResolvesDecimalStringTableSectionName()
    {
        byte[] data = TestArchive.Archive(
            ("long.obj/", TestArchive.SingleSectionObject("/4", stringTableName: ".averylongsectionname")));

        Assert.Equal(
            ".averylongsectionname",
            LibReader.Read(data, "long.lib").Objects.Single().Sections.Single().Name);
    }

    [Fact]
    public void Read_EmptyBase64SectionNameFallsBackToTheRawField()
    {
        // "//" だけではオフセットを決められないので、文字列テーブルは引かない。
        byte[] data = TestArchive.Archive(
            ("odd.obj/", TestArchive.SingleSectionObject("//", stringTableName: ".ignored")));

        Assert.Equal("//", LibReader.Read(data, "odd.lib").Objects.Single().Sections.Single().Name);
    }

    [Fact]
    public void Read_OutOfRangeRelocationOverflowPointerDoesNotBreakTheMember()
    {
        // 壊れた (あるいは細工された) ファイル: ポインタが 32 ビットの端にある。
        byte[] data = TestArchive.Archive(
            ("bad.obj/", TestArchive.SingleSectionObject(
                ".text",
                characteristics: TestArchive.CodeSection | TestArchive.RelocationOverflow,
                relocationCount: 0xFFFF,
                pointerToRelocations: 0xFFFFFFFE)));

        LibraryInfo library = LibReader.Read(data, "bad.lib");

        Assert.Empty(library.Warnings);
        SectionInfo section = library.Objects.Single().Sections.Single();
        Assert.Equal(0x100, section.Size);
        Assert.Equal(0xFFFF, section.RelocationCount); // 実数は読めないのでヘッダーの値のまま
    }

    [Fact]
    public void Read_ReadsRelocationCountFromOverflowRecord()
    {
        byte[] data = TestArchive.Archive(
            ("many.obj/", TestArchive.SingleSectionObject(
                ".text",
                characteristics: TestArchive.CodeSection | TestArchive.RelocationOverflow,
                relocationCount: 0xFFFF,
                overflowRelocationCount: 70_000)));

        SectionInfo section = LibReader.Read(data, "many.lib").Objects.Single().Sections.Single();

        Assert.Equal(70_000, section.RelocationCount);
        Assert.Equal(700_000, section.RelocationBytes);
    }

    [Fact]
    public void Read_ElfMembersAreReportedInsteadOfParsedAsCoff()
    {
        // GNU ar のライブラリも署名は同じなので、メンバー側で見分ける必要がある。
        byte[] data = TestArchive.Archive(
            ("elf1.o/", TestArchive.ElfObject()),
            ("elf2.o/", TestArchive.ElfObject()));

        LibraryInfo library = LibReader.Read(data, "libfoo.a");

        Assert.All(library.Objects, o => Assert.Equal(ObjectFileKind.Unknown, o.Kind));
        Assert.All(library.Objects, o => Assert.Empty(o.Sections));
        Assert.Contains(library.Warnings, w => w.Contains("ELF") && w.Contains("2"));
    }

    [Fact]
    public void Read_TreatsBigObjSignatureWithWrongClassIdAsAnonymous()
    {
        byte[] data = TestArchive.Archive(
            ("weird.obj/", TestArchive.AnonymousObject(version: 2, classId: new byte[16])));

        Assert.Equal(ObjectFileKind.Anonymous, LibReader.Read(data, "weird.lib").Objects.Single().Kind);
    }

    [Fact]
    public void Read_AcceptsBigObjSignatureWithTheRealClassId()
    {
        byte[] data = TestArchive.Archive(
            ("big.obj/", TestArchive.AnonymousObject(version: 2, classId: BigObjClassId)));

        ObjectFileInfo obj = LibReader.Read(data, "big.lib").Objects.Single();

        Assert.Equal(ObjectFileKind.BigObj, obj.Kind);
        Assert.Empty(obj.Sections); // NumberOfSections が 0 のヘッダーだけの入力
    }
}
