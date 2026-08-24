namespace LibTreeMapView.Core.Layout;

/// <summary>UI フレームワークに依存しない矩形。</summary>
public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double Area => Width * Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>四辺を <paramref name="amount"/> だけ内側に縮める。潰れる場合はサイズ 0 を返す。</summary>
    public LayoutRect Deflate(double amount)
    {
        double width = Width - (amount * 2);
        double height = Height - (amount * 2);
        return width <= 0 || height <= 0
            ? new LayoutRect(X + (Width / 2), Y + (Height / 2), 0, 0)
            : new LayoutRect(X + amount, Y + amount, width, height);
    }
}
