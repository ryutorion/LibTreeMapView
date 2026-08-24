using LibTreeMapView.Core.Model;

namespace LibTreeMapView.Core.Tree;

/// <summary>ツリーマップの階層の組み立て方。</summary>
public enum GroupingMode
{
    /// <summary>セクション種別 (.text, .rdata ...) → オブジェクトファイル。</summary>
    SectionThenObject,

    /// <summary>オブジェクトファイル → セクション。</summary>
    ObjectThenSection,

    /// <summary>COMDAT を含む完全なセクション名 (.text$mn など) → オブジェクトファイル。</summary>
    SectionNameThenObject,
}

/// <summary>ツリー構築のオプション。</summary>
public sealed record TreeBuildOptions
{
    public GroupingMode Mode { get; init; } = GroupingMode.SectionThenObject;

    /// <summary>アーカイブのメタデータ (シンボルテーブル、ヘッダー、再配置) を含める。</summary>
    public bool IncludeMetadata { get; init; }

    /// <summary>.bss など、ファイル上に実体を持たないセクションを含める。</summary>
    public bool IncludeUninitialized { get; init; } = true;

    /// <summary>オブジェクトファイル名の部分一致フィルター (大文字小文字は区別しない)。</summary>
    public string? Filter { get; init; }
}

/// <summary>解析結果からツリーマップ用の階層を作る。</summary>
public static class TreeBuilder
{
    public static TreeNode Build(LibraryInfo library, TreeBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(options);

        List<(ObjectFileInfo Object, SectionInfo Section)> entries = Collect(library, options);

        List<TreeNode> children = options.Mode switch
        {
            GroupingMode.ObjectThenSection => BuildObjectFirst(entries),
            GroupingMode.SectionNameThenObject => BuildSectionFirst(entries, useFullName: true),
            _ => BuildSectionFirst(entries, useFullName: false),
        };

        children.Sort(static (a, b) => b.Size.CompareTo(a.Size));

        return new TreeNode(library.FileName, TreeNodeKind.Root, SectionKind.Other, 0, children);
    }

    private static List<(ObjectFileInfo Object, SectionInfo Section)> Collect(LibraryInfo library, TreeBuildOptions options)
    {
        string? filter = string.IsNullOrWhiteSpace(options.Filter) ? null : options.Filter.Trim();
        var entries = new List<(ObjectFileInfo, SectionInfo)>();

        foreach (ObjectFileInfo obj in library.Objects)
        {
            if (filter is not null &&
                !obj.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (SectionInfo section in obj.Sections)
            {
                if (section.Size <= 0)
                {
                    continue;
                }

                if (!options.IncludeMetadata && section.Kind == SectionKind.Metadata)
                {
                    continue;
                }

                if (!options.IncludeUninitialized && section.IsUninitialized)
                {
                    continue;
                }

                entries.Add((obj, section));
            }

            if (options.IncludeMetadata && obj.MetadataSize > 0 && obj.Kind is ObjectFileKind.Coff or ObjectFileKind.BigObj)
            {
                entries.Add((obj, CreateMetadataSection(obj)));
            }
        }

        return entries;
    }

    private static SectionInfo CreateMetadataSection(ObjectFileInfo obj) => new()
    {
        Name = "(シンボル・ヘッダー・再配置)",
        GroupName = "(メタデータ)",
        Size = obj.MetadataSize,
        RawDataSize = obj.MetadataSize,
        VirtualSize = 0,
        Characteristics = 0,
        RelocationCount = 0,
        IsUninitialized = false,
        Kind = SectionKind.Metadata,
        IsSynthetic = true,
    };

    private static List<TreeNode> BuildSectionFirst(
        List<(ObjectFileInfo Object, SectionInfo Section)> entries,
        bool useFullName)
    {
        var groups = new List<TreeNode>();

        foreach (var group in entries.GroupBy(e => useFullName ? e.Section.Name : e.Section.GroupName, StringComparer.Ordinal))
        {
            var objectNodes = new List<TreeNode>();

            foreach (var byObject in group.GroupBy(e => e.Object))
            {
                ObjectFileInfo obj = byObject.Key;
                List<TreeNode> sectionNodes = byObject
                    .Select(e => CreateSectionLeaf(e.Object, e.Section))
                    .OrderByDescending(n => n.Size)
                    .ToList();

                objectNodes.Add(sectionNodes.Count == 1
                    ? Rename(sectionNodes[0], obj.ShortName)
                    : new TreeNode(obj.ShortName, TreeNodeKind.ObjectFile, KindOf(byObject.Select(e => e.Section)), 0, sectionNodes, objectFile: obj));
            }

            objectNodes.Sort(static (a, b) => b.Size.CompareTo(a.Size));

            groups.Add(new TreeNode(
                group.Key,
                TreeNodeKind.SectionGroup,
                KindOf(group.Select(e => e.Section)),
                0,
                objectNodes));
        }

        return groups;
    }

    private static List<TreeNode> BuildObjectFirst(List<(ObjectFileInfo Object, SectionInfo Section)> entries)
    {
        var objectNodes = new List<TreeNode>();

        foreach (var byObject in entries.GroupBy(e => e.Object))
        {
            ObjectFileInfo obj = byObject.Key;
            List<TreeNode> sectionNodes = byObject
                .Select(e => CreateSectionLeaf(e.Object, e.Section))
                .OrderByDescending(n => n.Size)
                .ToList();

            objectNodes.Add(new TreeNode(
                obj.ShortName,
                TreeNodeKind.ObjectFile,
                KindOf(byObject.Select(e => e.Section)),
                0,
                sectionNodes,
                objectFile: obj));
        }

        return objectNodes;
    }

    private static TreeNode CreateSectionLeaf(ObjectFileInfo obj, SectionInfo section) =>
        new(section.Name, TreeNodeKind.Section, section.Kind, section.Size, section: section, objectFile: obj);

    private static TreeNode Rename(TreeNode node, string name) =>
        new(name, node.Kind, node.SectionKind, node.Size, section: node.Section, objectFile: node.ObjectFile);

    /// <summary>サイズが最大のセクション種別を代表色として採用する。</summary>
    private static SectionKind KindOf(IEnumerable<SectionInfo> sections) => sections
        .GroupBy(s => s.Kind)
        .OrderByDescending(g => g.Sum(s => s.Size))
        .Select(g => g.Key)
        .FirstOrDefault(SectionKind.Other);
}
