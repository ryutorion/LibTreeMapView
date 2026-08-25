using System.Text;

namespace LibTreeMapView.Core.Coff;

/// <summary>アーカイブメンバーの種類。</summary>
internal enum ArchiveMemberKind
{
    /// <summary>1 番目・2 番目のリンカーメンバー (シンボルテーブル)。</summary>
    LinkerMember,

    /// <summary>ロングネームテーブル。</summary>
    LongNames,

    /// <summary>通常のメンバー (オブジェクトファイル)。</summary>
    Object,
}

/// <summary>
/// COFF アーカイブのメンバーを順に取り出す。セクション解析とシンボル解析の両方から使う。
/// span を扱うためコールバック形式にしている。
/// </summary>
internal static class ArchiveWalker
{
    public delegate void MemberHandler(ArchiveMemberKind kind, string name, ReadOnlySpan<byte> body, long memberSize, int index);

    /// <summary>署名を確認する。合わなければ <see cref="LibFormatException"/>。</summary>
    public static void EnsureArchive(ReadOnlySpan<byte> data)
    {
        if (data.Length < CoffConstants.ArchiveMagicLength ||
            !data[..CoffConstants.ArchiveMagicLength].SequenceEqual(Encoding.ASCII.GetBytes(CoffConstants.ArchiveMagic)))
        {
            throw new LibFormatException(
                "COFF アーカイブの署名が見つかりません。MSVC の静的ライブラリ (.lib) を指定してください。");
        }
    }

    /// <summary>メンバーを順に <paramref name="handler"/> へ渡す。戻り値は警告。</summary>
    public static List<string> Walk(ReadOnlySpan<byte> data, MemberHandler handler)
    {
        EnsureArchive(data);

        var warnings = new List<string>();
        byte[] longNames = [];

        int offset = CoffConstants.ArchiveMagicLength;
        int index = 0;

        while (offset + CoffConstants.MemberHeaderSize <= data.Length)
        {
            ReadOnlySpan<byte> header = data.Slice(offset, CoffConstants.MemberHeaderSize);

            if (header[58] != (byte)'`' || header[59] != (byte)'\n')
            {
                warnings.Add($"オフセット 0x{offset:X} のメンバーヘッダーが壊れているため、以降の解析を打ち切りました。");
                break;
            }

            string rawName = Encoding.ASCII.GetString(header[..16]).TrimEnd();
            string sizeText = Encoding.ASCII.GetString(header.Slice(48, 10)).Trim();

            if (!long.TryParse(sizeText, out long memberSize) || memberSize < 0 ||
                offset + CoffConstants.MemberHeaderSize + memberSize > data.Length)
            {
                warnings.Add($"オフセット 0x{offset:X} のメンバーサイズ ({sizeText}) が不正なため、以降の解析を打ち切りました。");
                break;
            }

            ReadOnlySpan<byte> body = data.Slice(offset + CoffConstants.MemberHeaderSize, (int)memberSize);
            index++;

            if (rawName is "/" or "")
            {
                handler(
                    ArchiveMemberKind.LinkerMember,
                    index == 1 ? "(リンカーメンバー #1: シンボルテーブル)" : "(リンカーメンバー #2: シンボルテーブル)",
                    body,
                    memberSize,
                    index);
            }
            else if (rawName == "//")
            {
                longNames = body.ToArray();
                handler(ArchiveMemberKind.LongNames, "(ロングネームテーブル)", body, memberSize, index);
            }
            else
            {
                handler(ArchiveMemberKind.Object, ResolveMemberName(rawName, longNames), body, memberSize, index);
            }

            offset += CoffConstants.MemberHeaderSize + (int)memberSize;
            if ((offset & 1) != 0)
            {
                offset++; // メンバーは 2 バイト境界に整列する
            }
        }

        return warnings;
    }

    /// <summary>"/26" のような名前をロングネームテーブルから引く。</summary>
    private static string ResolveMemberName(string rawName, ReadOnlySpan<byte> longNames)
    {
        if (rawName.Length > 1 && rawName[0] == '/' && int.TryParse(rawName[1..], out int nameOffset))
        {
            if (nameOffset >= 0 && nameOffset < longNames.Length)
            {
                ReadOnlySpan<byte> tail = longNames[nameOffset..];
                int end = tail.IndexOfAny((byte)0, (byte)'\n');
                if (end < 0)
                {
                    end = tail.Length;
                }

                string longName = Encoding.UTF8.GetString(tail[..end]).TrimEnd();
                return longName.Length > 0 ? longName.TrimEnd('/') : rawName;
            }

            return rawName;
        }

        return rawName.TrimEnd('/');
    }

    /// <summary>ディレクトリを除いたファイル名。</summary>
    public static string ToShortName(string name)
    {
        int index = name.LastIndexOfAny(['\\', '/']);
        return index >= 0 && index < name.Length - 1 ? name[(index + 1)..] : name;
    }
}
