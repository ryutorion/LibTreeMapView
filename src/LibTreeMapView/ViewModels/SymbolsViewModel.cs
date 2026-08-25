using System.Collections.ObjectModel;
using System.Windows.Input;
using LibTreeMapView.Core.Model;
using LibTreeMapView.Core.Symbols;
using LibTreeMapView.Core.Tree;

namespace LibTreeMapView.ViewModels;

/// <summary>シンボル一覧の 1 行。</summary>
public sealed record SymbolRow(
    int Rank,
    string Name,
    string NamespaceText,
    string SizeText,
    string PercentText,
    string Location,
    string SizeSourceText,
    double BarWidth,
    TreeNode Node,
    ICommand Command);

/// <summary>シンボルの絞り込み方 (Picker 用)。</summary>
public sealed record SymbolKindOption(string Display, SymbolKindFilter Filter)
{
    public override string ToString() => Display;
}

/// <summary>シンボル画面の状態。</summary>
public sealed class SymbolsViewModel : ObservableObject
{
    private const int RankedCount = 30;

    private readonly SymbolAnalyzerService analyzer;

    private SymbolIndex index = SymbolIndex.Empty;
    private TreeNode? fullTree;
    private TreeNode? displayRoot;
    private TreeNode? selectedNode;
    private TreeNode? hoveredNode;
    private SymbolKindOption kindOption;
    private bool usePdb = true;
    private string filterText = string.Empty;
    private bool isBusy;
    private int busyCount;
    private int buildGeneration;
    private string statusMessage = "ライブラリを開くと、シンボルを名前空間ごとに整理して表示します。";
    private string? errorMessage;

    public SymbolsViewModel(SymbolAnalyzerService analyzer)
    {
        this.analyzer = analyzer;
        kindOption = KindOptions[0];

        NavigateCommand = new Command<TreeNode>(node =>
        {
            if (node is not null && !node.IsLeaf)
            {
                DisplayRoot = node;
            }
        });

        SelectCommand = new Command<TreeNode>(node => SelectedNode = node);
        ZoomOutCommand = new Command(
            () => DisplayRoot = displayRoot?.Parent ?? fullTree,
            () => displayRoot?.Parent is not null);
        ResetZoomCommand = new Command(
            () => DisplayRoot = fullTree,
            () => displayRoot is not null && !ReferenceEquals(displayRoot, fullTree));
    }

    /// <summary>ツリーが入れ替わった。</summary>
    public event EventHandler? TreeChanged;

    /// <summary>選択・ホバーが変わった。</summary>
    public event EventHandler? HighlightChanged;

    public IReadOnlyList<SymbolKindOption> KindOptions { get; } =
    [
        new("すべて", SymbolKindFilter.All),
        new("関数のみ", SymbolKindFilter.FunctionsOnly),
        new("データのみ", SymbolKindFilter.DataOnly),
    ];

    public ObservableCollection<BreadcrumbItem> Breadcrumb { get; } = [];

    public ObservableCollection<SymbolRow> RankedSymbols { get; } = [];

    public ObservableCollection<DetailItem> Details { get; } = [];

    public ICommand NavigateCommand { get; }

    public ICommand SelectCommand { get; }

    public ICommand ZoomOutCommand { get; }

    public ICommand ResetZoomCommand { get; }

    public string LibraryPath => index.LibraryPath.Length > 0 ? index.LibraryPath : "(未読み込み)";

    public bool HasSymbols => index.Count > 0;

