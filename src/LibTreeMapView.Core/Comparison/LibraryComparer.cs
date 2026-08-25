using LibTreeMapView.Core.Model;

namespace LibTreeMapView.Core.Comparison;

/// <summary>
/// 2 つのライブラリをオブジェクトファイル名で突き合わせ、セクションごとのサイズ差を出す。
/// 同じ名前のメンバーやセクション (COMDAT が複数ある場合など) は合算して比べる。
/// </summary>
public static class LibraryComparer
{
    public static LibraryDiff Compare(LibraryInfo baseline, LibraryInfo target, LibraryCompareOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(target);

        options ??= new LibraryCompareOptions();

        Dictionary<string, Dictionary<string, SectionTotal>> left = Collect(baseline, options);
        Dictionary<string, Dictionary<string, SectionTotal>> right = Collect(target, options);

        var objects = new List<ObjectDiff>(Math.Max(left.Count, right.Count));

        foreach (string name in left.Keys.Union(right.Keys, StringComparer.OrdinalIgnoreCase))
        {
            bool inBaseline = left.TryGetValue(name, out Dictionary<string, SectionTotal>? baselineSections);
            bool inTarget = right.TryGetValue(name, out Dictionary<string, SectionTotal>? targetSections);

            List<SectionDiff> sections = CompareSections(baselineSections, targetSections);

            if (sections.Count == 0)
            {
                // 比較対象のセクションが両方とも無い (リンカーメンバーなど)。並べても情報がない。
                continue;
            }

            objects.Add(new ObjectDiff(
                name,
                inBaseline,
                inTarget,
                sections.Sum(s => s.BaselineSize),
                sections.Sum(s => s.TargetSize),
                sections));
        }

        // 影響の大きいものから見たいので、差分の絶対値が大きい順に並べる。
        objects.Sort(static (a, b) =>
        {
            int byDelta = Math.Abs(b.Delta).CompareTo(Math.Abs(a.Delta));
            return byDelta != 0 ? byDelta : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return new LibraryDiff(baseline, target, objects);
    }

    private static List<SectionDiff> CompareSections(
        Dictionary<string, SectionTotal>? baseline,
        Dictionary<string, SectionTotal>? target)
    {
        IEnumerable<string> names = (baseline?.Keys ?? Enumerable.Empty<string>())
            .Union(target?.Keys ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

        var sections = new List<SectionDiff>();

        foreach (string name in names)
        {
            SectionTotal baselineTotal = baseline is not null && baseline.TryGetValue(name, out SectionTotal b)
                ? b
                : default;
            SectionTotal targetTotal = target is not null && target.TryGetValue(name, out SectionTotal t)
                ? t
                : default;

            sections.Add(new SectionDiff(
                name,
                targetTotal.Size > 0 ? targetTotal.Kind : baselineTotal.Kind,
                baselineTotal.Size,
                targetTotal.Size));
        }

        sections.Sort(static (a, b) =>
        {
            int byDelta = Math.Abs(b.Delta).CompareTo(Math.Abs(a.Delta));
            if (byDelta != 0)
            {
                return byDelta;
            }

            int bySize = Math.Max(b.BaselineSize, b.TargetSize).CompareTo(Math.Max(a.BaselineSize, a.TargetSize));
            return bySize != 0 ? bySize : string.CompareOrdinal(a.Name, b.Name);
        });

        return sections;
    }

    /// <summary>オブジェクト名 → セクション名 → 合計サイズ。</summary>
    private static Dictionary<string, Dictionary<string, SectionTotal>> Collect(
        LibraryInfo library,
        LibraryCompareOptions options)
    {
        var result = new Dictionary<string, Dictionary<string, SectionTotal>>(StringComparer.OrdinalIgnoreCase);

        foreach (ObjectFileInfo obj in library.Objects)
        {
            if (!result.TryGetValue(obj.ShortName, out Dictionary<string, SectionTotal>? sections))
            {
                sections = new Dictionary<string, SectionTotal>(StringComparer.Ordinal);
                result.Add(obj.ShortName, sections);
            }

            foreach (SectionInfo section in obj.Sections)
            {
                if (section.Size <= 0 ||
                    (!options.IncludeMetadata && section.Kind == SectionKind.Metadata) ||
                    (!options.IncludeUninitialized && section.IsUninitialized))
                {
                    continue;
                }

                Add(sections, section.Name, section.Kind, section.Size);
            }

            if (options.IncludeMetadata && obj.MetadataSize > 0 && obj.Kind is ObjectFileKind.Coff or ObjectFileKind.BigObj)
            {
                Add(sections, MetadataSectionName, SectionKind.Metadata, obj.MetadataSize);
            }
        }

        return result;
    }

    /// <summary>単一表示の「メタデータを含める」と同じ扱いにするための名前。</summary>
    public const string MetadataSectionName = "(シンボル・ヘッダー・再配置)";

    private static void Add(Dictionary<string, SectionTotal> sections, string name, SectionKind kind, long size)
    {
        sections[name] = sections.TryGetValue(name, out SectionTotal existing)
            ? existing with { Size = existing.Size + size }
            : new SectionTotal(kind, size);
    }

    private readonly record struct SectionTotal(SectionKind Kind, long Size);
}
