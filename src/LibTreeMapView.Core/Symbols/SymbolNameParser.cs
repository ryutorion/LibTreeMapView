namespace LibTreeMapView.Core.Symbols;

/// <summary>デマングルした名前を名前空間・クラスの階層に分解する。</summary>
public static class SymbolNameParser
{
    /// <summary>
    /// <c>std::vector&lt;int&gt;::push_back</c> のような名前を
    /// (["std", "vector&lt;int&gt;"], "push_back") に分ける。
    /// テンプレート引数や演算子の中の '&lt;' '&gt;' は区切りとして扱わない。
    /// </summary>
    public static (IReadOnlyList<string> NamespacePath, string LeafName) Split(string qualifiedName)
    {
        if (string.IsNullOrEmpty(qualifiedName))
        {
            return ([], string.Empty);
        }

        List<string> parts = SplitQualified(qualifiedName);

        return parts.Count <= 1
            ? ([], parts.Count == 1 ? parts[0] : qualifiedName)
            : (parts[..^1], parts[^1]);
    }

    /// <summary>'::' で区切る。ただし括弧やテンプレートの内側は無視する。</summary>
    private static List<string> SplitQualified(string name)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;

        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];

            switch (c)
            {
                case '(':
                case '[':
                    depth++;
                    break;

                case ')':
                case ']':
                    depth--;
                    break;

                case '<':
                    if (!IsOperatorToken(name, i))
                    {
                        depth++;
                    }

                    break;

                case '>':
                    // "->" と演算子の '>' は閉じ括弧ではない。
                    if (i > 0 && name[i - 1] == '-')
                    {
                        break;
                    }

                    if (!IsOperatorToken(name, i))
                    {
                        depth--;
                    }

                    break;

                case ':' when depth <= 0 && i + 1 < name.Length && name[i + 1] == ':':
                    parts.Add(name[start..i]);
                    i++;
                    start = i + 1;
                    break;
            }
        }

        if (start <= name.Length)
        {
            parts.Add(name[start..]);
        }

        parts.RemoveAll(string.IsNullOrWhiteSpace);
        return parts;
    }

    /// <summary>
    /// その位置の '&lt;' '&gt;' が <c>operator&lt;&lt;</c> のような演算子名の一部かどうか。
    /// 直前をさかのぼり、記号の並びの手前に "operator" があるかで判断する。
    /// </summary>
    private static bool IsOperatorToken(string name, int index)
    {
        int i = index;

        // '<' '>' '=' の並びをさかのぼる (operator<=> など)。
        while (i > 0 && name[i - 1] is '<' or '>' or '=')
        {
            i--;
        }

        while (i > 0 && name[i - 1] == ' ')
        {
            i--;
        }

        const string Keyword = "operator";
        return i >= Keyword.Length && name.AsSpan(i - Keyword.Length, Keyword.Length).SequenceEqual(Keyword);
    }
}
