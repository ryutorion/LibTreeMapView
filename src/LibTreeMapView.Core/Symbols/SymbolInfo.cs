using LibTreeMapView.Core.Model;

namespace LibTreeMapView.Core.Symbols;

/// <summary>シンボルの種類。</summary>
public enum SymbolKind
{
    /// <summary>関数。</summary>
    Function,

    /// <summary>変数・定数などのデータ。</summary>
    Data,

    /// <summary>判別できなかったもの。</summary>
    Other,
}

/// <summary>サイズをどこから求めたか。</summary>
public enum SymbolSizeSource
{
    /// <summary>COMDAT セクションなので、セクションのサイズ = シンボルのサイズ。</summary>
    Comdat,

    /// <summary>同じセクション内の次のシンボルとの距離から求めた概算。</summary>
    SectionRange,

    /// <summary>PDB の関数レコード (S_GPROC32) にあった正確なコードサイズ。</summary>
    Pdb,
}

/// <summary>1 つのシンボル。</summary>
public sealed record SymbolInfo
{
    /// <summary>マングルされたままの名前。</summary>
    public required string MangledName { get; init; }

    /// <summary>デマングルした完全な表記 (戻り値や引数を含む)。</summary>
    public required string DisplayName { get; init; }

    /// <summary>名前空間付きの名前 (引数や戻り値を除く)。</summary>
    public required string QualifiedName { get; init; }

    /// <summary>名前空間・クラスの階層。グローバルなら空。</summary>
    public required IReadOnlyList<string> NamespacePath { get; init; }

    /// <summary>階層を除いた名前。</summary>
    public required string LeafName { get; init; }

    /// <summary>このシンボルを含むオブジェクトファイル名。</summary>
    public required string ObjectName { get; init; }

    public required string SectionName { get; init; }

    public required SectionKind SectionKind { get; init; }

    /// <summary>セクション内のオフセット。</summary>
    public required long Offset { get; init; }

    public required long Size { get; init; }

    public required SymbolKind Kind { get; init; }

    /// <summary>COMDAT セクションに置かれている (テンプレート実体や /Gy の関数など)。</summary>
    public required bool IsComdat { get; init; }

    /// <summary>内部リンケージ (static)。</summary>
    public required bool IsStatic { get; init; }

    public required SymbolSizeSource SizeSource { get; init; }

    /// <summary>名前空間の表示用文字列。</summary>
    public string NamespaceText => NamespacePath.Count == 0 ? "(グローバル)" : string.Join("::", NamespacePath);
}

/// <summary>PDB を探した結果。</summary>
public enum PdbStatus
{
    /// <summary>同じディレクトリに PDB が無かった。</summary>
    NotFound,

    /// <summary>PDB はあったが、型情報だけでシンボルを持っていなかった (cl /Zi が作る vcXXX.pdb など)。</summary>
    NoSymbols,

    /// <summary>PDB のシンボルを取り込んだ。</summary>
    Used,

    /// <summary>PDB を読もうとして失敗した。</summary>
    Failed,
}

/// <summary>ライブラリ 1 つ分のシンボル一覧。</summary>
public sealed class SymbolIndex
{
    public static readonly SymbolIndex Empty = new()
    {
        LibraryPath = string.Empty,
        Symbols = [],
        Warnings = [],
        PdbStatus = PdbStatus.NotFound,
    };

    public required string LibraryPath { get; init; }

    public required IReadOnlyList<SymbolInfo> Symbols { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public required PdbStatus PdbStatus { get; init; }

    /// <summary>参照した PDB のパス。</summary>
    public string? PdbPath { get; init; }

    /// <summary>PDB の内容についての説明。</summary>
    public string? PdbMessage { get; init; }

    /// <summary>PDB に同じ名前の関数があったシンボルの数。</summary>
    public int PdbMatchedCount { get; init; }

    /// <summary>PDB の値でサイズを置き換えたシンボルの数。</summary>
    public int PdbSizedCount { get; init; }

    public long TotalSize => Symbols.Sum(s => s.Size);

    public int Count => Symbols.Count;
}
