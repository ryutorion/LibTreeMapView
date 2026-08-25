using LibTreeMapView.Core.Model;
using LibTreeMapView.Core.Tree;

namespace LibTreeMapView.Core.Symbols;

/// <summary>シンボルの絞り込み方。</summary>
public enum SymbolKindFilter
{
    All,
    FunctionsOnly,
    DataOnly,
}

/// <summary>名前空間ツリーの組み立て条件。</summary>
public sealed record SymbolTreeOptions
{
    public SymbolKindFilter Kinds { get; init; } = SymbolKindFilter.All;

    /// <summary>名前 (デマングル後・オブジェクト名) の部分一致フィルター。</summary>
    public string? Filter { get; init; }

    /// <summary>これより小さいシンボルは除く。</summary>
    public long MinimumSize { get; init; }
}

/// <summary>
/// シンボルを名前空間・クラスの階層に積み上げて、ツリーマップで描ける形にする。
/// </summary>
public static class SymbolTreeBuilder
{
    public static TreeNode Build(SymbolIndex index, SymbolTreeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(index);
        options ??= new SymbolTreeOptions();

        var root = new Bucket();

        foreach (SymbolInfo symbol in Filter(index.Symbols, options))
        {
            Bucket bucket = root;
            foreach (string segment in symbol.NamespacePath)
            {
                bucket = bucket.Child(segment);
            }

            bucket.Symbols.Add(symbol);
        }

        List<TreeNode> children = root.BuildChildren();
        children.Sort(static (a, b) => b.Size.CompareTo(a.Size));

        string name = Path.GetFileName(index.LibraryPath) is { Length: > 0 } fileName ? fileName : "シンボル";
        return new TreeNode(name, TreeNodeKind.Root, SectionKind.Other, 0, children);
    }

    public static IEnumerable<SymbolInfo> Filter(IEnumerable<SymbolInfo> symbols, SymbolTreeOptions options)
    {
        string? filter = string.IsNullOrWhiteSpace(options.Filter) ? null : options.Filter.Trim();

        foreach (SymbolInfo symbol in symbols)
        {
            if (symbol.Size < Math.Max(1, options.MinimumSize))
            {
                continue;
            }

            if (options.Kinds == SymbolKindFilter.FunctionsOnly && symbol.Kind != SymbolKind.Function)
            {
                continue;
            }

            if (options.Kinds == SymbolKindFilter.DataOnly && symbol.Kind != SymbolKind.Data)
            {
                continue;
            }

            if (filter is not null &&
                !symbol.QualifiedName.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !symbol.ObjectName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return symbol;
        }
    }

    /// <summary>組み立て中の 1 階層。</summary>
    private sealed class Bucket
    {
        private readonly Dictionary<string, Bucket> children = new(StringComparer.Ordinal);

        public List<SymbolInfo> Symbols { get; } = [];

        public Bucket Child(string name)
        {
            if (!children.TryGetValue(name, out Bucket? child))
            {
                child = new Bucket();
                children.Add(name, child);
            }

            return child;
        }

        public List<TreeNode> BuildChildren()
        {
            var nodes = new List<TreeNode>(children.Count + Symbols.Count);

            foreach ((string name, Bucket child) in children)
            {
                List<TreeNode> grandChildren = child.BuildChildren();
                if (grandChildren.Count == 0)
                {
                    continue;
                }

                grandChildren.Sort(static (a, b) => b.Size.CompareTo(a.Size));
                nodes.Add(new TreeNode(name, TreeNodeKind.Namespace, DominantKind(grandChildren), 0, grandChildren));
            }

            foreach (SymbolInfo symbol in Symbols)
            {
                nodes.Add(new TreeNode(
                    symbol.LeafName,
                    TreeNodeKind.Symbol,
                    symbol.SectionKind,
                    symbol.Size,
                    symbol: symbol));
            }

            nodes.Sort(static (a, b) => b.Size.CompareTo(a.Size));
            return nodes;
        }

        /// <summary>サイズが最大の種別を、まとまりの代表色にする。</summary>
        private static SectionKind DominantKind(List<TreeNode> nodes)
        {
            var totals = new Dictionary<SectionKind, long>();

            foreach (TreeNode node in nodes)
            {
                totals[node.SectionKind] = totals.GetValueOrDefault(node.SectionKind) + node.Size;
            }

            return totals.Count == 0
                ? SectionKind.Other
                : totals.OrderByDescending(e => e.Value).First().Key;
        }
    }
}
