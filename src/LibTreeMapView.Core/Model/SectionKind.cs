namespace LibTreeMapView.Core.Model;

/// <summary>セクションの大分類。配色と凡例に使う。</summary>
public enum SectionKind
{
    /// <summary>実行コード (.text)。</summary>
    Code,

    /// <summary>初期化済みデータ (.data)。</summary>
    Data,

    /// <summary>読み取り専用データ (.rdata, .rodata)。</summary>
    ReadOnlyData,

    /// <summary>未初期化データ (.bss)。ファイル上の実体は持たない。</summary>
    UninitializedData,

    /// <summary>デバッグ情報 (.debug$S, .debug$T など)。</summary>
    Debug,

    /// <summary>例外処理 (.pdata, .xdata)。</summary>
    ExceptionHandling,

    /// <summary>リンカーディレクティブ (.drectve) やコンパイラ情報 (.chks64 など)。</summary>
    Directive,

    /// <summary>インポート／エクスポート関連 (.idata, .edata)。</summary>
    Import,

    /// <summary>アーカイブ／オブジェクトのメタデータ (シンボルテーブル、ヘッダー、再配置)。</summary>
    Metadata,

    /// <summary>その他。</summary>
    Other,
}
