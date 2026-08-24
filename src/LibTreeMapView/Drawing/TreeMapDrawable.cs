using LibTreeMapView.Core.Layout;
using LibTreeMapView.Core.Model;
using LibTreeMapView.Core.Tree;

namespace LibTreeMapView.Drawing;

/// <summary>ツリーマップの描画。描画時のレイアウト結果をヒットテスト用に保持する。</summary>
public sealed class TreeMapDrawable : IDrawable
{
    private static readonly Color LightBackground = Color.FromArgb("#F3F4F6");
    private static readonly Color DarkBackground = Color.FromArgb("#14181F");
    private static readonly Color SelectionColor = Color.FromArgb("#FFC53D");

    private readonly TreeMapLayoutOptions layoutOptions = new()
    {
        HeaderHeight = 18,
        Padding = 2,
        MinSubdivideSize = 28,
        MaxDepth = 3,
    };

    public TreeNode? Root { get; set; }

    public TreeNode? SelectedNode { get; set; }

    public TreeNode? HoveredNode { get; set; }

    public bool IsDarkTheme { get; set; }

    /// <summary>ポインタ位置。ツールチップの表示位置に使う。</summary>
    public PointF? HoverPoint { get; set; }

    /// <summary>ツールチップに表示する行。1 行目は見出しとして太字で描く。</summary>
    public IReadOnlyList<string>? HoverLines { get; set; }

    /// <summary>直前に描画したレイアウト。ヒットテストは必ずこれを使う。</summary>
    public TreeMapLayoutResult Layout { get; private set; } = TreeMapLayoutResult.Empty;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.FillColor = IsDarkTheme ? DarkBackground : LightBackground;
        canvas.FillRectangle(dirtyRect);

        var bounds = new LayoutRect(dirtyRect.X + 1, dirtyRect.Y + 1, dirtyRect.Width - 2, dirtyRect.Height - 2);
        Layout = TreeMapLayout.Layout(Root, bounds, layoutOptions);

        if (Layout.Tiles.Count == 0)
        {
            return;
        }

        foreach (TreeMapTile tile in Layout.Tiles)
        {
            DrawTile(canvas, tile);
        }

        DrawHighlight(canvas, SelectedNode, SelectionColor, 2.5f);
        DrawHighlight(canvas, HoveredNode, IsDarkTheme ? Colors.White : Color.FromArgb("#1F2933"), 1.75f);

