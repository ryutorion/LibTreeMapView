using LibTreeMapView.Core.Comparison;

namespace LibTreeMapView.ViewModels;

/// <summary>比較ビューの配色。</summary>
public static class DiffPalette
{
    public static readonly Color Increase = Color.FromArgb("#D64545");
    public static readonly Color Decrease = Color.FromArgb("#2E9E5B");
    public static readonly Color Added = Color.FromArgb("#3B6FD6");
    public static readonly Color Removed = Color.FromArgb("#8A6BC8");
    public static readonly Color Unchanged = Color.FromArgb("#8892A6");

    public static Color ForDelta(long delta) => delta > 0 ? Increase : delta < 0 ? Decrease : Unchanged;

    public static Color ForStatus(DiffStatus status) => status switch
    {
        DiffStatus.Added => Added,
        DiffStatus.Removed => Removed,
        DiffStatus.Changed => Increase,
        _ => Unchanged,
    };

    public static string Describe(DiffStatus status) => status switch
    {
        DiffStatus.Added => "追加",
        DiffStatus.Removed => "削除",
        DiffStatus.Changed => "変更",
        _ => "同一",
    };
}

/// <summary>オブジェクト差分の 1 行。</summary>
public sealed record ObjectDiffRow(
    string Name,
    string StatusText,
    Color StatusColor,
    string BaselineText,
    string TargetText,
    string DeltaText,
    Color DeltaColor,
    double BarWidth,
    string SectionSummary,
    ObjectDiff Diff);

/// <summary>セクション差分の 1 行。</summary>
public sealed record SectionDiffRow(
    string Name,
    string StatusText,
    Color StatusColor,
    string BaselineText,
    string TargetText,
    string DeltaText,
    Color DeltaColor,
    double BarWidth);
