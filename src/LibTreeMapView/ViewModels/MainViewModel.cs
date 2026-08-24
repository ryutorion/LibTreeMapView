using System.Collections.ObjectModel;
using System.Windows.Input;
using LibTreeMapView.Core.Coff;
using LibTreeMapView.Core.Model;
using LibTreeMapView.Core.Tree;
using LibTreeMapView.Drawing;

namespace LibTreeMapView.ViewModels;

/// <summary>メイン画面の状態。</summary>
public sealed class MainViewModel : ObservableObject
{
    private const int RankedItemCount = 20;

    private static readonly FilePickerFileType LibFileType = new(
        new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            // GNU ar の .a も署名は同じだがメンバーは ELF なので対象外。
            [DevicePlatform.WinUI] = [".lib"],
        });

    private LibraryInfo? library;
    private TreeNode? fullTree;
    private TreeNode? displayRoot;
    private TreeNode? selectedNode;
    private TreeNode? hoveredNode;
    private GroupingOption groupingOption;
    private bool includeMetadata;
    private bool includeUninitialized = true;
    private string filterText = string.Empty;
    private bool isBusy;
    private bool isLoading;
    private int busyCount;
    private int rebuildGeneration;
    private string statusMessage = ".lib ファイルを開いてください。ウィンドウにドラッグ＆ドロップもできます。";
    private string? errorMessage;

    public MainViewModel()
    {
        groupingOption = GroupingOptions[0];

        OpenCommand = new Command(async () => await OpenAsync(), () => !IsBusy);
        ReloadCommand = new Command(async () => await ReloadAsync(), () => !IsBusy && library is not null);
        ZoomOutCommand = new Command(ZoomOut, () => displayRoot?.Parent is not null);
        ResetZoomCommand = new Command(ResetZoom, () => displayRoot is not null && !ReferenceEquals(displayRoot, fullTree));
        NavigateCommand = new Command<TreeNode>(Navigate);
        SelectCommand = new Command<TreeNode>(node => SelectedNode = node);
    }

    /// <summary>ツリーの内容が入れ替わったので再描画が必要。</summary>
    public event EventHandler? TreeChanged;

    /// <summary>選択・ホバーだけが変わったので再描画が必要。</summary>
    public event EventHandler? HighlightChanged;

    public IReadOnlyList<GroupingOption> GroupingOptions { get; } =
    [
        new("セクション → オブジェクト", GroupingMode.SectionThenObject),
        new("オブジェクト → セクション", GroupingMode.ObjectThenSection),
        new("セクション名 (COMDAT 単位) → オブジェクト", GroupingMode.SectionNameThenObject),
    ];

    public ObservableCollection<BreadcrumbItem> Breadcrumb { get; } = [];

    public ObservableCollection<RankedItem> RankedItems { get; } = [];

    public ObservableCollection<DetailItem> Details { get; } = [];

    public ObservableCollection<LegendItem> Legend { get; } = [];

    public ICommand OpenCommand { get; }

    public ICommand ReloadCommand { get; }

    public ICommand ZoomOutCommand { get; }

    public ICommand ResetZoomCommand { get; }

    public ICommand NavigateCommand { get; }

    public ICommand SelectCommand { get; }

    public LibraryInfo? Library
    {
        get => library;
        private set
        {
            if (SetProperty(ref library, value))
            {
                OnPropertyChanged(nameof(HasLibrary));
                OnPropertyChanged(nameof(FilePath));
                OnPropertyChanged(nameof(SummaryText));
                OnPropertyChanged(nameof(WarningText));
                OnPropertyChanged(nameof(HasWarnings));
            }
        }
    }

    public bool HasLibrary => library is not null;

    public string FilePath => library?.FilePath ?? string.Empty;

    /// <summary>ツリーマップに描画するノード (ズーム中はその部分木)。</summary>
    public TreeNode? DisplayRoot
    {
        get => displayRoot;
        private set
        {
            if (SetProperty(ref displayRoot, value))
            {
                OnPropertyChanged(nameof(DisplayRootText));
                UpdateBreadcrumb();
                UpdateRankedItems();
                UpdateLegend();
                RaiseCommandStates();
                TreeChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string DisplayRootText => displayRoot is null
        ? string.Empty
        : $"{displayRoot.Name} — {ByteSize.Format(displayRoot.Size)} / {displayRoot.LeafCount:N0} 個のセクション";

    public TreeNode? SelectedNode
    {
        get => selectedNode;
        set
        {
            if (SetProperty(ref selectedNode, value))
            {
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
        : $"{DescribePath(hoveredNode)}\n{ByteSize.Format(hoveredNode.Size)}  ({ByteSize.FormatPercent(hoveredNode.Size, fullTree?.Size ?? 0)})";

    public GroupingOption SelectedGrouping
    {
        get => groupingOption;
        set
        {
            if (value is not null && SetProperty(ref groupingOption, value))
            {
                QueueRebuild();
            }
        }
    }

    public bool IncludeMetadata
    {
        get => includeMetadata;
        set
        {
            if (SetProperty(ref includeMetadata, value))
            {
                QueueRebuild();
            }
        }
    }

    public bool IncludeUninitialized
    {
        get => includeUninitialized;
        set
        {
            if (SetProperty(ref includeUninitialized, value))
            {
                QueueRebuild();
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
                QueueRebuild();
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

    public string SummaryText
    {
        get
        {
            if (library is null)
            {
                return string.Empty;
            }

            string machines = library.Machines.Count > 0 ? string.Join(", ", library.Machines) : "不明";
            return $"ファイル {ByteSize.Format(library.FileSize)} ／ オブジェクト {library.ObjectCount:N0} 個 ／ " +
                   $"セクション {library.SectionCount:N0} 個 ／ アーキテクチャ {machines}";
        }
    }

    public bool HasWarnings => library is { Warnings.Count: > 0 };

    public string WarningText => library is null || library.Warnings.Count == 0
        ? string.Empty
        : string.Join(Environment.NewLine, library.Warnings);

    public async Task OpenAsync()
    {
        try
        {
            FileResult? result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "静的ライブラリ (.lib) を選択",
                FileTypes = LibFileType,
            });

            if (result is not null)
            {
                await LoadAsync(result.FullPath);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"ファイルの選択に失敗しました: {ex.Message}";
        }
    }

    public async Task LoadAsync(string path)
    {
        if (isLoading)
        {
            return;
        }

        isLoading = true;
        BeginBusy();
        ErrorMessage = null;
        StatusMessage = $"{Path.GetFileName(path)} を解析中…";

        try
        {
            LibraryInfo info = await Task.Run(() => LibReader.Read(path));

            Library = info;
            SelectedNode = null;
            HoveredNode = null;
            await RebuildAsync(resetZoom: true);
            StatusMessage = info.Warnings.Count > 0
                ? $"{info.FileName} を読み込みました ({info.Warnings.Count} 件の警告)"
                : $"{info.FileName} を読み込みました";
        }
        catch (Exception ex)
        {
            Library = null;
            fullTree = null;
            DisplayRoot = null;
            ErrorMessage = ex is LibFormatException or FileNotFoundException
                ? ex.Message
                : $"読み込みに失敗しました: {ex.Message}";
            StatusMessage = "読み込みに失敗しました。";
        }
        finally
        {
            isLoading = false;
            EndBusy();
        }
    }

    public Task ReloadAsync() => library is null ? Task.CompletedTask : LoadAsync(library.FilePath);

    /// <summary>タイルをダブルクリックしたときのズームイン。</summary>
    public void ZoomInto(TreeNode node)
    {
        if (!node.IsLeaf)
        {
            DisplayRoot = node;
        }
    }

    public void ZoomOut()
    {
        if (displayRoot?.Parent is { } parent)
        {
            DisplayRoot = parent;
        }
    }

    public void ResetZoom()
    {
        if (fullTree is not null)
        {
            DisplayRoot = fullTree;
        }
    }

    private void Navigate(TreeNode? node)
    {
        if (node is not null && !node.IsLeaf)
        {
            DisplayRoot = node;
        }
    }

    /// <summary>表示オプションが変わったときの作り直し。結果を待たずに戻る。</summary>
    private void QueueRebuild() => _ = RebuildAsync(resetZoom: false);

    /// <summary>
    /// ツリーを組み立て直す。大きなライブラリでは時間がかかるので UI スレッドでは動かさない。
    /// 途中で新しい要求が来た場合、古い結果は捨てる。
    /// </summary>
    private async Task RebuildAsync(bool resetZoom)
    {
        if (library is null)
        {
            fullTree = null;
            DisplayRoot = null;
            return;
        }

        int generation = ++rebuildGeneration;
        LibraryInfo target = library;
        var options = new TreeBuildOptions
        {
            Mode = groupingOption.Mode,
            IncludeMetadata = includeMetadata,
            IncludeUninitialized = includeUninitialized,
            Filter = filterText,
        };

        TreeNode? previousRoot = displayRoot;
        BeginBusy();

        try
        {
            TreeNode tree = await Task.Run(() => TreeBuilder.Build(target, options));

            if (generation != rebuildGeneration || !ReferenceEquals(library, target))
            {
                return; // より新しい要求が走っている、または別のライブラリに切り替わった
            }

            fullTree = tree;
            SelectedNode = null;
            HoveredNode = null;

            DisplayRoot = resetZoom || previousRoot is null
                ? tree
                // 同じ名前のノードが残っていればズーム位置を保つ。
                : FindByPath(tree, previousRoot) ?? tree;
        }
        catch (Exception ex)
        {
            if (generation == rebuildGeneration)
            {
                ErrorMessage = $"表示の組み立てに失敗しました: {ex.Message}";
            }
        }
        finally
        {
            EndBusy();
        }
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

    private void UpdateRankedItems()
    {
        RankedItems.Clear();
        if (displayRoot is null)
        {
            return;
        }

        long total = displayRoot.Size;
        int rank = 1;

        foreach (TreeNode leaf in EnumerateLeaves(displayRoot).OrderByDescending(n => n.Size).Take(RankedItemCount))
        {
            RankedItems.Add(new RankedItem(
                rank++,
                leaf.Name,
                LeafSubtitle(leaf),
                ByteSize.Format(leaf.Size),
                ByteSize.FormatPercent(leaf.Size, total),
                leaf,
                SelectCommand));
        }
    }

    private void UpdateLegend()
    {
        Legend.Clear();
        if (displayRoot is null)
        {
            return;
        }

        var totals = new Dictionary<SectionKind, long>();
        foreach (TreeNode leaf in EnumerateLeaves(displayRoot))
        {
            totals[leaf.SectionKind] = totals.GetValueOrDefault(leaf.SectionKind) + leaf.Size;
        }

        long total = totals.Values.Sum();
        long max = totals.Values.DefaultIfEmpty(0).Max();

        foreach ((SectionKind kind, long size) in totals.OrderByDescending(e => e.Value))
        {
            Legend.Add(new LegendItem(
                kind,
                SectionPalette.GetName(kind),
                SectionPalette.GetColor(kind),
                ByteSize.Format(size),
                ByteSize.FormatPercent(size, total),
                max > 0 ? Math.Max(2, size * 120.0 / max) : 2));
        }
    }

    private void UpdateDetails()
    {
        Details.Clear();
        OnPropertyChanged(nameof(SelectedTitle));

        if (selectedNode is null)
        {
            return;
        }

        TreeNode node = selectedNode;
        Details.Add(new DetailItem("サイズ", $"{ByteSize.Format(node.Size)} ({ByteSize.FormatExact(node.Size)})"));
        Details.Add(new DetailItem("表示中の全体に対する比率", ByteSize.FormatPercent(node.Size, displayRoot?.Size ?? 0)));
        Details.Add(new DetailItem("ライブラリ全体に対する比率", ByteSize.FormatPercent(node.Size, fullTree?.Size ?? 0)));
        Details.Add(new DetailItem("パス", DescribePath(node)));

        if (!node.IsLeaf)
        {
            Details.Add(new DetailItem("子ノード", $"{node.Children.Count:N0} 個 (末端 {node.LeafCount:N0} 個)"));
        }

        if (node.Section is { } section)
        {
            Details.Add(new DetailItem("セクション名", section.Name));
            Details.Add(new DetailItem("種別", SectionPalette.GetName(section.Kind)));
            Details.Add(new DetailItem("属性", $"{section.Attributes}  (Characteristics: 0x{section.Characteristics:X8})"));
            Details.Add(new DetailItem("COMDAT", section.IsComdat ? "はい" : "いいえ"));
            if (section.IsUninitialized)
            {
                Details.Add(new DetailItem("実体", "ファイル上にデータを持たない (.bss 相当)"));
            }

            if (section.IsSynthetic)
            {
                Details.Add(new DetailItem("備考", "セクションヘッダーではなく、解析結果を表すために作った項目です。"));
            }

            if (section.RelocationCount > 0)
            {
                Details.Add(new DetailItem(
                    "再配置",
                    $"{section.RelocationCount:N0} 件 ({ByteSize.Format(section.RelocationBytes)})"));
            }
        }

        if (node.ObjectFile is { } obj)
        {
            Details.Add(new DetailItem("オブジェクト", obj.Name));
            Details.Add(new DetailItem("アーカイブ上のサイズ", ByteSize.Format(obj.MemberSize)));
            Details.Add(new DetailItem("アーキテクチャ", obj.MachineName));
            Details.Add(new DetailItem("シンボル数", $"{obj.SymbolCount:N0}"));
            if (obj.ImportDllName is { Length: > 0 } dll)
            {
                Details.Add(new DetailItem("インポート元 DLL", dll));
            }

            if (obj.Warning is { Length: > 0 } warning)
            {
                Details.Add(new DetailItem("警告", warning));
            }
        }
    }

    public string SelectedTitle => selectedNode?.Name ?? "タイルを選択してください";

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

    private static string LeafSubtitle(TreeNode leaf)
    {
        IReadOnlyList<TreeNode> path = leaf.PathFromRoot;
        return path.Count <= 1 ? string.Empty : string.Join(" › ", path.Skip(1).Take(path.Count - 2).Select(n => n.Name));
    }

    private static string DescribePath(TreeNode node) =>
        string.Join(" › ", node.PathFromRoot.Select(n => n.Name));

    private void RaiseCommandStates()
    {
        (OpenCommand as Command)?.ChangeCanExecute();
        (ReloadCommand as Command)?.ChangeCanExecute();
        (ZoomOutCommand as Command)?.ChangeCanExecute();
        (ResetZoomCommand as Command)?.ChangeCanExecute();
    }
}
