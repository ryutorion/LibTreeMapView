namespace LibTreeMapView.Core.Symbols;

/// <summary>シンボル解析の条件。</summary>
public sealed record SymbolAnalysisOptions
{
    /// <summary>同じディレクトリの PDB を探して、関数のサイズを取り込む。</summary>
    public bool UsePdb { get; init; } = true;

    /// <summary>PDB を明示的に指定する (省略時は .lib と同じディレクトリから探す)。</summary>
    public string? PdbPath { get; init; }

    /// <summary>サイズ 0 のシンボル (同じ位置にある別名など) も残す。</summary>
    public bool IncludeZeroSized { get; init; }
}

/// <summary>
/// .lib のシンボルを読み、デマングルして名前空間ごとに整理できる形にする。
/// サイズは .lib 側 (COMDAT セクションと隣接シンボルの距離) から求め、
/// 同じディレクトリにシンボル入りの PDB があれば関数のサイズをそちらで上書きする。
/// </summary>
public static class SymbolAnalyzer
{
    public static SymbolIndex Analyze(string libraryPath, SymbolAnalysisOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(libraryPath);
        options ??= new SymbolAnalysisOptions();

        var warnings = new List<string>();
        byte[] data = File.ReadAllBytes(libraryPath);

        List<SymbolInfo> symbols = CoffSymbolReader.Read(data, warnings);

        if (!options.IncludeZeroSized)
        {
            symbols.RemoveAll(s => s.Size <= 0);
        }

        if (!Demangler.IsAvailable)
        {
            warnings.Add("デマングルできない環境のため、マングルされた名前のまま表示します。");
        }

        string? pdbPath = options.PdbPath ?? (options.UsePdb ? PdbLocator.Find(libraryPath) : null);
        PdbStatus status = PdbStatus.NotFound;
        string? message = null;
        int pdbMatched = 0;
        int pdbSized = 0;

        if (pdbPath is not null)
        {
            try
            {
                PdbSymbols pdb = PdbSymbolReader.Read(pdbPath);

                if (!pdb.HasDebugInfo)
                {
                    status = PdbStatus.NoSymbols;
                    message = "この PDB は型情報だけを持っていて、シンボルを含みません " +
                              "(cl /Zi が作る vcXXX.pdb はこの形式です)。サイズは .lib から求めています。";
                }
                else
                {
                    (pdbMatched, pdbSized) = ApplyPdbSizes(symbols, pdb);
                    status = PdbStatus.Used;
                    message = $"{pdb.ModuleCount:N0} 個のモジュールから {pdb.Functions.Count:N0} 件の関数を読み、" +
                              $"{pdbMatched:N0} 件がこのライブラリのシンボルと一致 " +
                              $"({pdbSized:N0} 件はサイズを PDB の値に置き換え)。";
                }
            }
            catch (PdbFormatException ex)
            {
                status = PdbStatus.Failed;
                message = ex.Message;
                warnings.Add($"PDB を読めませんでした: {ex.Message}");
            }
        }

        symbols.Sort(static (a, b) => b.Size.CompareTo(a.Size));

        return new SymbolIndex
        {
            LibraryPath = libraryPath,
            Symbols = symbols,
            Warnings = warnings,
            PdbStatus = status,
            PdbPath = pdbPath,
            PdbMessage = message,
            PdbMatchedCount = pdbMatched,
            PdbSizedCount = pdbSized,
        };
    }

    /// <summary>
    /// PDB にある関数のコードサイズで上書きする。突き合わせは名前空間付きの名前で行う。
    /// 戻り値は (名前が一致した数, サイズを置き換えた数)。
    /// </summary>
    private static (int Matched, int Updated) ApplyPdbSizes(List<SymbolInfo> symbols, PdbSymbols pdb)
    {
        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (PdbFunctionSymbol function in pdb.Functions)
        {
            if (function.Size <= 0)
            {
                continue;
            }

            // 同じ名前が複数のモジュールにある場合は大きい方を採用する。
            sizes[function.Name] = sizes.TryGetValue(function.Name, out long existing)
                ? Math.Max(existing, function.Size)
                : function.Size;
        }

        if (sizes.Count == 0)
        {
            return (0, 0);
        }

        int matched = 0;
        int updated = 0;

        for (int i = 0; i < symbols.Count; i++)
        {
            SymbolInfo symbol = symbols[i];

            if (symbol.Kind != SymbolKind.Function || !sizes.TryGetValue(symbol.QualifiedName, out long size))
            {
                continue;
            }

            matched++;

            if (size != symbol.Size)
            {
                symbols[i] = symbol with { Size = size, SizeSource = SymbolSizeSource.Pdb };
                updated++;
            }
        }

        return (matched, updated);
    }
}

/// <summary>.lib と同じディレクトリから PDB を探す。</summary>
public static class PdbLocator
{
    /// <summary>見つからなければ null。</summary>
    public static string? Find(string libraryPath)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(libraryPath));
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        // 1. ライブラリと同じ名前の PDB
        string sameName = Path.Combine(directory, Path.GetFileNameWithoutExtension(libraryPath) + ".pdb");
        if (File.Exists(sameName))
        {
            return sameName;
        }

        string[] candidates;
        try
        {
            candidates = Directory.GetFiles(directory, "*.pdb");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (candidates.Length == 0)
        {
            return null;
        }

        // 2. コンパイラが作る vcXXX.pdb (型情報だけ) より、リンカーが作る PDB を優先する。
        return candidates
            .OrderBy(p => Path.GetFileName(p).StartsWith("vc", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(p => new FileInfo(p).Length)
            .First();
    }
}
