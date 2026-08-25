using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using LibTreeMapView.Core;
using LibTreeMapView.Core.Comparison;
using LibTreeMapView.Core.Model;

namespace LibTreeMapView.ViewModels;

/// <summary>2 つのライブラリを比較する画面の状態。</summary>
public sealed class CompareViewModel : ObservableObject
{
    private const double MaxBarWidth = 96;

    private static readonly FilePickerFileType LibFileType = new(
        new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.WinUI] = [".lib"],
        });

    private readonly LibraryLoader loader;

    private LibraryInfo? baseline;
    private LibraryInfo? target;
    private LibraryDiff diff = LibraryDiff.Empty;
    private ObjectDiffRow? selectedObject;
    private bool onlyChangedObjects = true;
    private bool onlyChangedSections = true;
    private bool includeUninitialized = true;
    private bool includeMetadata;
    private string filterText = string.Empty;
    private bool isBusy;
    private int busyCount;
    private int compareGeneration;
    private string statusMessage = "比較する 2 つの .lib を開いてください。";
    private string? errorMessage;

    public CompareViewModel(LibraryLoader loader)
    {
        this.loader = loader;

        OpenBaselineCommand = new Command(async () => await OpenAsync(isBaseline: true), () => !IsBusy);
        OpenTargetCommand = new Command(async () => await OpenAsync(isBaseline: false), () => !IsBusy);
        SwapCommand = new Command(Swap, () => !IsBusy && (baseline is not null || target is not null));
    }

    public ICommand OpenBaselineCommand { get; }

    public ICommand OpenTargetCommand { get; }

    public ICommand SwapCommand { get; }

    public ObservableCollection<ObjectDiffRow> Objects { get; } = [];

    public ObservableCollection<SectionDiffRow> Sections { get; } = [];

    public string BaselinePath => baseline?.FilePath ?? "(未選択)";

    public string TargetPath => target?.FilePath ?? "(未選択)";

    public bool HasDiff => diff.HasResult;

    /// <summary>比較元がすでに読み込まれている。</summary>
    public bool HasBaseline => baseline is not null;

    /// <summary>差分のあるオブジェクトだけを一覧に出す。</summary>
    public bool OnlyChangedObjects
    {
        get => onlyChangedObjects;
        set
        {
            if (SetProperty(ref onlyChangedObjects, value))
            {
                UpdateObjects();
            }
        }
    }

    /// <summary>選んだオブジェクトの中で、差分のあるセクションだけを出す。</summary>
    public bool OnlyChangedSections
    {
        get => onlyChangedSections;
        set
        {
            if (SetProperty(ref onlyChangedSections, value))
            {
                UpdateSections();
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
                QueueCompare();
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
                QueueCompare();
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
                UpdateObjects();
            }
        }
    }

    public ObjectDiffRow? SelectedObject
    {
        get => selectedObject;
        set
        {
            if (SetProperty(ref selectedObject, value))
            {
                OnPropertyChanged(nameof(SelectedObjectTitle));
                UpdateSections();
            }
        }
    }

    public string SelectedObjectTitle => selectedObject is null
        ? "オブジェクトを選択してください"
        : $"{selectedObject.Name} — {selectedObject.DeltaText}";

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                (OpenBaselineCommand as Command)?.ChangeCanExecute();
                (OpenTargetCommand as Command)?.ChangeCanExecute();
                (SwapCommand as Command)?.ChangeCanExecute();
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

    /// <summary>A と B の合計と差分。</summary>
    public string SummaryText
    {
        get
        {
            if (!diff.HasResult)
            {
                return string.Empty;
            }

            string percent = diff.BaselineSize > 0
                ? $" ({(diff.Delta * 100.0 / diff.BaselineSize).ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture)} %)"
                : string.Empty;

            return $"A {ByteSize.Format(diff.BaselineSize)} → B {ByteSize.Format(diff.TargetSize)} ／ " +
                   $"差分 {FormatDelta(diff.Delta)}{percent}";
        }
    }

    /// <summary>状態ごとの件数。</summary>
    public string CountsText => diff.HasResult
        ? $"変更 {diff.ChangedCount:N0} ／ 追加 {diff.AddedCount:N0} ／ 削除 {diff.RemovedCount:N0} ／ 同一 {diff.UnchangedCount:N0}"
        : string.Empty;

    public string ObjectsHeaderText => diff.HasResult
        ? $"オブジェクト ({Objects.Count:N0} / {diff.Objects.Count:N0} 件)"
        : "オブジェクト";

    public async Task LoadAsync(string path, bool isBaseline)
    {
        if (IsBusy)
        {
            return;
        }

        BeginBusy();
        ErrorMessage = null;
        StatusMessage = $"{Path.GetFileName(path)} を読み込み中…";

        try
        {
            LibraryLoadResult result = await Task.Run(() => loader.Load(path));

            if (isBaseline)
            {
                baseline = result.Library;
                OnPropertyChanged(nameof(BaselinePath));
            }
            else
            {
                target = result.Library;
                OnPropertyChanged(nameof(TargetPath));
            }

            string source = result.FromCache ? "キャッシュ" : "解析";
            StatusMessage = $"{result.Library.FileName} を{(isBaseline ? " A " : " B ")}に読み込みました " +
                            $"({source} {result.Elapsed.TotalMilliseconds:F0} ms)";

            if (!result.FromCache)
            {
                _ = Task.Run(() => loader.SaveToCache(result));
            }

            (SwapCommand as Command)?.ChangeCanExecute();
            await CompareAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"読み込みに失敗しました: {ex.Message}";
            StatusMessage = "読み込みに失敗しました。";
        }
        finally
        {
            EndBusy();
        }
    }

    private async Task OpenAsync(bool isBaseline)
    {
        try
        {
            FileResult? result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = isBaseline ? "比較元 (A) の .lib を選択" : "比較先 (B) の .lib を選択",
                FileTypes = LibFileType,
            });

            if (result is not null)
            {
                await LoadAsync(result.FullPath, isBaseline);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"ファイルの選択に失敗しました: {ex.Message}";
        }
    }

    private void Swap()
    {
        (baseline, target) = (target, baseline);
        OnPropertyChanged(nameof(BaselinePath));
        OnPropertyChanged(nameof(TargetPath));
        QueueCompare();
    }

    private void QueueCompare() => _ = CompareAsync();

    private async Task CompareAsync()
    {
        if (baseline is null || target is null)
        {
            return;
        }

        int generation = ++compareGeneration;
        LibraryInfo a = baseline;
        LibraryInfo b = target;
        var options = new LibraryCompareOptions
        {
            IncludeUninitialized = includeUninitialized,
            IncludeMetadata = includeMetadata,
        };

        BeginBusy();

        try
        {
            LibraryDiff result = await Task.Run(() => LibraryComparer.Compare(a, b, options));

            if (generation != compareGeneration)
            {
                return; // より新しい比較が走っている
            }

            diff = result;
            OnPropertyChanged(nameof(HasDiff));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(CountsText));
            UpdateObjects();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"比較に失敗しました: {ex.Message}";
        }
        finally
        {
            EndBusy();
        }
    }

    private void UpdateObjects()
    {
        string? filter = string.IsNullOrWhiteSpace(filterText) ? null : filterText.Trim();

        List<ObjectDiff> visible = diff.Objects
            .Where(o => !onlyChangedObjects || o.IsChanged)
            .Where(o => filter is null || o.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        long max = visible.Count > 0 ? visible.Max(o => Math.Abs(o.Delta)) : 0;

        Objects.Clear();
        foreach (ObjectDiff item in visible)
        {
            Objects.Add(new ObjectDiffRow(
                item.Name,
                DiffPalette.Describe(item.Status),
                DiffPalette.ForStatus(item.Status),
                ByteSize.Format(item.BaselineSize),
                ByteSize.Format(item.TargetSize),
                FormatDelta(item.Delta),
                DiffPalette.ForDelta(item.Delta),
                BarWidth(item.Delta, max),
                item.ChangedSectionCount > 0
                    ? $"差分のあるセクション {item.ChangedSectionCount:N0} / {item.Sections.Count:N0}"
                    : $"セクション {item.Sections.Count:N0}",
                item));
        }

        OnPropertyChanged(nameof(ObjectsHeaderText));

        // 選択していたオブジェクトが消えた場合は先頭に寄せる。
        SelectedObject = Objects.FirstOrDefault(o => o.Name == selectedObject?.Name) ?? Objects.FirstOrDefault();
    }

    private void UpdateSections()
    {
        Sections.Clear();

        if (selectedObject is null)
        {
            return;
        }

        List<SectionDiff> visible = selectedObject.Diff.Sections
            .Where(s => !onlyChangedSections || s.IsChanged)
            .ToList();

        long max = visible.Count > 0 ? visible.Max(s => Math.Abs(s.Delta)) : 0;

        foreach (SectionDiff item in visible)
        {
            Sections.Add(new SectionDiffRow(
                item.Name,
                DiffPalette.Describe(item.Status),
                DiffPalette.ForStatus(item.Status),
                ByteSize.Format(item.BaselineSize),
                ByteSize.Format(item.TargetSize),
                FormatDelta(item.Delta),
                DiffPalette.ForDelta(item.Delta),
                BarWidth(item.Delta, max)));
        }
    }

    private static double BarWidth(long delta, long max) =>
        max > 0 ? Math.Max(2, Math.Abs(delta) * MaxBarWidth / max) : 0;

    private static string FormatDelta(long delta) => delta == 0
        ? "±0"
        : $"{(delta > 0 ? "+" : "-")}{ByteSize.Format(Math.Abs(delta))}";

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
