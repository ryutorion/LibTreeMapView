using LibTreeMapView.Core.Tree;

namespace LibTreeMapView.Core.Layout;

/// <summary>レイアウトの見た目に関わるパラメーター。</summary>
public sealed record TreeMapLayoutOptions
{
    /// <summary>グループ矩形の上部に確保するラベル領域の高さ。</summary>
    public double HeaderHeight { get; init; } = 18;

    /// <summary>入れ子の際に内側へ取る余白。</summary>
    public double Padding { get; init; } = 2;

    /// <summary>これより小さいタイルは子を展開しない。</summary>
    public double MinSubdivideSize { get; init; } = 26;

    /// <summary>展開する最大の深さ (ルートの子が深さ 1)。</summary>
    public int MaxDepth { get; init; } = 3;
}

/// <summary>配置済みのタイル 1 枚。</summary>
public sealed class TreeMapTile
{
    public required TreeNode Node { get; init; }

    public required LayoutRect Bounds { get; init; }

    /// <summary>ルートを 0 とした深さ。</summary>
    public required int Depth { get; init; }

    /// <summary>このタイルの内側に子タイルを描画していない (末端として塗る)。</summary>
    public required bool IsLeafTile { get; init; }

    /// <summary>グループ名を描く帯。子を展開したタイルのみ持つ。</summary>
    public LayoutRect? Header { get; init; }
}

/// <summary>レイアウト結果。</summary>
public sealed class TreeMapLayoutResult
{
    public static readonly TreeMapLayoutResult Empty = new(null, [], new LayoutRect(0, 0, 0, 0));

    public TreeMapLayoutResult(TreeNode? root, IReadOnlyList<TreeMapTile> tiles, LayoutRect bounds)
    {
        Root = root;
        Tiles = tiles;
        Bounds = bounds;
    }

    public TreeNode? Root { get; }

    /// <summary>描画順 (浅いものから) に並んだタイル。</summary>
    public IReadOnlyList<TreeMapTile> Tiles { get; }

    public LayoutRect Bounds { get; }

    /// <summary>指定座標の最も深いタイルを返す。</summary>
    public TreeMapTile? HitTest(double x, double y)
    {
        TreeMapTile? hit = null;
        foreach (TreeMapTile tile in Tiles)
        {
            if (tile.Bounds.Contains(x, y) && (hit is null || tile.Depth >= hit.Depth))
            {
                hit = tile;
            }
        }

        return hit;
    }
}

/// <summary>Bruls らの squarified treemap アルゴリズムによる配置。</summary>
public static class TreeMapLayout
{
    public static TreeMapLayoutResult Layout(TreeNode? root, LayoutRect bounds, TreeMapLayoutOptions? options = null)
    {
        options ??= new TreeMapLayoutOptions();

        if (root is null || bounds.IsEmpty || root.Size <= 0)
        {
            return TreeMapLayoutResult.Empty;
        }

        var tiles = new List<TreeMapTile>();
        LayoutChildren(root, bounds, 0, options, tiles);
        return new TreeMapLayoutResult(root, tiles, bounds);
    }

    private static void LayoutChildren(
        TreeNode parent,
        LayoutRect area,
        int depth,
        TreeMapLayoutOptions options,
        List<TreeMapTile> tiles)
    {
        if (area.IsEmpty || parent.Children.Count == 0)
        {
            return;
        }

        List<TreeNode> children = parent.Children
            .Where(c => c.Size > 0)
            .OrderByDescending(c => c.Size)
            .ToList();

        if (children.Count == 0)
        {
            return;
        }

        var placements = new List<(TreeNode Node, LayoutRect Rect)>(children.Count);
        Squarify(children, area, placements);

        foreach ((TreeNode node, LayoutRect rect) in placements)
        {
            if (rect.IsEmpty)
            {
                continue;
            }

            int childDepth = depth + 1;
            bool canSubdivide = !node.IsLeaf &&
                                childDepth < options.MaxDepth &&
                                rect.Width >= options.MinSubdivideSize &&
                                rect.Height >= options.MinSubdivideSize + options.HeaderHeight;

            if (!canSubdivide)
            {
                tiles.Add(new TreeMapTile
                {
                    Node = node,
                    Bounds = rect,
                    Depth = childDepth,
                    IsLeafTile = true,
                });
                continue;
            }

            var header = new LayoutRect(rect.X, rect.Y, rect.Width, options.HeaderHeight);
            tiles.Add(new TreeMapTile
            {
                Node = node,
                Bounds = rect,
                Depth = childDepth,
                IsLeafTile = false,
                Header = header,
            });

            var inner = new LayoutRect(
                rect.X + options.Padding,
                rect.Y + options.HeaderHeight,
                rect.Width - (options.Padding * 2),
                rect.Height - options.HeaderHeight - options.Padding);

            LayoutChildren(node, inner, childDepth, options, tiles);
        }
    }

