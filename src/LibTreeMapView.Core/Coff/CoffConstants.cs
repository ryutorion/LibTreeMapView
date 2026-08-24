namespace LibTreeMapView.Core.Coff;

/// <summary>COFF / アーカイブ形式の定数。値は winnt.h の IMAGE_* 定義に対応する。</summary>
internal static class CoffConstants
{
    public const string ArchiveMagic = "!<arch>\n";
    public const int ArchiveMagicLength = 8;
    public const int MemberHeaderSize = 60;

    public const int CoffFileHeaderSize = 20;
    public const int BigObjHeaderSize = 56;
    public const int ImportObjectHeaderSize = 20;
    public const int SectionHeaderSize = 40;
    public const int SymbolRecordSize = 18;
    public const int BigObjSymbolRecordSize = 20;
    public const int RelocationRecordSize = 10;

    // セクション属性 (IMAGE_SCN_*)
    public const uint ScnCntCode = 0x00000020;
    public const uint ScnCntInitializedData = 0x00000040;
    public const uint ScnCntUninitializedData = 0x00000080;
    public const uint ScnLnkInfo = 0x00000200;
    public const uint ScnLnkComdat = 0x00001000;
    public const uint ScnLnkNRelocOvfl = 0x01000000;
    public const uint ScnMemDiscardable = 0x02000000;
    public const uint ScnMemExecute = 0x20000000;
    public const uint ScnMemRead = 0x40000000;
    public const uint ScnMemWrite = 0x80000000;

    /// <summary>ANON_OBJECT_HEADER_BIGOBJ の ClassID。</summary>
    public static ReadOnlySpan<byte> BigObjClassId => new byte[]
    {
        0xC7, 0xA1, 0xBA, 0xD1, 0xEE, 0xBA, 0xA9, 0x4B,
        0xAF, 0x20, 0xFA, 0xF6, 0x6A, 0xA4, 0xDC, 0xB8,
    };

    public static string MachineName(ushort machine) => machine switch
    {
        0x0000 => "UNKNOWN",
        0x014C => "x86",
        0x0166 => "R4000",
        0x01A2 => "SH3",
        0x01C0 => "ARM",
        0x01C2 => "Thumb",
        0x01C4 => "ARMNT",
        0x01F0 => "PowerPC",
        0x0200 => "IA64",
        0x0266 => "MIPS16",
        0x5032 => "RISCV32",
        0x5064 => "RISCV64",
        0x6264 => "LOONGARCH64",
        0x8664 => "x64",
        0xAA64 => "ARM64",
        0xA641 => "ARM64EC",
        0xA64E => "ARM64X",
        0xC0EE => "CEE",
        _ => $"0x{machine:X4}",
    };
}
