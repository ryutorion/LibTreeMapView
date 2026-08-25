using LibTreeMapView.Core.Model;

namespace LibTreeMapView.Core.Comparison;

/// <summary>比較したときの状態。</summary>
public enum DiffStatus
{
    /// <summary>両方にあり、サイズも同じ。</summary>
    Unchanged,

    /// <summary>比較先にだけある。</summary>
    Added,

    /// <summary>比較元にだけある。</summary>
    Removed,

    /// <summary>両方にあるがサイズが違う。</summary>
    Changed,
}

/// <summary>セクション 1 種類分の比較結果。同じ名前のセクションは合算する。</summary>
public sealed record SectionDiff(string Name, SectionKind Kind, long BaselineSize, long TargetSize)
{
    public long Delta => TargetSize - BaselineSize;

    public DiffStatus Status => BaselineSize == 0 && TargetSize > 0
        ? DiffStatus.Added
        : TargetSize == 0 && BaselineSize > 0
            ? DiffStatus.Removed
            : Delta == 0
                ? DiffStatus.Unchanged
                : DiffStatus.Changed;

    public bool IsChanged => Status != DiffStatus.Unchanged;
}

/// <summary>オブジェクトファイル 1 つ分の比較結果。</summary>
public sealed record ObjectDiff(
    string Name,
    bool InBaseline,
    bool InTarget,
    long BaselineSize,
    long TargetSize,
    IReadOnlyList<SectionDiff> Sections)
{
    public long Delta => TargetSize - BaselineSize;

    public DiffStatus Status => !InBaseline
        ? DiffStatus.Added
        : !InTarget
            ? DiffStatus.Removed
            // サイズの合計が同じでも、セクションの内訳が動いていれば「変更」とみなす。
            : Delta != 0 || Sections.Any(s => s.IsChanged)
                ? DiffStatus.Changed
                : DiffStatus.Unchanged;

    public bool IsChanged => Status != DiffStatus.Unchanged;

    public int ChangedSectionCount => Sections.Count(s => s.IsChanged);
}

/// <summary>比較の条件。単一表示の「含める」設定と揃える。</summary>
public sealed record LibraryCompareOptions
{
    /// <summary>.bss など、ファイル上に実体を持たないセクションを含める。</summary>
    public bool IncludeUninitialized { get; init; } = true;

    /// <summary>アーカイブのメタデータ (シンボルテーブル、ヘッダー、再配置) を含める。</summary>
    public bool IncludeMetadata { get; init; }
}

/// <summary>2 つのライブラリの比較結果。</summary>
public sealed class LibraryDiff
{
    public static readonly LibraryDiff Empty = new(null, null, []);

    public LibraryDiff(LibraryInfo? baseline, LibraryInfo? target, IReadOnlyList<ObjectDiff> objects)
    {
        Baseline = baseline;
        Target = target;
        Objects = objects;

        foreach (ObjectDiff diff in objects)
        {
            BaselineSize += diff.BaselineSize;
            TargetSize += diff.TargetSize;

            switch (diff.Status)
            {
                case DiffStatus.Added:
                    AddedCount++;
                    break;
                case DiffStatus.Removed:
                    RemovedCount++;
                    break;
                case DiffStatus.Changed:
                    ChangedCount++;
                    break;
                default:
                    UnchangedCount++;
                    break;
            }
        }
    }

    /// <summary>比較元 (A)。</summary>
    public LibraryInfo? Baseline { get; }

    /// <summary>比較先 (B)。</summary>
    public LibraryInfo? Target { get; }

    /// <summary>差分の大きい順に並んだオブジェクト。</summary>
    public IReadOnlyList<ObjectDiff> Objects { get; }

    public long BaselineSize { get; }

    public long TargetSize { get; }

    public long Delta => TargetSize - BaselineSize;

    public int AddedCount { get; }

    public int RemovedCount { get; }

    public int ChangedCount { get; }

    public int UnchangedCount { get; }

    public bool HasResult => Baseline is not null && Target is not null;

    /// <summary>差分のあるオブジェクトの数。</summary>
    public int ChangedObjectCount => AddedCount + RemovedCount + ChangedCount;
}
