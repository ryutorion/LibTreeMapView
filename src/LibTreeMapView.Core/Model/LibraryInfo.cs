namespace LibTreeMapView.Core.Model;

/// <summary>1 つの .lib ファイルの解析結果。</summary>
public sealed class LibraryInfo
{
    public required string FilePath { get; init; }

    public required long FileSize { get; init; }

    public required IReadOnlyList<ObjectFileInfo> Objects { get; init; }

    /// <summary>解析中の警告 (スキップしたメンバーなど)。</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    public string FileName => System.IO.Path.GetFileName(FilePath);

    public long TotalSectionSize => Objects.Sum(o => o.TotalSectionSize);

    public int SectionCount => Objects.Sum(o => o.Sections.Count);

    public int ObjectCount => Objects.Count;

    /// <summary>ライブラリに含まれるアーキテクチャの一覧。</summary>
    public IReadOnlyList<string> Machines => Objects
        .Where(o => o.Machine != 0)
        .Select(o => o.MachineName)
        .Distinct()
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToList();
}