        DrawTooltip(canvas, dirtyRect);
    }

    /// <summary>
    /// ツールチップはキャンバスに直接描く。XAML のオーバーレイにするとポインタのヒットテストを
    /// 奪ってしまい、ホバーが点滅するため。
    /// </summary>
    private void DrawTooltip(ICanvas canvas, RectF dirtyRect)
    {
        if (HoverPoint is not { } point || HoverLines is not { Count: > 0 } lines)
        {
            return;
        }

        const float FontSize = 11.5f;
        const float LineHeight = 16f;
        const float PaddingX = 9f;
        const float PaddingY = 7f;

        float width = 0;
        foreach (string line in lines)
        {
            width = Math.Max(width, MeasureWidth(line, FontSize));
        }

        width = Math.Min(width, 420) + (PaddingX * 2);
        float height = (lines.Count * LineHeight) + (PaddingY * 2);

        float x = point.X + 16;
        float y = point.Y + 18;

        if (x + width > dirtyRect.Right)
        {
            x = Math.Max(dirtyRect.Left, point.X - width - 8);
        }

        if (y + height > dirtyRect.Bottom)
        {
            y = Math.Max(dirtyRect.Top, point.Y - height - 8);
        }

        var box = new RectF(x, y, width, height);

        canvas.FillColor = IsDarkTheme ? Color.FromArgb("#F00F1319") : Color.FromArgb("#FAFFFFFF");
        canvas.FillRoundedRectangle(box, 5);
        canvas.StrokeColor = IsDarkTheme ? Color.FromArgb("#3A4351") : Color.FromArgb("#C7CDD6");
        canvas.StrokeSize = 1;
        canvas.DrawRoundedRectangle(box, 5);

        canvas.FontColor = IsDarkTheme ? Color.FromArgb("#F2F5F9") : Color.FromArgb("#1A1F27");

        for (int i = 0; i < lines.Count; i++)
        {
            canvas.Font = i == 0
                ? Microsoft.Maui.Graphics.Font.DefaultBold
                : Microsoft.Maui.Graphics.Font.Default;
            canvas.FontSize = FontSize;
            canvas.DrawString(
                Truncate(lines[i], width - (PaddingX * 2), FontSize),
                box.X + PaddingX,
                box.Y + PaddingY + (i * LineHeight),
                width - (PaddingX * 2),
                LineHeight,
                HorizontalAlignment.Left,
                VerticalAlignment.Center);
        }
    }

    private static float MeasureWidth(string text, float fontSize)
    {
        float width = 0;
        foreach (char c in text)
        {
            width += CharWidth(c, fontSize);
        }

        return width;
    }

    private void DrawTile(ICanvas canvas, TreeMapTile tile)
    {
        var rect = new RectF(
            (float)tile.Bounds.X,
            (float)tile.Bounds.Y,
            (float)tile.Bounds.Width,
            (float)tile.Bounds.Height);

        if (rect.Width <= 0.5f || rect.Height <= 0.5f)
        {
            return;
        }

        Color color = SectionPalette.GetTileColor(tile.Node.SectionKind, tile.Depth, IsDarkTheme);

        if (tile.IsLeafTile)
        {
            canvas.FillColor = color;
            canvas.FillRectangle(rect);

            if (rect.Width > 2 && rect.Height > 2)
            {
                canvas.StrokeColor = color.WithAlpha(0.55f).WithLuminosity(IsDarkTheme ? 0.1f : 0.95f);
                canvas.StrokeSize = 1;
                canvas.DrawRectangle(rect);
            }

            DrawLeafLabel(canvas, tile, rect, color);
            return;
        }

        // グループ: 枠とヘッダー帯だけを描き、内側は子タイルが埋める。
        canvas.FillColor = color.WithAlpha(IsDarkTheme ? 0.35f : 0.30f);
        canvas.FillRectangle(rect);

        canvas.StrokeColor = color.WithLuminosity(IsDarkTheme ? 0.30f : 0.42f);
        canvas.StrokeSize = 1;
        canvas.DrawRectangle(rect);

        if (tile.Header is { } header)
        {
            var headerRect = new RectF(
                (float)header.X,
                (float)header.Y,
                (float)header.Width,
                (float)header.Height);

            canvas.FillColor = color.WithLuminosity(IsDarkTheme ? 0.26f : 0.52f);
            canvas.FillRectangle(headerRect);

            DrawHeaderLabel(canvas, tile, headerRect);
        }
    }

    private void DrawLeafLabel(ICanvas canvas, TreeMapTile tile, RectF rect, Color background)
    {
        if (rect.Width < 34 || rect.Height < 13)
        {
            return;
        }

        canvas.SaveState();
        canvas.ClipRectangle(rect);

        canvas.FontColor = SectionPalette.GetTextColor(background);
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.FontSize = 11;

        float padding = 4;
        float textWidth = rect.Width - (padding * 2);
        string name = Truncate(tile.Node.Name, textWidth, 11);

        bool twoLines = rect.Height >= 30 && rect.Width >= 56;
        if (twoLines)
        {
            canvas.DrawString(name, rect.X + padding, rect.Y + 3, textWidth, 13, HorizontalAlignment.Left, VerticalAlignment.Top);

            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            canvas.FontSize = 10;
            canvas.DrawString(
                ByteSize.Format(tile.Node.Size),
                rect.X + padding,
                rect.Y + 16,
                textWidth,
                12,
                HorizontalAlignment.Left,
                VerticalAlignment.Top);
        }
        else
        {
            canvas.DrawString(
                name,
                rect.X + padding,
                rect.Y,
                textWidth,
                rect.Height,
                HorizontalAlignment.Left,
                VerticalAlignment.Center);
        }

        canvas.RestoreState();
    }

    private void DrawHeaderLabel(ICanvas canvas, TreeMapTile tile, RectF headerRect)
    {
        if (headerRect.Width < 30)
        {
            return;
        }

        canvas.SaveState();
        canvas.ClipRectangle(headerRect);

        Color headerColor = SectionPalette.GetTileColor(tile.Node.SectionKind, tile.Depth, IsDarkTheme)
            .WithLuminosity(IsDarkTheme ? 0.26f : 0.52f);

        canvas.FontColor = SectionPalette.GetTextColor(headerColor);
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.FontSize = 11;

        string label = $"{tile.Node.Name}  ({ByteSize.Format(tile.Node.Size)})";
        canvas.DrawString(
            Truncate(label, headerRect.Width - 10, 11),
            headerRect.X + 5,
            headerRect.Y,
            headerRect.Width - 10,
            headerRect.Height,
            HorizontalAlignment.Left,
            VerticalAlignment.Center);

        canvas.RestoreState();
    }

    private void DrawHighlight(ICanvas canvas, TreeNode? node, Color color, float strokeSize)
    {
        if (node is null)
        {
            return;
        }

        TreeMapTile? tile = Layout.Tiles.FirstOrDefault(t => ReferenceEquals(t.Node, node));
        if (tile is null)
        {
            return;
        }

        var rect = new RectF(
            (float)tile.Bounds.X,
            (float)tile.Bounds.Y,
            (float)tile.Bounds.Width,
            (float)tile.Bounds.Height);

        canvas.StrokeColor = color;
        canvas.StrokeSize = strokeSize;
        canvas.DrawRectangle(rect.Inflate(-strokeSize / 2, -strokeSize / 2));
    }

    /// <summary>おおよその文字幅で切り詰め、末尾に … を付ける。</summary>
    private static string Truncate(string text, float availableWidth, float fontSize)
    {
        if (availableWidth <= 0)
        {
            return string.Empty;
        }

        float width = 0;
        for (int i = 0; i < text.Length; i++)
        {
            width += CharWidth(text[i], fontSize);
            if (width > availableWidth)
            {
                return i <= 1 ? text[..Math.Min(1, text.Length)] : string.Concat(text.AsSpan(0, i - 1), "…");
            }
        }

        return text;
    }

    private static float CharWidth(char c, float fontSize) => c < 128 ? fontSize * 0.55f : fontSize * 1.0f;
}
