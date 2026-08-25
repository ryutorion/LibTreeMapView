using LibTreeMapView.Core.Model;
using LibTreeMapView.Core.Tree;
using LibTreeMapView.Drawing;
using LibTreeMapView.ViewModels;

namespace LibTreeMapView.Views;

public partial class MainPage : ContentPage
{
    private static readonly TimeSpan FilterDebounce = TimeSpan.FromMilliseconds(300);

    private readonly MainViewModel viewModel;
    private readonly ComparePage comparePage;
    private readonly SymbolsPage symbolsPage;
    private readonly TreeMapController treeMap;

    private CancellationTokenSource? filterDebounce;

    public MainPage(MainViewModel viewModel, ComparePage comparePage, SymbolsPage symbolsPage)
    {
        InitializeComponent();

        this.viewModel = viewModel;
        this.comparePage = comparePage;
        this.symbolsPage = symbolsPage;
        BindingContext = viewModel;

        NavigationPage.SetHasNavigationBar(this, false);

        treeMap = new TreeMapController(TreeMapView) { TooltipLines = BuildTooltipLines };
        treeMap.Selected += (_, node) => viewModel.SelectedNode = node;
        treeMap.ZoomRequested += (_, node) => viewModel.ZoomInto(node);
        treeMap.HoverChanged += (_, node) => viewModel.HoveredNode = node;

        viewModel.TreeChanged += (_, _) => treeMap.Root = viewModel.DisplayRoot;
        viewModel.HighlightChanged += (_, _) => treeMap.SetHighlight(viewModel.SelectedNode, viewModel.HoveredNode);
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

        filterDebounce?.Cancel();
        filterDebounce?.Dispose();
        filterDebounce = null;

        base.OnDisappearing();
    }

    /// <summary>コマンドライン等から渡されたファイルを開く。</summary>
    public Task LoadAsync(string path) => viewModel.LoadAsync(path);

    private async void OnCompareClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(comparePage);

        // 単一表示で開いていたライブラリをそのまま比較元にする。
        await comparePage.EnsureBaselineAsync(viewModel.Library?.FilePath);
    }

    private async void OnSymbolsClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(symbolsPage);

        // 単一表示で開いていたライブラリのシンボルを読む。
        await symbolsPage.EnsureLoadedAsync(viewModel.Library?.FilePath);
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e) => ApplyTheme();

    private void ApplyTheme() =>
        treeMap.ApplyTheme((Application.Current?.RequestedTheme ?? AppTheme.Light) == AppTheme.Dark);

    private void OnTapped(object? sender, TappedEventArgs e) => treeMap.OnTapped(e.GetPosition(TreeMapView));

    private void OnDoubleTapped(object? sender, TappedEventArgs e) => treeMap.OnDoubleTapped(e.GetPosition(TreeMapView));

    private void OnPointerMoved(object? sender, PointerEventArgs e) => treeMap.OnPointerMoved(e.GetPosition(TreeMapView));

    private void OnPointerExited(object? sender, PointerEventArgs e) => treeMap.OnPointerExited();

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