    private static void Squarify(
        IReadOnlyList<TreeNode> children,
        LayoutRect area,
        List<(TreeNode Node, LayoutRect Rect)> placements)
    {
        double totalSize = children.Sum(c => (double)c.Size);
        if (totalSize <= 0)
        {
            return;
        }

        double scale = area.Area / totalSize;
        LayoutRect free = area;

        var row = new List<(TreeNode Node, double Area)>();
        double rowSum = 0;
        double rowMin = double.MaxValue;
        double rowMax = 0;

        foreach (TreeNode child in children)
        {
            double childArea = child.Size * scale;
            double side = Math.Min(free.Width, free.Height);

            if (side <= 0)
            {
                break;
            }

            double newSum = rowSum + childArea;
            double newMin = Math.Min(rowMin, childArea);
            double newMax = Math.Max(rowMax, childArea);

            bool startsNewRow = row.Count > 0 &&
                                Worst(newSum, newMin, newMax, side) > Worst(rowSum, rowMin, rowMax, side);

            if (startsNewRow)
            {
                free = PlaceRow(row, rowSum, free, placements);
                row.Clear();
                rowSum = 0;
                rowMin = double.MaxValue;
                rowMax = 0;

                side = Math.Min(free.Width, free.Height);
                if (side <= 0)
                {
                    break;
                }

                newSum = childArea;
                newMin = childArea;
                newMax = childArea;
            }

            row.Add((child, childArea));
            rowSum = newSum;
            rowMin = newMin;
            rowMax = newMax;
        }

        if (row.Count > 0)
        {
            PlaceRow(row, rowSum, free, placements);
        }
    }

    /// <summary>行に含まれるタイルの縦横比のうち最悪値。小さいほど正方形に近い。</summary>
    private static double Worst(double sum, double min, double max, double side)
    {
        if (sum <= 0 || min <= 0 || side <= 0)
        {
            return double.MaxValue;
        }

        double sideSquared = side * side;
        double sumSquared = sum * sum;
        return Math.Max(sideSquared * max / sumSquared, sumSquared / (sideSquared * min));
    }

    /// <summary>1 行分を配置し、残りの領域を返す。</summary>
    private static LayoutRect PlaceRow(
        List<(TreeNode Node, double Area)> row,
        double rowSum,
        LayoutRect free,
        List<(TreeNode Node, LayoutRect Rect)> placements)
    {
        if (rowSum <= 0 || free.IsEmpty)
        {
            foreach ((TreeNode node, _) in row)
            {
                placements.Add((node, new LayoutRect(free.X, free.Y, 0, 0)));
            }

            return free;
        }

        bool vertical = free.Width >= free.Height;

        if (vertical)
        {
            double columnWidth = Math.Min(rowSum / free.Height, free.Width);
            double y = free.Y;

            foreach ((TreeNode node, double area) in row)
            {
                double height = columnWidth > 0 ? area / columnWidth : 0;
                height = Math.Min(height, Math.Max(0, free.Bottom - y));
                placements.Add((node, new LayoutRect(free.X, y, columnWidth, height)));
                y += height;
            }

            return new LayoutRect(free.X + columnWidth, free.Y, free.Width - columnWidth, free.Height);
        }

        double rowHeight = Math.Min(rowSum / free.Width, free.Height);
        double x = free.X;

        foreach ((TreeNode node, double area) in row)
        {
            double width = rowHeight > 0 ? area / rowHeight : 0;
            width = Math.Min(width, Math.Max(0, free.Right - x));
            placements.Add((node, new LayoutRect(x, free.Y, width, rowHeight)));
            x += width;
        }

        return new LayoutRect(free.X, free.Y + rowHeight, free.Width, free.Height - rowHeight);
    }
}
