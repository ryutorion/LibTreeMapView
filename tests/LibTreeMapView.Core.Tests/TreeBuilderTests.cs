using LibTreeMapView.Core.Coff;
using LibTreeMapView.Core.Model;
using LibTreeMapView.Core.Tree;

namespace LibTreeMapView.Core.Tests;

public class TreeBuilderTests
{
    private static LibraryInfo ReadFixture() =>
        LibReader.Read(Path.Combine(AppContext.BaseDirectory, "TestData", "fixture.lib"));

    [Fact]
    public void Build_SectionFirst_GroupsBySectionGroupName()
    {
        TreeNode root = TreeBuilder.Build(ReadFixture(), new TreeBuildOptions { Mode = GroupingMode.SectionThenObject });

        Assert.Contains(root.Children, c => c.Name == ".text");
        Assert.Contains(root.Children, c => c.Name == ".debug");
        Assert.DoesNotContain(root.Children, c => c.Name.Contains('$'));

        // .text は alpha (0x72) + gamma (0x3A) + delta の COMDAT 3 本 (0xB + 0xC + 0xC)。
        TreeNode text = root.Children.Single(c => c.Name == ".text");
        Assert.Equal(0x72 + 0x3A + 0xB + 0xC + 0xC, text.Size);
    }

    [Fact]
    public void Build_SectionNameFirst_KeepsComdatSuffix()
    {
        TreeNode root = TreeBuilder.Build(ReadFixture(), new TreeBuildOptions { Mode = GroupingMode.SectionNameThenObject });

        Assert.Contains(root.Children, c => c.Name == ".text$mn");
        Assert.Contains(root.Children, c => c.Name == ".debug$S");
    }

    [Fact]
    public void Build_ObjectFirst_GroupsByObjectFile()
    {
        TreeNode root = TreeBuilder.Build(ReadFixture(), new TreeBuildOptions { Mode = GroupingMode.ObjectThenSection });

        Assert.Contains(root.Children, c => c.Name == "alpha.obj");
        Assert.Contains(root.Children, c => c.Name == "gamma.obj");

        TreeNode alpha = root.Children.Single(c => c.Name == "alpha.obj");
        Assert.Contains(alpha.Children, c => c.Name == ".bss" && c.Size == 0x400);
    }

    [Fact]
    public void Build_ChildSizesRollUpToParents()
    {
        TreeNode root = TreeBuilder.Build(ReadFixture(), new TreeBuildOptions());

        Assert.Equal(root.Children.Sum(c => c.Size), root.Size);
        Assert.All(root.Children, group =>
            Assert.Equal(group.Children.Sum(c => c.Size), group.Size));
    }

    [Fact]
    public void Build_ChildrenAreSortedBySizeDescending()
    {
        TreeNode root = TreeBuilder.Build(ReadFixture(), new TreeBuildOptions());

        List<long> sizes = root.Children.Select(c => c.Size).ToList();
        Assert.Equal(sizes.OrderByDescending(s => s).ToList(), sizes);
    }

    [Fact]
    public void Build_ExcludesMetadataByDefaultAndIncludesItOnRequest()
    {
        LibraryInfo library = ReadFixture();

        TreeNode without = TreeBuilder.Build(library, new TreeBuildOptions { IncludeMetadata = false });
        TreeNode with = TreeBuilder.Build(library, new TreeBuildOptions { IncludeMetadata = true });

        Assert.DoesNotContain(without.Children, c => c.Name.Contains("メタデータ"));
        Assert.Contains(with.Children, c => c.Name.Contains("メタデータ"));
        Assert.True(with.Size > without.Size);

        // メタデータを含め .bss を除くと、合計はファイルサイズに近づく (メンバーヘッダー分だけ小さい)。
        TreeNode onDisk = TreeBuilder.Build(library, new TreeBuildOptions
        {
            IncludeMetadata = true,
            IncludeUninitialized = false,
        });
        Assert.InRange(onDisk.Size, library.FileSize * 0.95, library.FileSize);
    }

    [Fact]
    public void Build_CanExcludeUninitializedSections()
    {
        LibraryInfo library = ReadFixture();

        TreeNode with = TreeBuilder.Build(library, new TreeBuildOptions { IncludeUninitialized = true });
        TreeNode without = TreeBuilder.Build(library, new TreeBuildOptions { IncludeUninitialized = false });

        Assert.Contains(with.Children, c => c.Name == ".bss");
        Assert.DoesNotContain(without.Children, c => c.Name == ".bss");
        Assert.Equal(0x400, with.Size - without.Size);
    }

    [Fact]
    public void Build_FilterMatchesObjectNames()
    {
        TreeNode root = TreeBuilder.Build(ReadFixture(), new TreeBuildOptions
        {
            Mode = GroupingMode.ObjectThenSection,
            Filter = "gamma",
        });

        Assert.Single(root.Children);
        Assert.Equal("gamma.obj", root.Children[0].Name);
    }

    [Fact]
    public void Build_SingleSectionObjectBecomesLeafNamedAfterTheObject()
    {
        TreeNode root = TreeBuilder.Build(ReadFixture(), new TreeBuildOptions { Mode = GroupingMode.SectionThenObject });

        TreeNode text = root.Children.Single(c => c.Name == ".text");

        // 単一セクションの alpha/gamma は葉、COMDAT が 3 本ある delta は中間ノードになる。
        TreeNode alpha = text.Children.Single(c => c.Name == "alpha.obj");
        Assert.True(alpha.IsLeaf);
        Assert.NotNull(alpha.Section);

        TreeNode delta = text.Children.Single(c => c.Name == "delta.obj");
        Assert.Equal(3, delta.Children.Count);
        Assert.All(delta.Children, c => Assert.Equal(".text$mn", c.Name));
    }

    [Fact]
    public void TreeNode_PathFromRootWalksUpTheTree()
    {
        TreeNode root = TreeBuilder.Build(ReadFixture(), new TreeBuildOptions { Mode = GroupingMode.ObjectThenSection });
        TreeNode leaf = root.Children[0].Children[0];

        IReadOnlyList<TreeNode> path = leaf.PathFromRoot;

        Assert.Equal(3, path.Count);
        Assert.Same(root, path[0]);
        Assert.Same(leaf, path[^1]);
        Assert.Equal(2, leaf.Depth);
    }
}
