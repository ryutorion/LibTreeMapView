using System.Windows.Input;
using LibTreeMapView.Core.Model;
using LibTreeMapView.Core.Tree;

namespace LibTreeMapView.ViewModels;

/// <summary>グループ化方法の選択肢 (Picker 用)。</summary>
public sealed record GroupingOption(string Display, GroupingMode Mode)
{
    public override string ToString() => Display;
}

/// <summary>パンくずの 1 項目。</summary>
public sealed record BreadcrumbItem(string Name, TreeNode Node, ICommand Command, bool ShowSeparator);

/// <summary>「サイズ上位」一覧の 1 行。</summary>
public sealed record RankedItem(
    int Rank,
    string Name,
    string Location,
    string SizeText,
    string PercentText,
    TreeNode Node,
    ICommand Command);

/// <summary>詳細パネルの 1 行。</summary>
public sealed record DetailItem(string Label, string Value);

/// <summary>凡例の 1 行。</summary>
public sealed record LegendItem(
    SectionKind Kind,
    string Name,
    Color Color,
    string SizeText,
    string PercentText,
    double BarWidth);
