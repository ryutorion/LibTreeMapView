namespace LibTreeMapView.Core.Model;

/// <summary>アーカイブメンバーの種類。</summary>
public enum ObjectFileKind
{
    /// <summary>通常の COFF オブジェクト。</summary>
    Coff,

    /// <summary>/bigobj で生成された COFF オブジェクト。</summary>
    BigObj,

    /// <summary>インポートライブラリのインポート記述子。</summary>
    Import,

    /// <summary>/GL (LTCG) などによる匿名オブジェクト。セクション情報を持たない。</summary>
    Anonymous,

    /// <summary>解析できなかったメンバー。</summary>
    Unknown,
}

/// <summary>ライブラリに含まれるオブジェクトファイル (アーカイブメンバー) 1 件。</summary>
public sealed class ObjectFileInfo
{
    /// <summary>アーカイブに記録されたメンバー名。フルパスの場合もある。</summary>
    public required string Name { get; init; }

    /// <summary>ディレクトリを除いたファイル名。</summary>
    public required string ShortName { get; init; }

    /// <summary>アーカイブ内でこのメンバーが占めるバイト数 (ヘッダーを除く)。</summary>
    public required long MemberSize { get; init; }

    public required ObjectFileKind Kind { get; init; }

    public required ushort Machine { get; init; }

    public string MachineName => Coff.CoffConstants.MachineName(Machine);

    public required int SymbolCount { get; init; }

    public required IReadOnlyList<SectionInfo> Sections { get; init; }

    /// <summary>インポート記述子の場合の DLL 名。</summary>
    public string? ImportDllName { get; init; }

    /// <summary>解析中に見つかった問題 (壊れたヘッダーなど)。</summary>
    public string? Warning { get; init; }

    /// <summary>全セクションのサイズ合計。</summary>
    public long TotalSectionSize => Sections.Sum(s => s.Size);

    /// <summary>メンバーサイズのうちセクション実体以外が占める分 (ヘッダー、シンボル、再配置、文字列テーブル)。</summary>
    public long MetadataSize
    {
        get
        {
            long payload = Sections.Where(s => !s.IsUninitialized).Sum(s => s.RawDataSize);
            return Math.Max(0, MemberSize - payload);
        }
    }
}
