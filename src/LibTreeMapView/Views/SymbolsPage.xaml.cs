using LibTreeMapView.Core.Model;
using LibTreeMapView.Core.Symbols;
using LibTreeMapView.Core.Tree;
using LibTreeMapView.Drawing;
using LibTreeMapView.ViewModels;

namespace LibTreeMapView.Views;

public partial class SymbolsPage : ContentPage
{
    private static readonly TimeSpan FilterDebounce = TimeSpan.FromMilliseconds(300);

    private static readonly FilePickerFileType LibFileType = new(
        new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.WinUI] = [".lib"],
        });

    private readonly SymbolsViewModel viewModel;
    private readonly TreeMapController treeMap;

    private CancellationTokenSource? filterDebounce;

    public SymbolsPage(SymbolsViewModel viewModel)
    {
        InitializeComponent();

        this.viewModel = viewModel;
        BindingContext = viewModel;

        NavigationPage.SetHasNavigationBar(this, false);

        treeMap = new TreeMapController(TreeMapView) { TooltipLines = BuildTooltipLines };
        treeMap.Selected += (_, node) => viewModel.SelectedNode = node;
        treeMap.ZoomRequested += (_, node) => viewModel.ZoomInto(node);
        treeMap.HoverChanged += (_, node) => viewModel.HoveredNode = node;

        viewModel.TreeChanged += (_, _) => treeMap.Root = viewModel.DisplayRoot;
        viewModel.HighlightChanged += (_, _) => treeMap.SetHighlight(viewModel.SelectedNode, viewModel.HoveredNode);
    }

    /// <summary>単一表示から来たときに、開いていたライブラリのシンボルを読む。</summary>
    public Task EnsureLoadedAsync(string? path) =>
        string.IsNullOrEmpty(path) || viewModel.HasSymbols ? Task.CompletedTask : viewModel.LoadAsync(path);

    protected override void OnAppearing()
    {
        base.OnAppearing();

        treeMap.ApplyTheme((Application.Current?.RequestedTheme ?? AppTheme.Light) == AppTheme.Dark);
    }

    protected override void OnDisappearing()
    {
        filterDebounce?.Cancel();
        filterDebounce?.Dispose();
        filterDebounce = null;

        base.OnDisappearing();
    }

    private async void OnBackClicked(object? sender, EventArgs e) => await Navigation.PopAsync();

    private async void OnOpenClicked(object? sender, EventArgs e)
    {
        try
        {
            FileResult? result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "シンボルを見る .lib を選択",
                FileTypes = LibFileType,
            });

            if (result is not null)
            {
                await viewModel.LoadAsync(result.FullPath);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("読み込みエラー", ex.Message, "OK");
        }
    }

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

        if (node.Symbol is { } symbol)
        {
            lines.Add(symbol.NamespaceText);
            lines.Add($"{symbol.ObjectName} / {symbol.SectionName}");
            lines.Add(SymbolsViewModel.DescribeSource(symbol.SizeSource));
        }
        else
        {
            lines.Add($"{node.Children.Count:N0} 個の子 / シンボル {node.LeafCount:N0} 件 — ダブルクリックでズーム");
        }

        return lines;
    }

    private async void OnFilterTextChanged(object? sender, TextChangedEventArgs e)
    {
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
}
