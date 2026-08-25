using LibTreeMapView.Core.Layout;
using LibTreeMapView.Core.Tree;

namespace LibTreeMapView.Drawing;

/// <summary>
/// ツリーマップの操作 (ホバー・選択・ズーム・ツールチップ) をまとめたもの。
/// ページ側は XAML のイベントをここへ流すだけでよい。
/// </summary>
public sealed class TreeMapController
{
    private readonly GraphicsView view;

    public TreeMapController(GraphicsView view)
    {
        this.view = view;
        view.Drawable = Drawable;
    }

    public TreeMapDrawable Drawable { get; } = new();

    /// <summary>ツールチップに出す行を作る。未設定ならツールチップを出さない。</summary>
    public Func<TreeNode, IReadOnlyList<string>>? TooltipLines { get; set; }

    /// <summary>タイルがタップされた。</summary>
    public event EventHandler<TreeNode>? Selected;

    /// <summary>タイルがダブルタップされた (ズーム要求)。</summary>
    public event EventHandler<TreeNode>? ZoomRequested;

    /// <summary>ホバー先が変わった。</summary>
    public event EventHandler<TreeNode?>? HoverChanged;

    /// <summary>描画するツリー。</summary>
    public TreeNode? Root
    {
        get => Drawable.Root;
        set
        {
            Drawable.Root = value;
            Drawable.SelectedNode = null;
            Drawable.HoveredNode = null;
            HideTooltip();
            view.Invalidate();
        }
    }

    public void SetHighlight(TreeNode? selected, TreeNode? hovered)
    {
        Drawable.SelectedNode = selected;
        Drawable.HoveredNode = hovered;
        view.Invalidate();
    }

    public void ApplyTheme(bool isDark)
    {
        Drawable.IsDarkTheme = isDark;
        view.Invalidate();
    }

    public void OnTapped(Point? position)
    {
        if (HitTest(position) is { } tile)
        {
            Selected?.Invoke(this, tile.Node);
        }
    }

    public void OnDoubleTapped(Point? position)
    {
        if (HitTest(position) is not { } tile)
        {
            return;
        }

        // 末端をダブルクリックしたときは 1 つ上のまとまりへズームする。
        ZoomRequested?.Invoke(this, tile.Node.IsLeaf ? tile.Node.Parent ?? tile.Node : tile.Node);
    }

    public void OnPointerMoved(Point? position)
    {
        TreeMapTile? tile = HitTest(position);

        if (tile is null || position is not { } point)
        {
            OnPointerExited();
            return;
        }

        Drawable.HoverPoint = new PointF((float)point.X, (float)point.Y);
        Drawable.HoverLines = TooltipLines?.Invoke(tile.Node);

        // ホバー先が変わったときは呼び出し側が再描画するので、二重に呼ばない。
        bool changed = !ReferenceEquals(Drawable.HoveredNode, tile.Node);
        if (changed)
        {
            HoverChanged?.Invoke(this, tile.Node);
        }
        else
        {
            view.Invalidate();
        }
    }

    public void OnPointerExited()
    {
        bool changed = Drawable.HoveredNode is not null;
        HideTooltip();

        if (changed)
        {
            HoverChanged?.Invoke(this, null);
        }
        else
        {
            view.Invalidate();
        }
    }

    private void HideTooltip()
    {
        Drawable.HoverPoint = null;
        Drawable.HoverLines = null;
    }

    private TreeMapTile? HitTest(Point? position) =>
        position is { } point ? Drawable.Layout.HitTest(point.X, point.Y) : null;
}
