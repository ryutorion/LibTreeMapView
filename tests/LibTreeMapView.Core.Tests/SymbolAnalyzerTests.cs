using LibTreeMapView.Core.Symbols;
using LibTreeMapView.Core.Tree;

namespace LibTreeMapView.Core.Tests;

/// <summary>.lib のシンボル読み取りと名前空間への整理。</summary>
public class SymbolAnalyzerTests
{
    private static string TestDataPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    private static SymbolIndex Analyze(string fileName = "fixture.lib") =>
        SymbolAnalyzer.Analyze(TestDataPath(fileName), new SymbolAnalysisOptions { UsePdb = false });

    private static SymbolInfo Find(SymbolIndex index, string mangledName) =>
        index.Symbols.First(s => s.MangledName == mangledName);

    [Fact]
    public void Analyze_ReadsSymbolsFromTheCoffSymbolTable()
    {
        SymbolIndex index = Analyze();

        Assert.NotEmpty(index.Symbols);
        Assert.Contains(index.Symbols, s => s.MangledName == "?alpha_add@@YAHHH@Z");
        Assert.Contains(index.Symbols, s => s.MangledName == "?gamma_twice@@YAHH@Z");
        Assert.All(index.Symbols, s => Assert.True(s.Size > 0));
    }

    [Fact]
    public void Analyze_DemanglesNames()
    {
        SymbolInfo add = Find(Analyze(), "?alpha_add@@YAHHH@Z");

        Assert.Equal("alpha_add", add.QualifiedName);
        Assert.Contains("int __cdecl alpha_add(int,int)", add.DisplayName);
        Assert.Empty(add.NamespacePath);
        Assert.Equal("(グローバル)", add.NamespaceText);
    }

    [Fact]
    public void Analyze_UsesComdatSectionSizeForComdatSymbols()
    {
        // delta.obj は /Gy でビルドしてあり、関数ごとに COMDAT セクションを持つ。
        SymbolInfo one = Find(Analyze(), "?delta_one@@YAHH@Z");

        Assert.Equal(SymbolSizeSource.Comdat, one.SizeSource);
        Assert.True(one.IsComdat);
        Assert.Equal(0xB, one.Size); // dumpbin のセクションサイズと一致
        Assert.Equal(SymbolKind.Function, one.Kind);
    }

    [Fact]
    public void Analyze_UsesTheDistanceToTheNextSymbolInsideSharedSections()
    {
        // gamma.obj の .text$mn には gamma_twice (offset 0) と gamma_pick (offset 0x20) が入っている。
        SymbolIndex index = Analyze();

        SymbolInfo twice = Find(index, "?gamma_twice@@YAHH@Z");
        Assert.Equal(SymbolSizeSource.SectionRange, twice.SizeSource);
        Assert.Equal(0x20, twice.Size);

        SymbolInfo pick = Find(index, "?gamma_pick@@YANH@Z");
        Assert.Equal(0x20, pick.Offset);
        Assert.True(pick.Size > 0);
    }

    [Fact]
    public void Analyze_FindsDataSymbols()
    {
        SymbolInfo uninitialized = Find(Analyze(), "?g_uninitialized@@3PAHA");

        Assert.Equal(SymbolKind.Data, uninitialized.Kind);
        Assert.Equal(".bss", uninitialized.SectionName);
        Assert.Equal(0x400, uninitialized.Size);
        Assert.Equal("alpha.obj", uninitialized.ObjectName);
    }

    [Fact]
    public void Analyze_KeepsStaticSymbols()
    {
        // kTable は gamma.cpp の static const 配列。
        SymbolInfo table = Find(Analyze(), "?kTable@@3QBNB");

        Assert.True(table.IsStatic);
        Assert.Equal(".rdata", table.SectionName);
    }

    [Fact]
    public void Analyze_ReadsBigObjSymbols()
    {
        SymbolIndex index = Analyze("special.lib");

        Assert.Contains(index.Symbols, s => s.MangledName == "?epsilon_one@@YAHH@Z" && s.ObjectName == "epsilon.obj");
        Assert.Contains(index.Symbols, s => s.QualifiedName == "epsilon_pick");
    }

