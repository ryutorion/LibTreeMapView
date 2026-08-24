using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace LibTreeMapView.Core.Tests;

/// <summary>
/// 実物のライブラリでは再現しづらいケース (壊れたヘッダー、GNU 形式のセクション名など) を
/// 作るための最小限のアーカイブ／COFF 組み立て。
/// </summary>
internal static class TestArchive
{
    public const uint CodeSection = 0x60000020;   // CNT_CODE | MEM_EXECUTE | MEM_READ
    public const uint RelocationOverflow = 0x01000000; // IMAGE_SCN_LNK_NRELOC_OVFL

    /// <summary>メンバーを並べた COFF アーカイブを作る。</summary>
    public static byte[] Archive(params (string Name, byte[] Body)[] members)
    {
        using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes("!<arch>\n"));

        foreach ((string name, byte[] body) in members)
        {
            WriteMemberHeader(stream, name, body.Length);
            stream.Write(body);

            if ((stream.Length & 1) != 0)
            {
                stream.WriteByte((byte)'\n'); // メンバーは 2 バイト境界に整列する
            }
        }

        return stream.ToArray();
    }

    /// <summary>セクション 1 つだけを持つ最小の COFF オブジェクト。</summary>
    public static byte[] SingleSectionObject(
        string sectionNameField,
        string? stringTableName = null,
        uint characteristics = CodeSection,
        uint sizeOfRawData = 0x100,
        ushort relocationCount = 0,
        uint pointerToRelocations = 0,
        uint? overflowRelocationCount = null)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sectionNameField.Length, 8);

        byte[] names = stringTableName is null
            ? []
            : [.. Encoding.ASCII.GetBytes(stringTableName), (byte)0];

        // シンボル 0 個なので、シンボルテーブルの位置がそのまま文字列テーブルの先頭になる。
        const int StringTableOffset = 60;
        int stringTableSize = 4 + names.Length;
        int overflowOffset = StringTableOffset + stringTableSize;

        var buffer = new byte[overflowOffset + (overflowRelocationCount.HasValue ? 4 : 0)];
        Span<byte> span = buffer;

        // COFF ファイルヘッダー
        BinaryPrimitives.WriteUInt16LittleEndian(span, 0x8664);
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], StringTableOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], 0);

        // セクションヘッダー
        Span<byte> section = span.Slice(20, 40);
        Encoding.ASCII.GetBytes(sectionNameField, section[..8]);
        BinaryPrimitives.WriteUInt32LittleEndian(section[16..], sizeOfRawData);
        BinaryPrimitives.WriteUInt32LittleEndian(section[20..], 0x1000); // PointerToRawData
        BinaryPrimitives.WriteUInt32LittleEndian(
            section[24..],
            overflowRelocationCount.HasValue ? (uint)overflowOffset : pointerToRelocations);
        BinaryPrimitives.WriteUInt16LittleEndian(section[32..], relocationCount);
        BinaryPrimitives.WriteUInt32LittleEndian(section[36..], characteristics);

        // 文字列テーブル
        BinaryPrimitives.WriteInt32LittleEndian(span[StringTableOffset..], stringTableSize);
        names.CopyTo(buffer, StringTableOffset + 4);

        if (overflowRelocationCount is { } count)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[overflowOffset..], count);
        }

        return buffer;
    }

    /// <summary>署名だけ bigobj に似せた匿名オブジェクト。ClassID は呼び出し側が指定する。</summary>
    public static byte[] AnonymousObject(ushort version, ReadOnlySpan<byte> classId)
    {
        var buffer = new byte[56];
        Span<byte> span = buffer;

        BinaryPrimitives.WriteUInt16LittleEndian(span, 0);          // Sig1
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 0xFFFF); // Sig2
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], version);
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], 0x8664); // Machine
        classId.CopyTo(span.Slice(12, Math.Min(16, classId.Length)));

        return buffer;
    }

    /// <summary>GNU ar が入れる ELF オブジェクトの先頭だけを真似たもの。</summary>
    public static byte[] ElfObject()
    {
        var buffer = new byte[64];
        buffer[0] = 0x7F;
        buffer[1] = (byte)'E';
        buffer[2] = (byte)'L';
        buffer[3] = (byte)'F';
        buffer[4] = 2; // 64 ビット
        buffer[5] = 1; // リトルエンディアン
        return buffer;
    }

    private static void WriteMemberHeader(Stream stream, string name, int size)
    {
        Span<byte> header = stackalloc byte[60];
        header.Fill((byte)' ');

        Encoding.ASCII.GetBytes(name, header[..16]);
        Encoding.ASCII.GetBytes("0", header.Slice(16, 12));  // 更新日時
        Encoding.ASCII.GetBytes("100666", header.Slice(40, 8)); // モード
        Encoding.ASCII.GetBytes(size.ToString(CultureInfo.InvariantCulture), header.Slice(48, 10));
        header[58] = (byte)'`';
        header[59] = (byte)'\n';

        stream.Write(header);
    }
}