    public TreeNode? DisplayRoot
    {
        get => displayRoot;
        private set
        {
            if (SetProperty(ref displayRoot, value))
            {
                OnPropertyChanged(nameof(DisplayRootText));
                UpdateBreadcrumb();
                UpdateRanked();
                RaiseCommandStates();
                TreeChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string DisplayRootText => displayRoot is null
        ? string.Empty
        : $"{displayRoot.Name} — {ByteSize.Format(displayRoot.Size)} / {displayRoot.LeafCount:N0} 個のシンボル";

    public TreeNode? SelectedNode
    {
        get => selectedNode;
        set
        {
            if (SetProperty(ref selectedNode, value))
            {
                OnPropertyChanged(nameof(SelectedTitle));
                UpdateDetails();
                HighlightChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public TreeNode? HoveredNode
    {
        get => hoveredNode;
        set
        {
            if (SetProperty(ref hoveredNode, value))
            {
                OnPropertyChanged(nameof(HoverText));
                HighlightChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string HoverText => hoveredNode is null
        ? string.Empty
        : $"{string.Join(" › ", hoveredNode.PathFromRoot.Select(n => n.Name))} — {ByteSize.Format(hoveredNode.Size)}";

    public string SelectedTitle => selectedNode?.Symbol?.LeafName ?? selectedNode?.Name ?? "タイルを選択してください";

    public SymbolKindOption SelectedKind
    {
        get => kindOption;
        set
        {
            if (value is not null && SetProperty(ref kindOption, value))
            {
                RebuildTree();
            }
        }
    }

    /// <summary>同じディレクトリの PDB を使う。</summary>
    public bool UsePdb
    {
        get => usePdb;
        set
        {
            if (SetProperty(ref usePdb, value) && index.LibraryPath.Length > 0)
            {
                _ = LoadAsync(index.LibraryPath);
            }
        }
    }

    public string FilterText
    {
        get => filterText;
        set
        {
            if (SetProperty(ref filterText, value))
            {
                RebuildTree();
            }
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public string? ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (SetProperty(ref errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(errorMessage);

    /// <summary>シンボル数とサイズの概要。</summary>
    public string SummaryText => index.Count == 0
        ? string.Empty
        : $"シンボル {index.Count:N0} 件 ／ 合計 {ByteSize.Format(index.TotalSize)}";

    /// <summary>PDB の状態。</summary>
    public string PdbText => index.PdbStatus switch
    {
        PdbStatus.Used => $"PDB: {Path.GetFileName(index.PdbPath)} を使用 — {index.PdbMessage}",
        PdbStatus.NoSymbols => $"PDB: {Path.GetFileName(index.PdbPath)} — {index.PdbMessage}",
        PdbStatus.Failed => $"PDB: {Path.GetFileName(index.PdbPath)} を読めませんでした — {index.PdbMessage}",
        _ => index.LibraryPath.Length > 0
            ? "PDB: 同じディレクトリに PDB はありません。サイズは .lib から求めています。"
            : string.Empty,
    };

    public async Task LoadAsync(string path)
    {
        if (IsBusy)
        {
            return;
        }

        BeginBusy();
        ErrorMessage = null;
        StatusMessage = $"{Path.GetFileName(path)} のシンボルを解析中…";

        try
        {
            bool withPdb = usePdb;
            SymbolIndex result = await Task.Run(() => analyzer.Analyze(path, withPdb));

            index = result;
            OnPropertyChanged(nameof(LibraryPath));
            OnPropertyChanged(nameof(HasSymbols));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(PdbText));

            SelectedNode = null;
            HoveredNode = null;
            await RebuildAsync(resetZoom: true);

            StatusMessage = result.Warnings.Count > 0
                ? $"{Path.GetFileName(path)} から {result.Count:N0} 件のシンボルを読みました ({result.Warnings.Count} 件の警告)"
                : $"{Path.GetFileName(path)} から {result.Count:N0} 件のシンボルを読みました";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"シンボルの解析に失敗しました: {ex.Message}";
            StatusMessage = "シンボルの解析に失敗しました。";
        }
        finally
        {
            EndBusy();
        }
    }

    public void ZoomInto(TreeNode node)
    {
        if (!node.IsLeaf)
        {
            DisplayRoot = node;
        }
    }

    private void RebuildTree() => _ = RebuildAsync(resetZoom: false);

    private async Task RebuildAsync(bool resetZoom)
    {
        if (index.Count == 0)
        {
            fullTree = null;
            DisplayRoot = null;
            return;
        }

        int generation = ++buildGeneration;
        SymbolIndex target = index;
        var options = new SymbolTreeOptions
        {
            Kinds = kindOption.Filter,
            Filter = filterText,
        };

        TreeNode? previousRoot = displayRoot;
        BeginBusy();

        try
        {
            TreeNode tree = await Task.Run(() => SymbolTreeBuilder.Build(target, options));

            if (generation != buildGeneration || !ReferenceEquals(index, target))
            {
                return;
            }

            fullTree = tree;
            SelectedNode = null;
            HoveredNode = null;

            DisplayRoot = resetZoom || previousRoot is null
                ? tree
                : FindByPath(tree, previousRoot) ?? tree;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"表示の組み立てに失敗しました: {ex.Message}";
        }
        finally
        {
            EndBusy();
        }
    }

    private static TreeNode? FindByPath(TreeNode root, TreeNode previous)
    {
        TreeNode current = root;

        foreach (TreeNode step in previous.PathFromRoot.Skip(1))
        {
            TreeNode? next = current.Children.FirstOrDefault(c => c.Name == step.Name);
            if (next is null)
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    private void UpdateBreadcrumb()
    {
        Breadcrumb.Clear();
        if (displayRoot is null)
        {
            return;
        }

        IReadOnlyList<TreeNode> path = displayRoot.PathFromRoot;
        for (int i = 0; i < path.Count; i++)
        {
            Breadcrumb.Add(new BreadcrumbItem(path[i].Name, path[i], NavigateCommand, i < path.Count - 1));
        }
    }

    private void UpdateRanked()
    {
        RankedSymbols.Clear();
        if (displayRoot is null)
        {
            return;
        }

        List<TreeNode> leaves = EnumerateLeaves(displayRoot)
            .Where(n => n.Symbol is not null)
            .OrderByDescending(n => n.Size)
            .Take(RankedCount)
            .ToList();

        long total = displayRoot.Size;
        long max = leaves.Count > 0 ? leaves[0].Size : 0;
        int rank = 1;

        foreach (TreeNode leaf in leaves)
        {
            SymbolInfo symbol = leaf.Symbol!;

            RankedSymbols.Add(new SymbolRow(
                rank++,
                Shorten(symbol.LeafName, 46),
                Shorten(symbol.NamespaceText, 46),
                ByteSize.Format(symbol.Size),
                ByteSize.FormatPercent(symbol.Size, total),
                $"{symbol.ObjectName} / {symbol.SectionName}",
                DescribeSource(symbol.SizeSource),
                max > 0 ? Math.Max(2, symbol.Size * 110.0 / max) : 2,
                leaf,
                SelectCommand));
        }
    }

    private void UpdateDetails()
    {
        Details.Clear();

        if (selectedNode is null)
        {
            return;
        }

        Details.Add(new DetailItem("サイズ", $"{ByteSize.Format(selectedNode.Size)} ({ByteSize.FormatExact(selectedNode.Size)})"));
        Details.Add(new DetailItem("表示中の全体に対する比率", ByteSize.FormatPercent(selectedNode.Size, displayRoot?.Size ?? 0)));

        if (selectedNode.Symbol is { } symbol)
        {
            Details.Add(new DetailItem("名前空間", symbol.NamespaceText));
            Details.Add(new DetailItem("デマングル後", symbol.DisplayName));
            Details.Add(new DetailItem("マングル名", symbol.MangledName));
            Details.Add(new DetailItem("種別", symbol.Kind == SymbolKind.Function ? "関数" : "データ"));
            Details.Add(new DetailItem("オブジェクト", symbol.ObjectName));
            Details.Add(new DetailItem("セクション", $"{symbol.SectionName} (+0x{symbol.Offset:X})"));
            Details.Add(new DetailItem("COMDAT", symbol.IsComdat ? "はい" : "いいえ"));
            Details.Add(new DetailItem("リンケージ", symbol.IsStatic ? "内部 (static)" : "外部"));
            Details.Add(new DetailItem("サイズの出所", DescribeSource(symbol.SizeSource)));
        }
        else
        {
            Details.Add(new DetailItem("シンボル数", $"{selectedNode.LeafCount:N0} 件"));
            Details.Add(new DetailItem("パス", string.Join(" › ", selectedNode.PathFromRoot.Select(n => n.Name))));
        }
    }

    /// <summary>
    /// 長すぎる名前を真ん中で詰める。テンプレート名は数百文字になることがあり、
    /// Label の省略表示に任せるとほとんど読めなくなるため、あらかじめ切っておく。
    /// </summary>
    private static string Shorten(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        int head = (maxLength * 2 / 3) - 1;
        int tail = maxLength - head - 1;
        return string.Concat(text.AsSpan(0, head), "…", text.AsSpan(text.Length - tail));
    }

    public static string DescribeSource(SymbolSizeSource source) => source switch
    {
        SymbolSizeSource.Comdat => "COMDAT セクション (正確)",
        SymbolSizeSource.Pdb => "PDB の関数レコード (正確)",
        _ => "次のシンボルとの距離 (概算)",
    };

    private static IEnumerable<TreeNode> EnumerateLeaves(TreeNode node)
    {
        if (node.IsLeaf)
        {
            yield return node;
            yield break;
        }

        foreach (TreeNode child in node.Children)
        {
            foreach (TreeNode leaf in EnumerateLeaves(child))
            {
                yield return leaf;
            }
        }
    }

    private void RaiseCommandStates()
    {
        (ZoomOutCommand as Command)?.ChangeCanExecute();
        (ResetZoomCommand as Command)?.ChangeCanExecute();
    }

    private void BeginBusy()
    {
        busyCount++;
        IsBusy = true;
    }

    private void EndBusy()
    {
        busyCount = Math.Max(0, busyCount - 1);
        if (busyCount == 0)
        {
            IsBusy = false;
        }
    }
}

/// <summary>シンボル解析の呼び出し口 (テストや差し替えのために薄く包む)。</summary>
public sealed class SymbolAnalyzerService
{
    public SymbolIndex Analyze(string libraryPath, bool usePdb) =>
        SymbolAnalyzer.Analyze(libraryPath, new SymbolAnalysisOptions { UsePdb = usePdb });
}
