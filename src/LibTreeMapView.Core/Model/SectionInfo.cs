using LibTreeMapView.Core.Coff;

namespace LibTreeMapView.Core.Model;

/// <summary>オブジェクトファイル内の 1 セクション。</summary>
public sealed class SectionInfo
{
    public required string Name { get; init; }

    /// <summary>'$' より前の部分。<c>.text$mn</c> なら <c>.text</c>。</summary>
    public required string GroupName { get; init; }

    /// <summary>SizeOfRawData。0 かつ VirtualSize &gt; 0 の場合は VirtualSize を採用する。</summary>
    public required long Size { get; init; }

    public required long RawDataSize { get; init; }

    public required long VirtualSize { get; init; }

    public required uint Characteristics { get; init; }

    /// <summary>再配置エントリ数。</summary>
    public required int RelocationCount { get; init; }

    /// <summary>COMDAT セクション (テンプレート実体や /Gy による関数単位のセクション)。</summary>
    public bool IsComdat => (Characteristics & CoffConstants.ScnLnkComdat) != 0;

    /// <summary>ファイル上に実体を持たない (.bss など)。</summary>
    public required bool IsUninitialized { get; init; }

    public required SectionKind Kind { get; init; }

    /// <summary>実際のセクションヘッダーではなく、解析結果を表現するために合成したエントリ。</summary>
    public bool IsSynthetic { get; init; }

    /// <summary>再配置レコードが占めるバイト数。</summary>
    public long RelocationBytes => (long)RelocationCount * CoffConstants.RelocationRecordSize;

    /// <summary>rwx 形式のアクセス属性表記。</summary>
    public string Attributes
    {
        get
        {
            Span<char> buffer = stackalloc char[4];
            buffer[0] = (Characteristics & CoffConstants.ScnMemRead) != 0 ? 'r' : '-';
            buffer[1] = (Characteristics & CoffConstants.ScnMemWrite) != 0 ? 'w' : '-';
            buffer[2] = (Characteristics & CoffConstants.ScnMemExecute) != 0 ? 'x' : '-';
            buffer[3] = (Characteristics & CoffConstants.ScnMemDiscardable) != 0 ? 'd' : '-';
            return new string(buffer);
        }
    }
}
