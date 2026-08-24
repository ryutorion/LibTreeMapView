using LibTreeMapView.Core.Layout;
using LibTreeMapView.Core.Model;
using LibTreeMapView.Core.Tree;

namespace LibTreeMapView.Core.Tests;

public class TreeMapLayoutTests
{
    private static TreeNode Leaf(string name, long size) =>
        new(name, TreeNodeKind.Section, SectionKind.Code, size);

    private static TreeNode Group(string name, params TreeNode[] children) =>
        new(name, TreeNodeKind.SectionGroup, SectionKind.Code, 0, children);

    [Fact]
    public void Layout_FillsTheWholeAreaForFlatTree()
    {
        TreeNode root = Group("root", Leaf("a", 500), Leaf("b", 300), Leaf("c", 150), Leaf("d", 50));
        var bounds = new LayoutRect(0, 0, 400, 300);

        TreeMapLayoutResult result = TreeMapLayout.Layout(root, bounds);

        double covered = result.Tiles.Sum(t => t.Bounds.Area);
        Assert.Equal(bounds.Area, covered, 0.5);
        Assert.Equal(4, result.Tiles.Count);
    }

    [Fact]
    public void Layout_AreaIsProportionalToSize()
    {
        TreeNode root = Group("root", Leaf("big", 750), Leaf("small", 250));
        var bounds = new LayoutRect(0, 0, 200, 200);

        TreeMapLayoutResult result = TreeMapLayout.Layout(root, bounds);

        double big = result.Tiles.Single(t => t.Node.Name == "big").Bounds.Area;
        double small = result.Tiles.Single(t => t.Node.Name == "small").Bounds.Area;
        Assert.Equal(3.0, big / small, 0.01);
    }

    [Fact]
    public void Layout_SiblingTilesDoNotOverlap()
    {
        TreeNode root = Group("root", Enumerable.Range(1, 25).Select(i => Leaf($"n{i}", i * 37)).ToArray());

        TreeMapLayoutResult result = TreeMapLayout.Layout(root, new LayoutRect(0, 0, 640, 480));

        List<LayoutRect> rects = result.Tiles.Select(t => t.Bounds).ToList();
        for (int i = 0; i < rects.Count; i++)
        {
            for (int j = i + 1; j < rects.Count; j++)
            {
                Assert.True(IsDisjoint(rects[i], rects[j]), $"タイル {i} と {j} が重なっています。");
            }
        }
    }

    [Fact]
    public void Layout_TilesStayInsideBounds()
    {
        TreeNode root = Group(
            "root",
            Group("g1", Leaf("a", 400), Leaf("b", 220), Leaf("c", 90)),
            Group("g2", Leaf("d", 300), Leaf("e", 120)),
            Leaf("f", 60));
        var bounds = new LayoutRect(10, 20, 500, 400);

        TreeMapLayoutResult result = TreeMapLayout.Layout(root, bounds);

        Assert.All(result.Tiles, t =>
        {
            Assert.True(t.Bounds.X >= bounds.X - 0.001);
            Assert.True(t.Bounds.Y >= bounds.Y - 0.001);
            Assert.True(t.Bounds.Right <= bounds.Right + 0.001);
            Assert.True(t.Bounds.Bottom <= bounds.Bottom + 0.001);
        });
    }

    [Fact]
    public void Layout_NestsChildrenUnderGroupsWithHeaders()
    {
        TreeNode root = Group("root", Group("g1", Leaf("a", 600), Leaf("b", 400)));

        TreeMapLayoutResult result = TreeMapLayout.Layout(root, new LayoutRect(0, 0, 300, 200));

        TreeMapTile group = result.Tiles.Single(t => t.Node.Name == "g1");
        Assert.False(group.IsLeafTile);
        Assert.NotNull(group.Header);
        Assert.Equal(2, result.Tiles.Count(t => t.Depth == 2));
    }

    [Fact]
    public void Layout_DoesNotSubdivideTinyTiles()
    {
        TreeNode root = Group(
            "root",
            Leaf("huge", 1_000_000),
            Group("tiny", Leaf("x", 5), Leaf("y", 5)));

        TreeMapLayoutResult result = TreeMapLayout.Layout(root, new LayoutRect(0, 0, 400, 300));

        TreeMapTile tiny = result.Tiles.Single(t => t.Node.Name == "tiny");
        Assert.True(tiny.IsLeafTile);
        Assert.DoesNotContain(result.Tiles, t => t.Node.Name is "x" or "y");
    }

    [Fact]
    public void HitTest_ReturnsDeepestTileAtPoint()
    {
        TreeNode root = Group("root", Group("g1", Leaf("a", 600), Leaf("b", 400)));
        TreeMapLayoutResult result = TreeMapLayout.Layout(root, new LayoutRect(0, 0, 300, 200));

        TreeMapTile leaf = result.Tiles.Single(t => t.Node.Name == "a");
        TreeMapTile? hit = result.HitTest(leaf.Bounds.X + 1, leaf.Bounds.Y + 1);

        Assert.NotNull(hit);
        Assert.Equal("a", hit!.Node.Name);
    }

    [Fact]
    public void HitTest_ReturnsNullOutsideBounds()
    {
        TreeNode root = Group("root", Leaf("a", 100));
        TreeMapLayoutResult result = TreeMapLayout.Layout(root, new LayoutRect(0, 0, 100, 100));

        Assert.Null(result.HitTest(500, 500));
    }

    [Fact]
    public void Layout_ReturnsEmptyForNullOrZeroSizedTree()
    {
        Assert.Empty(TreeMapLayout.Layout(null, new LayoutRect(0, 0, 100, 100)).Tiles);
        Assert.Empty(TreeMapLayout.Layout(Group("root", Leaf("a", 0)), new LayoutRect(0, 0, 100, 100)).Tiles);
        Assert.Empty(TreeMapLayout.Layout(Group("root", Leaf("a", 10)), new LayoutRect(0, 0, 0, 0)).Tiles);
    }

    [Fact]
    public void Layout_KeepsAspectRatiosReasonable()
    {
        TreeNode root = Group("root", Enumerable.Range(1, 40).Select(i => Leaf($"n{i}", 100 + (i * 5))).ToArray());

        TreeMapLayoutResult result = TreeMapLayout.Layout(root, new LayoutRect(0, 0, 800, 600));

        // squarified の狙いどおり、極端に細長いタイルが出ないこと。
        double worstRatio = result.Tiles
            .Where(t => t.Bounds.Area > 1)
            .Max(t => Math.Max(t.Bounds.Width / t.Bounds.Height, t.Bounds.Height / t.Bounds.Width));
        Assert.True(worstRatio < 6, $"最悪の縦横比が大きすぎます: {worstRatio:F2}");
    }

    private static bool IsDisjoint(LayoutRect a, LayoutRect b) =>
        a.Right <= b.X + 0.001 || b.Right <= a.X + 0.001 ||
        a.Bottom <= b.Y + 0.001 || b.Bottom <= a.Y + 0.001;
}