    [Fact]
    public void Analyze_WithoutPdbReportsNotFound()
    {
        SymbolIndex index = Analyze();

        Assert.Equal(PdbStatus.NotFound, index.PdbStatus);
        Assert.Null(index.PdbPath);
    }

    [Fact]
    public void Analyze_SortsBySizeDescending()
    {
        List<long> sizes = Analyze().Symbols.Select(s => s.Size).ToList();

        Assert.Equal(sizes.OrderByDescending(s => s).ToList(), sizes);
    }

    [Fact]
    public void BuildTree_GroupsSymbolsByNamespace()
    {
        SymbolIndex index = Analyze();
        TreeNode root = SymbolTreeBuilder.Build(index);

        Assert.Equal(index.Symbols.Sum(s => s.Size), root.Size);
        Assert.All(root.Children, c => Assert.True(c.Size > 0));

        // グローバル関数はルート直下の葉になる。
        Assert.Contains(root.Children, c => c.Kind == TreeNodeKind.Symbol && c.Name == "alpha_add");
    }

    [Fact]
    public void BuildTree_NestsNamespacesAndClasses()
    {
        var index = new SymbolIndex
        {
            LibraryPath = @"C:\libs\demo.lib",
            Symbols =
            [
                Symbol("std::vector<int>::push_back", 100),
                Symbol("std::vector<int>::clear", 40),
                Symbol("std::string::append", 60),
                Symbol("main", 10),
            ],
            Warnings = [],
            PdbStatus = PdbStatus.NotFound,
        };

        TreeNode root = SymbolTreeBuilder.Build(index);

        TreeNode std = root.Children.Single(c => c.Name == "std");
        Assert.Equal(200, std.Size);

        TreeNode vector = std.Children.Single(c => c.Name == "vector<int>");
        Assert.Equal(140, vector.Size);
        Assert.Equal(2, vector.Children.Count);
        Assert.Contains(vector.Children, c => c.Name == "push_back" && c.Size == 100);

        Assert.Contains(root.Children, c => c.Name == "main" && c.Kind == TreeNodeKind.Symbol);
    }

    [Fact]
    public void BuildTree_CanFilterByKindAndName()
    {
        var index = new SymbolIndex
        {
            LibraryPath = @"C:\libs\demo.lib",
            Symbols =
            [
                Symbol("app::run", 100),
                Symbol("app::table", 40, SymbolKind.Data),
            ],
            Warnings = [],
            PdbStatus = PdbStatus.NotFound,
        };

        Assert.Equal(
            100,
            SymbolTreeBuilder.Build(index, new SymbolTreeOptions { Kinds = SymbolKindFilter.FunctionsOnly }).Size);
        Assert.Equal(
            40,
            SymbolTreeBuilder.Build(index, new SymbolTreeOptions { Kinds = SymbolKindFilter.DataOnly }).Size);
        Assert.Equal(
            40,
            SymbolTreeBuilder.Build(index, new SymbolTreeOptions { Filter = "table" }).Size);
        Assert.Equal(
            100,
            SymbolTreeBuilder.Build(index, new SymbolTreeOptions { MinimumSize = 50 }).Size);
    }

    private static SymbolInfo Symbol(string qualifiedName, long size, SymbolKind kind = SymbolKind.Function)
    {
        (IReadOnlyList<string> path, string leaf) = SymbolNameParser.Split(qualifiedName);

        return new SymbolInfo
        {
            MangledName = "?" + qualifiedName,
            DisplayName = qualifiedName,
            QualifiedName = qualifiedName,
            NamespacePath = path,
            LeafName = leaf,
            ObjectName = "demo.obj",
            SectionName = kind == SymbolKind.Function ? ".text$mn" : ".rdata",
            SectionKind = kind == SymbolKind.Function ? Model.SectionKind.Code : Model.SectionKind.ReadOnlyData,
            Offset = 0,
            Size = size,
            Kind = kind,
            IsComdat = true,
            IsStatic = false,
            SizeSource = SymbolSizeSource.Comdat,
        };
    }
}
