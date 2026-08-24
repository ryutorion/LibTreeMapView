using LibTreeMapView.Core.Layout;
using LibTreeMapView.Core.Model;
using LibTreeMapView.Core.Tree;
using LibTreeMapView.Drawing;
using LibTreeMapView.ViewModels;

namespace LibTreeMapView.Views;

public partial class MainPage : ContentPage
{
    private static readonly TimeSpan FilterDebounce = TimeSpan.FromMilliseconds(300);

    private readonly MainViewModel viewModel;
    private readonly TreeMapDrawable drawable = new();

    private CancellationTokenSource? filterDebounce;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();

        this.viewModel = viewModel;
        BindingContext = viewModel;

        TreeMapView.Drawable = drawable;

        viewModel.TreeChanged += OnTreeChanged;
        viewModel.HighlightChanged += OnHighlightChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        ApplyTheme();
        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged += OnRequestedThemeChanged;
        }
    }

    protected override void OnDisappearing()
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged -= OnRequestedThemeChanged;
        }

        base.OnDisappearing();
    }

    /// <summary>コマンドライン等から渡されたファイルを開く。</summary>
    public Task LoadAsync(string path) => viewModel.LoadAsync(path);

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        ApplyTheme();
        TreeMapView.Invalidate();
    }

    private void ApplyTheme() =>
        drawable.IsDarkTheme = (Application.Current?.RequestedTheme ?? AppTheme.Light) == AppTheme.Dark;

    private void OnTreeChanged(object? sender, EventArgs e)
    {
        drawable.Root = viewModel.DisplayRoot;
        drawable.SelectedNode = null;
        drawable.HoveredNode = null;
        HideTooltip();
        TreeMapView.Invalidate();
    }

    private void OnHighlightChanged(object? sender, EventArgs e)
    {
        drawable.SelectedNode = viewModel.SelectedNode;
        drawable.HoveredNode = viewModel.HoveredNode;
        TreeMapView.Invalidate();
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (HitTest(e.GetPosition(TreeMapView)) is { } tile)
        {
            viewModel.SelectedNode = tile.Node;
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (HitTest(e.GetPosition(TreeMapView)) is not { } tile)
        {
            return;
        }

        // 末端をダブルクリックしたときは 1 つ上のまとまりへズームする。
        viewModel.ZoomInto(tile.Node.IsLeaf ? tile.Node.Parent ?? tile.Node : tile.Node);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        Point? position = e.GetPosition(TreeMapView);
        TreeMapTile? tile = HitTest(position);

        viewModel.HoveredNode = tile?.Node;

        if (tile is null || position is not { } point)
        {
            HideTooltip();
            return;
        }

        drawable.HoverPoint = new PointF((float)point.X, (float)point.Y);
        drawable.HoverLines = BuildTooltipLines(tile.Node);
        TreeMapView.Invalidate();
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        viewModel.HoveredNode = null;
        HideTooltip();
    }

    private TreeMapTile? HitTest(Point? position) =>
        position is { } point ? drawable.Layout.HitTest(point.X, point.Y) : null;

    private IReadOnlyList<string> BuildTooltipLines(TreeNode node)
    {
        var lines = new List<string> { node.Name };

        string percent = viewModel.DisplayRoot is { } root
            ? $"  ({ByteSize.FormatPercent(node.Size, root.Size)})"
            : string.Empty;
        lines.Add($"{ByteSize.Format(node.Size)}{percent}");

        if (node.Section is { } section)
        {
            string comdat = section.IsComdat ? "  COMDAT" : string.Empty;
            lines.Add($"{section.Name}  [{section.Attributes}]{comdat}");
        }

        if (node.ObjectFile is { } obj && node.Kind != TreeNodeKind.SectionGroup)
        {
            lines.Add(obj.Name);
        }

        if (!node.IsLeaf)
        {
            lines.Add($"子 {node.Children.Count:N0} / 末端 {node.LeafCount:N0} — ダブルクリックでズーム");
        }

        return lines;
    }

    private void HideTooltip()
    {
        drawable.HoverPoint = null;
        drawable.HoverLines = null;
        TreeMapView.Invalidate();
    }

    private async void OnFilterTextChanged(object? sender, TextChangedEventArgs e)
    {
        // 入力のたびにツリーを作り直すと重いので少し待つ。
        filterDebounce?.Cancel();
        filterDebounce?.Dispose();
        var cts = new CancellationTokenSource();
        filterDebounce = cts;

        try
        {
            await Task.Delay(FilterDebounce, cts.Token);
            if (!cts.IsCancellationRequested)
            {
                viewModel.FilterText = e.NewTextValue ?? string.Empty;
            }
        }
        catch (OperationCanceledException)
        {
            // 次の入力が来ただけ。
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
#if WINDOWS
        if (e.PlatformArgs?.DragEventArgs is { } args)
        {
            args.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }
#endif
    }

    private async void OnDrop(object? sender, DropEventArgs e)
    {
#if WINDOWS
        if (e.PlatformArgs?.DragEventArgs is not { } args ||
            !args.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            return;
        }

        try
        {
            IReadOnlyList<Windows.Storage.IStorageItem> items = await args.DataView.GetStorageItemsAsync();
            if (items.OfType<Windows.Storage.StorageFile>().FirstOrDefault() is { } file)
            {
                await viewModel.LoadAsync(file.Path);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("読み込みエラー", ex.Message, "OK");
        }
#else
        await Task.CompletedTask;
#endif
    }
}
