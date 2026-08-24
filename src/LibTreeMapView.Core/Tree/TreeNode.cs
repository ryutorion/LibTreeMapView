using LibTreeMapView.Core.Model;

namespace LibTreeMapView.Core.Tree;

/// <summary>ツリーマップのノード種別。</summary>
public enum TreeNodeKind
{
    Root,
    SectionGroup,
    ObjectFile,
    Section,
}

/// <summary>ツリーマップに描画する 1 ノード。構築後は不変。</summary>
public sealed class TreeNode
{
    private static readonly IReadOnlyList<TreeNode> NoChildren = [];

    public TreeNode(
        string name,
        TreeNodeKind kind,
        SectionKind sectionKind,
        long size,
        IReadOnlyList<TreeNode>? children = null,
        SectionInfo? section = null,
        ObjectFileInfo? objectFile = null)
    {
        Name = name;
        Kind = kind;
        SectionKind = sectionKind;
        Children = children ?? NoChildren;
        Section = section;
        ObjectFile = objectFile;
        Size = children is { Count: > 0 } ? children.Sum(c => c.Size) : size;
        LeafCount = Children.Count == 0 ? 1 : Children.Sum(c => c.LeafCount);

        foreach (TreeNode child in Children)
        {
            child.Parent = this;
        }
    }

    public string Name { get; }

    public TreeNodeKind Kind { get; }

    public SectionKind SectionKind { get; }

    /// <summary>バイト数。子を持つ場合は子の合計。</summary>
    public long Size { get; }

    public IReadOnlyList<TreeNode> Children { get; }

    public TreeNode? Parent { get; private set; }

    /// <summary>葉ノードのときの元セクション。</summary>
    public SectionInfo? Section { get; }

    /// <summary>オブジェクトノード／セクション葉ノードが属するオブジェクトファイル。</summary>
    public ObjectFileInfo? ObjectFile { get; }

    public bool IsLeaf => Children.Count == 0;

    public int Depth
    {
        get
        {
            int depth = 0;
            for (TreeNode? node = Parent; node is not null; node = node.Parent)
            {
                depth++;
            }

            return depth;
        }
    }

    /// <summary>葉の総数。構築時に数えるので参照は O(1)。</summary>
    public int LeafCount { get; }

    /// <summary>ルートからこのノードまでのパス (パンくず用、ルートを含む)。</summary>
    public IReadOnlyList<TreeNode> PathFromRoot
    {
        get
        {
            var path = new List<TreeNode>();
            for (TreeNode? node = this; node is not null; node = node.Parent)
            {
                path.Add(node);
            }

            path.Reverse();
            return path;
        }
    }
}
