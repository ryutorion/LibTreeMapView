using System.Buffers.Binary;
using System.Text;
using LibTreeMapView.Core.Model;

namespace LibTreeMapView.Core.Coff;

/// <summary>
/// MSVC 形式の静的ライブラリ (COFF アーカイブ) を読み、メンバーごとのセクション情報を取り出す。
/// </summary>
public static class LibReader
{
    /// <summary>これを超えるファイルは読み込みを拒否する (メモリ保護)。</summary>
    private const long MaxFileSize = 1L << 31;

    public static LibraryInfo Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException($"ファイルが見つかりません: {path}", path);
        }

        if (file.Length >= MaxFileSize)
        {
            throw new LibFormatException(
                $"ファイルが大きすぎます ({file.Length / 1024.0 / 1024.0:F1} MB)。2 GB 未満のライブラリのみ扱えます。");
        }

        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (IOException ex)
        {
            throw new LibFormatException($"ファイルを読み込めません: {ex.Message}", ex);
        }

        return Read(data, path);
    }

    public static LibraryInfo Read(ReadOnlySpan<byte> data, string path)
    {
        if (data.Length < CoffConstants.ArchiveMagicLength ||
            !data[..CoffConstants.ArchiveMagicLength].SequenceEqual(Encoding.ASCII.GetBytes(CoffConstants.ArchiveMagic)))
        {
            throw new LibFormatException(
                "COFF アーカイブの署名が見つかりません。MSVC の静的ライブラリ (.lib) を指定してください。");
        }

        var objects = new List<ObjectFileInfo>();
        var warnings = new List<string>();
        byte[] longNames = [];

        int offset = CoffConstants.ArchiveMagicLength;
        int memberIndex = 0;

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
            memberIndex++;

            if (rawName is "/" or "")
            {
                // 1 番目・2 番目のリンカーメンバー (シンボルテーブル)。中身は解析せずサイズだけ数える。
                objects.Add(CreateSpecialMember(
                    memberIndex == 1 ? "(リンカーメンバー #1: シンボルテーブル)" : "(リンカーメンバー #2: シンボルテーブル)",
                    memberSize));
            }
            else if (rawName == "//")
            {
                longNames = body.ToArray();
                objects.Add(CreateSpecialMember("(ロングネームテーブル)", memberSize));
            }
            else
            {
                string name = ResolveMemberName(rawName, longNames);
                try
                {
                    objects.Add(ParseMember(body, name, memberSize));
                }
                catch (Exception ex) when (ex is not LibFormatException)
                {
                    warnings.Add($"{name} の解析に失敗しました: {ex.Message}");
                    objects.Add(CreateUnknownMember(name, memberSize, ex.Message));
                }
            }

            offset += CoffConstants.MemberHeaderSize + (int)memberSize;
            if ((offset & 1) != 0)
            {
                offset++; // メンバーは 2 バイト境界に整列する
            }
        }

        if (objects.Count == 0)
        {
            warnings.Add("解析できるメンバーが 1 つもありませんでした。");
        }

        return new LibraryInfo
        {
            FilePath = path,
            FileSize = data.Length,
            Objects = objects,
            Warnings = warnings,
        };
    }

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

    private static ObjectFileInfo ParseMember(ReadOnlySpan<byte> body, string name, long memberSize)
    {
        if (body.Length < 4)
        {
            return CreateUnknownMember(name, memberSize, "メンバーが短すぎます。");
        }

        ushort sig1 = BinaryPrimitives.ReadUInt16LittleEndian(body);
        ushort sig2 = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);

        if (sig1 == 0 && sig2 == 0xFFFF && body.Length >= 6)
        {
            ushort version = BinaryPrimitives.ReadUInt16LittleEndian(body[4..]);
            if (version >= 2 && body.Length >= CoffConstants.BigObjHeaderSize)
            {
                return ParseCoff(body, name, memberSize, bigObj: true);
            }

            return version == 0
                ? ParseImportMember(body, name, memberSize)
                : CreateOpaqueMember(name, memberSize, ObjectFileKind.Anonymous, "(LTCG / 匿名オブジェクト)", SectionKind.Other);
        }

        return ParseCoff(body, name, memberSize, bigObj: false);
    }

    private static ObjectFileInfo ParseCoff(ReadOnlySpan<byte> body, string name, long memberSize, bool bigObj)
    {
        ushort machine;
        long numberOfSections;
        uint pointerToSymbolTable;
        uint numberOfSymbols;
        int sectionTableOffset;
        int symbolRecordSize;

        if (bigObj)
        {
            machine = BinaryPrimitives.ReadUInt16LittleEndian(body[6..]);
            numberOfSections = BinaryPrimitives.ReadUInt32LittleEndian(body[44..]);
            pointerToSymbolTable = BinaryPrimitives.ReadUInt32LittleEndian(body[48..]);
            numberOfSymbols = BinaryPrimitives.ReadUInt32LittleEndian(body[52..]);
            sectionTableOffset = CoffConstants.BigObjHeaderSize;
            symbolRecordSize = CoffConstants.BigObjSymbolRecordSize;
        }
        else
        {
            if (body.Length < CoffConstants.CoffFileHeaderSize)
            {
                return CreateUnknownMember(name, memberSize, "COFF ヘッダーが読み取れません。");
            }

            machine = BinaryPrimitives.ReadUInt16LittleEndian(body);
            numberOfSections = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
            pointerToSymbolTable = BinaryPrimitives.ReadUInt32LittleEndian(body[8..]);
            numberOfSymbols = BinaryPrimitives.ReadUInt32LittleEndian(body[12..]);
            ushort sizeOfOptionalHeader = BinaryPrimitives.ReadUInt16LittleEndian(body[16..]);
            sectionTableOffset = CoffConstants.CoffFileHeaderSize + sizeOfOptionalHeader;
            symbolRecordSize = CoffConstants.SymbolRecordSize;
        }

        long stringTableOffset = pointerToSymbolTable == 0
            ? -1
            : pointerToSymbolTable + ((long)numberOfSymbols * symbolRecordSize);

        var sections = new List<SectionInfo>();
        string? warning = null;

        for (long i = 0; i < numberOfSections; i++)
        {
            long sectionOffset = sectionTableOffset + (i * CoffConstants.SectionHeaderSize);
            if (sectionOffset + CoffConstants.SectionHeaderSize > body.Length)
            {
                warning = $"セクションヘッダーが途中で切れています ({sections.Count}/{numberOfSections} 件のみ解析)。";
                break;
            }

            sections.Add(ParseSection(body, (int)sectionOffset, stringTableOffset));
        }

        return new ObjectFileInfo
        {
            Name = name,
            ShortName = ToShortName(name),
            MemberSize = memberSize,
            Kind = bigObj ? ObjectFileKind.BigObj : ObjectFileKind.Coff,
            Machine = machine,
            SymbolCount = (int)Math.Min(numberOfSymbols, int.MaxValue),
            Sections = sections,
            Warning = warning,
        };
    }

    private static SectionInfo ParseSection(ReadOnlySpan<byte> body, int sectionOffset, long stringTableOffset)
    {
        ReadOnlySpan<byte> header = body.Slice(sectionOffset, CoffConstants.SectionHeaderSize);

        string name = ResolveSectionName(body, header[..8], stringTableOffset);
        long virtualSize = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        long rawDataSize = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
        uint pointerToRawData = BinaryPrimitives.ReadUInt32LittleEndian(header[20..]);
        uint pointerToRelocations = BinaryPrimitives.ReadUInt32LittleEndian(header[24..]);
        int relocationCount = BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
        uint characteristics = BinaryPrimitives.ReadUInt32LittleEndian(header[36..]);

        // 再配置数が 65535 を超える場合、実数は最初の再配置レコードの VirtualAddress に入る。
        if ((characteristics & CoffConstants.ScnLnkNRelocOvfl) != 0 &&
            relocationCount == 0xFFFF &&
            pointerToRelocations + 4 <= (uint)body.Length)
        {
            relocationCount = (int)Math.Min(
                BinaryPrimitives.ReadUInt32LittleEndian(body[(int)pointerToRelocations..]),
                int.MaxValue);
        }

        bool uninitialized = (characteristics & CoffConstants.ScnCntUninitializedData) != 0 ||
                             (pointerToRawData == 0 && rawDataSize > 0);

        long size = rawDataSize > 0 ? rawDataSize : virtualSize;

        return new SectionInfo
        {
            Name = name,
            GroupName = SectionClassifier.GetGroupName(name),
            Size = size,
            RawDataSize = uninitialized ? 0 : rawDataSize,
            VirtualSize = virtualSize,
            Characteristics = characteristics,
            RelocationCount = relocationCount,
            IsUninitialized = uninitialized,
            Kind = SectionClassifier.Classify(name, characteristics),
        };
    }

    private static string ResolveSectionName(ReadOnlySpan<byte> body, ReadOnlySpan<byte> nameField, long stringTableOffset)
    {
        if (nameField[0] == (byte)'/')
        {
            string reference = Encoding.ASCII.GetString(nameField[1..]).Trim('\0').TrimEnd();

            long offset = -1;
            if (reference.StartsWith('/'))
            {
                offset = DecodeBase64Offset(reference[1..]);
            }
            else if (long.TryParse(reference, out long parsed))
            {
                offset = parsed;
            }

            if (offset >= 0 && stringTableOffset >= 0)
            {
                long absolute = stringTableOffset + offset;
                if (absolute >= 0 && absolute < body.Length)
                {
                    ReadOnlySpan<byte> tail = body[(int)absolute..];
                    int end = tail.IndexOf((byte)0);
                    if (end < 0)
                    {
                        end = tail.Length;
                    }

                    string resolved = Encoding.UTF8.GetString(tail[..end]);
                    if (resolved.Length > 0)
                    {
                        return resolved;
                    }
                }
            }
        }

        ReadOnlySpan<byte> raw = nameField;
        int nul = raw.IndexOf((byte)0);
        if (nul >= 0)
        {
            raw = raw[..nul];
        }

        return Encoding.UTF8.GetString(raw).TrimEnd();
    }

    /// <summary>GNU 形式の base64 セクション名オフセットを解く。</summary>
    private static long DecodeBase64Offset(string text)
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        long value = 0;
        foreach (char c in text)
        {
            int digit = Alphabet.IndexOf(c);
            if (digit < 0)
            {
                return -1;
            }

            value = (value * 64) + digit;
        }

        return value;
    }

    private static ObjectFileInfo ParseImportMember(ReadOnlySpan<byte> body, string name, long memberSize)
    {
        ushort machine = body.Length >= 8 ? BinaryPrimitives.ReadUInt16LittleEndian(body[6..]) : (ushort)0;
        string? symbolName = null;
        string? dllName = null;

        if (body.Length > CoffConstants.ImportObjectHeaderSize)
        {
            ReadOnlySpan<byte> names = body[CoffConstants.ImportObjectHeaderSize..];
            int end = names.IndexOf((byte)0);
            if (end >= 0)
            {
                symbolName = Encoding.UTF8.GetString(names[..end]);
                ReadOnlySpan<byte> tail = names[(end + 1)..];
                int dllEnd = tail.IndexOf((byte)0);
                dllName = Encoding.UTF8.GetString(dllEnd >= 0 ? tail[..dllEnd] : tail);
            }
        }

        var section = new SectionInfo
        {
            Name = ".idata (インポート記述子)",
            GroupName = ".idata",
            Size = memberSize,
            RawDataSize = memberSize,
            VirtualSize = 0,
            Characteristics = CoffConstants.ScnCntInitializedData | CoffConstants.ScnMemRead,
            RelocationCount = 0,
            IsUninitialized = false,
            Kind = SectionKind.Import,
            IsSynthetic = true,
        };

        return new ObjectFileInfo
        {
            Name = string.IsNullOrEmpty(symbolName) ? name : $"{name} [{symbolName}]",
            ShortName = ToShortName(name),
            MemberSize = memberSize,
            Kind = ObjectFileKind.Import,
            Machine = machine,
            SymbolCount = 1,
            Sections = [section],
            ImportDllName = dllName,
        };
    }

    private static ObjectFileInfo CreateSpecialMember(string name, long memberSize) =>
        CreateOpaqueMember(name, memberSize, ObjectFileKind.Unknown, "(アーカイブメタデータ)", SectionKind.Metadata);

    private static ObjectFileInfo CreateOpaqueMember(
        string name,
        long memberSize,
        ObjectFileKind kind,
        string sectionName,
        SectionKind sectionKind)
    {
        var section = new SectionInfo
        {
            Name = sectionName,
            GroupName = sectionName,
            Size = memberSize,
            RawDataSize = memberSize,
            VirtualSize = 0,
            Characteristics = 0,
            RelocationCount = 0,
            IsUninitialized = false,
            Kind = sectionKind,
            IsSynthetic = true,
        };

        return new ObjectFileInfo
        {
            Name = name,
            ShortName = ToShortName(name),
            MemberSize = memberSize,
            Kind = kind,
            Machine = 0,
            SymbolCount = 0,
            Sections = [section],
        };
    }

    private static ObjectFileInfo CreateUnknownMember(string name, long memberSize, string warning) =>
        new()
        {
            Name = name,
            ShortName = ToShortName(name),
            MemberSize = memberSize,
            Kind = ObjectFileKind.Unknown,
            Machine = 0,
            SymbolCount = 0,
            Sections = [],
            Warning = warning,
        };

    private static string ToShortName(string name)
    {
        int index = name.LastIndexOfAny(['\\', '/']);
        return index >= 0 && index < name.Length - 1 ? name[(index + 1)..] : name;
    }
}
