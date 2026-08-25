using LibTreeMapView.Core.Symbols;

namespace LibTreeMapView.Core.Tests;

/// <summary>
/// TestData/pdb/ の tiny.lib と tiny.pdb は tiny.cpp を cl /c /Zi /Gy でコンパイルし、
/// link /DLL /DEBUG /NOENTRY /NODEFAULTLIB でリンクして作ったもの。
/// リンカーが作る PDB なのでシンボル (S_GPROC32) を含む。
/// </summary>
public class PdbSymbolTests
{
    private static string PdbDataPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "pdb", fileName);

    [Fact]
    public void Locator_PrefersThePdbWithTheSameName()
    {
        string? found = PdbLocator.Find(PdbDataPath("tiny.lib"));

        Assert.NotNull(found);
        Assert.Equal("tiny.pdb", Path.GetFileName(found));
    }

    [Fact]
    public void Locator_ReturnsNullWhenThereIsNoPdb()
    {
        Assert.Null(PdbLocator.Find(Path.Combine(AppContext.BaseDirectory, "TestData", "fixture.lib")));
    }

    [Fact]
    public void Analyze_UsesTheLinkedPdb()
    {
        SymbolIndex index = SymbolAnalyzer.Analyze(PdbDataPath("tiny.lib"));

        Assert.Equal(PdbStatus.Used, index.PdbStatus);
        Assert.Equal("tiny.pdb", Path.GetFileName(index.PdbPath));
        Assert.NotNull(index.PdbMessage);

        // demo::math の 3 つの関数はどれも PDB 側にある。
        Assert.True(index.PdbMatchedCount >= 3, $"一致したのは {index.PdbMatchedCount} 件でした。");
    }

    [Fact]
    public void Analyze_GroupsSymbolsUnderTheirNamespace()
    {
        SymbolIndex index = SymbolAnalyzer.Analyze(PdbDataPath("tiny.lib"));

        SymbolInfo add = index.Symbols.First(s => s.QualifiedName == "demo::math::add");

        Assert.Equal(["demo", "math"], add.NamespacePath);
        Assert.Equal("add", add.LeafName);
        Assert.Equal("demo::math", add.NamespaceText);
        Assert.Contains("demo::math::add", add.DisplayName);
        Assert.True(add.Size > 0);
    }

    [Fact]
    public void Analyze_CanIgnoreThePdb()
    {
        SymbolIndex index = SymbolAnalyzer.Analyze(
            PdbDataPath("tiny.lib"),
            new SymbolAnalysisOptions { UsePdb = false });

        Assert.Equal(PdbStatus.NotFound, index.PdbStatus);
        Assert.Null(index.PdbPath);
        Assert.DoesNotContain(index.Symbols, s => s.SizeSource == SymbolSizeSource.Pdb);
    }

    [Fact]
    public void Analyze_ReportsWhenThePdbHasNoSymbols()
    {
        // fixture.lib は PDB を持たないので、型情報だけの PDB を明示的に渡して確かめる。
        // (cl /Zi が作る vcXXX.pdb と同じ状況)
        string typeOnlyPdb = PdbDataPath("typeonly.pdb");
        if (!File.Exists(typeOnlyPdb))
        {
            return; // フィクスチャが無い環境ではスキップ
        }

        SymbolIndex index = SymbolAnalyzer.Analyze(
            Path.Combine(AppContext.BaseDirectory, "TestData", "fixture.lib"),
            new SymbolAnalysisOptions { PdbPath = typeOnlyPdb });

        Assert.Equal(PdbStatus.NoSymbols, index.PdbStatus);
        Assert.Contains("型情報", index.PdbMessage);
    }

    [Fact]
    public void Analyze_ReportsWhenThePdbCannotBeRead()
    {
        SymbolIndex index = SymbolAnalyzer.Analyze(
            Path.Combine(AppContext.BaseDirectory, "TestData", "fixture.lib"),
            new SymbolAnalysisOptions { PdbPath = Path.Combine(AppContext.BaseDirectory, "TestData", "fixture.lib") });

        Assert.Equal(PdbStatus.Failed, index.PdbStatus);
        Assert.NotEmpty(index.Warnings);
        Assert.NotEmpty(index.Symbols); // PDB が読めなくてもシンボルは .lib から出る
    }
}
