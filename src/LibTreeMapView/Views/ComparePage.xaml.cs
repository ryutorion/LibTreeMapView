using LibTreeMapView.ViewModels;

namespace LibTreeMapView.Views;

public partial class ComparePage : ContentPage
{
    private static readonly TimeSpan FilterDebounce = TimeSpan.FromMilliseconds(300);

    private readonly CompareViewModel viewModel;

    private CancellationTokenSource? filterDebounce;

    public ComparePage(CompareViewModel viewModel)
    {
        InitializeComponent();

        this.viewModel = viewModel;
        BindingContext = viewModel;

        NavigationPage.SetHasNavigationBar(this, false);
    }

    /// <summary>単一表示から来たときに、開いていたライブラリを比較元にしておく。</summary>
    public Task EnsureBaselineAsync(string? path) =>
        string.IsNullOrEmpty(path) || viewModel.HasBaseline
            ? Task.CompletedTask
            : viewModel.LoadAsync(path, isBaseline: true);

    /// <summary>コマンドライン等から渡された 2 つのライブラリを読み込む。</summary>
    public async Task LoadAsync(string baselinePath, string targetPath)
    {
        await viewModel.LoadAsync(baselinePath, isBaseline: true);
        await viewModel.LoadAsync(targetPath, isBaseline: false);
    }

    protected override void OnDisappearing()
    {
        filterDebounce?.Cancel();
        filterDebounce?.Dispose();
        filterDebounce = null;

        base.OnDisappearing();
    }

    private async void OnBackClicked(object? sender, EventArgs e) => await Navigation.PopAsync();

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
