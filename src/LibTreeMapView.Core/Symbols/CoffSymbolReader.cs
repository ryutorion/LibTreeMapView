using System.Buffers.Binary;
using System.Text;
using LibTreeMapView.Core.Coff;
using LibTreeMapView.Core.Model;

namespace LibTreeMapView.Core.Symbols;

/// <summary>
/// .lib の各オブジェクトが持つ COFF シンボルテーブルからシンボルを取り出す。
/// サイズはシンボル自体には書かれていないため、COMDAT セクションならセクションのサイズ、
/// そうでなければ同じセクション内の次のシンボルとの距離から求める。
/// </summary>
internal static class CoffSymbolReader
{
    private const byte StorageClassExternal = 2;
    private const byte StorageClassStatic = 3;
    private const ushort DataTypeFunction = 0x20;

    /// <summary>1 つのセクション。</summary>
    private readonly record struct SectionEntry(string Name, long Size, uint Characteristics, SectionKind Kind)
    {
        public bool IsComdat => (Characteristics & CoffConstants.ScnLnkComdat) != 0;
    }

    /// <summary>サイズを決める前のシンボル。</summary>
    private sealed record RawSymbol(string Name, int SectionIndex, long Offset, bool IsFunction, bool IsStatic);

    public static List<SymbolInfo> Read(ReadOnlySpan<byte> data, List<string> warnings)
    {
        var symbols = new List<SymbolInfo>();

        warnings.AddRange(ArchiveWalker.Walk(data, (kind, name, body, memberSize, index) =>
        {
            if (kind != ArchiveMemberKind.Object || body.Length < CoffConstants.CoffFileHeaderSize)
            {
                return;
            }

            try
            {
                ReadObject(body, ArchiveWalker.ToShortName(name), symbols);
            }
            catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException)
            {
                warnings.Add($"{name} のシンボルを読めませんでした: {ex.Message}");
            }
        }));

        return symbols;
    }

    private static void ReadObject(ReadOnlySpan<byte> body, string objectName, List<SymbolInfo> output)
    {
        ushort sig1 = BinaryPrimitives.ReadUInt16LittleEndian(body);
        ushort sig2 = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
        bool bigObj = sig1 == 0 && sig2 == 0xFFFF;

        if (bigObj && (body.Length < CoffConstants.BigObjHeaderSize ||
                       BinaryPrimitives.ReadUInt16LittleEndian(body[4..]) < 2))
        {
            return; // インポート記述子や LTCG の匿名オブジェクトにはシンボルテーブルが無い
        }

        long numberOfSections;
        uint pointerToSymbolTable;
        uint numberOfSymbols;
        int sectionTableOffset;
        int symbolRecordSize;

        if (bigObj)
        {
            numberOfSections = BinaryPrimitives.ReadUInt32LittleEndian(body[44..]);
            pointerToSymbolTable = BinaryPrimitives.ReadUInt32LittleEndian(body[48..]);
            numberOfSymbols = BinaryPrimitives.ReadUInt32LittleEndian(body[52..]);
            sectionTableOffset = CoffConstants.BigObjHeaderSize;
            symbolRecordSize = CoffConstants.BigObjSymbolRecordSize;
        }
        else
        {
            numberOfSections = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
            pointerToSymbolTable = BinaryPrimitives.ReadUInt32LittleEndian(body[8..]);
            numberOfSymbols = BinaryPrimitives.ReadUInt32LittleEndian(body[12..]);
            sectionTableOffset = CoffConstants.CoffFileHeaderSize + BinaryPrimitives.ReadUInt16LittleEndian(body[16..]);
            symbolRecordSize = CoffConstants.SymbolRecordSize;
        }

        if (pointerToSymbolTable == 0 || numberOfSymbols == 0)
        {
            return;
        }

        long stringTableOffset = pointerToSymbolTable + ((long)numberOfSymbols * symbolRecordSize);
        List<SectionEntry> sections = ReadSections(body, sectionTableOffset, numberOfSections, stringTableOffset);
        List<RawSymbol> raw = ReadSymbols(body, pointerToSymbolTable, numberOfSymbols, symbolRecordSize, bigObj, sections, stringTableOffset);

        BuildSymbols(raw, sections, objectName, output);
    }

    private static List<SectionEntry> ReadSections(
        ReadOnlySpan<byte> body,
        int sectionTableOffset,
        long numberOfSections,
        long stringTableOffset)
    {
        var sections = new List<SectionEntry>((int)Math.Min(numberOfSections, 4096));

        for (long i = 0; i < numberOfSections; i++)
        {
            long offset = sectionTableOffset + (i * CoffConstants.SectionHeaderSize);
            if (offset + CoffConstants.SectionHeaderSize > body.Length)
            {
                break;
            }

            ReadOnlySpan<byte> header = body.Slice((int)offset, CoffConstants.SectionHeaderSize);
            string name = ReadSectionName(body, header[..8], stringTableOffset);
            long rawSize = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
            long virtualSize = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
            uint characteristics = BinaryPrimitives.ReadUInt32LittleEndian(header[36..]);

            sections.Add(new SectionEntry(
                name,
                rawSize > 0 ? rawSize : virtualSize,
                characteristics,
                SectionClassifier.Classify(name, characteristics)));
        }

        return sections;
    }

    private static List<RawSymbol> ReadSymbols(
        ReadOnlySpan<byte> body,
        uint pointerToSymbolTable,
        uint numberOfSymbols,
        int symbolRecordSize,
        bool bigObj,
        List<SectionEntry> sections,
        long stringTableOffset)
    {
        var raw = new List<RawSymbol>();

        for (uint i = 0; i < numberOfSymbols; i++)
        {
            long offset = pointerToSymbolTable + ((long)i * symbolRecordSize);
            if (offset + symbolRecordSize > body.Length)
            {
                break;
            }

            ReadOnlySpan<byte> record = body.Slice((int)offset, symbolRecordSize);

            int sectionNumber;
            ushort type;
            byte storageClass;
            byte auxCount;

            if (bigObj)
            {
                sectionNumber = BinaryPrimitives.ReadInt32LittleEndian(record[12..]);
                type = BinaryPrimitives.ReadUInt16LittleEndian(record[16..]);
                storageClass = record[18];
                auxCount = record[19];
            }
            else
            {
                sectionNumber = BinaryPrimitives.ReadInt16LittleEndian(record[12..]);
                type = BinaryPrimitives.ReadUInt16LittleEndian(record[14..]);
                storageClass = record[16];
                auxCount = record[17];
            }

            i += auxCount; // 補助レコードは読み飛ばす

            if (sectionNumber <= 0 || sectionNumber > sections.Count ||
                (storageClass != StorageClassExternal && storageClass != StorageClassStatic))
            {
                continue;
            }

            string name = ReadSymbolName(body, record[..8], stringTableOffset);
            if (name.Length == 0 || name == sections[sectionNumber - 1].Name)
            {
                continue; // セクション自身を表すシンボル
            }

            raw.Add(new RawSymbol(
                name,
                sectionNumber - 1,
                BinaryPrimitives.ReadUInt32LittleEndian(record[8..]),
                (type & 0xF0) == DataTypeFunction,
                storageClass == StorageClassStatic));
        }

        return raw;
    }

    /// <summary>セクションごとにオフセット順へ並べ、隣との距離からサイズを決める。</summary>
    private static void BuildSymbols(
        List<RawSymbol> raw,
        List<SectionEntry> sections,
        string objectName,
        List<SymbolInfo> output)
    {
        foreach (IGrouping<int, RawSymbol> group in raw.GroupBy(s => s.SectionIndex))
        {
            SectionEntry section = sections[group.Key];
            List<RawSymbol> ordered = group.OrderBy(s => s.Offset).ThenBy(s => s.Name, StringComparer.Ordinal).ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                RawSymbol symbol = ordered[i];
                long size;
                SymbolSizeSource source;

                if (section.IsComdat && ordered.Count == 1)
                {
                    // COMDAT はシンボル 1 つで 1 セクションなので、そのままの大きさ。
                    size = section.Size;
                    source = SymbolSizeSource.Comdat;
                }
                else if (i + 1 < ordered.Count && ordered[i + 1].Offset > symbol.Offset)
                {
                    size = ordered[i + 1].Offset - symbol.Offset;
                    source = SymbolSizeSource.SectionRange;
                }
                else if (i + 1 < ordered.Count)
                {
                    size = 0; // 同じ位置にある別名。サイズは最初のシンボルに持たせる。
                    source = SymbolSizeSource.SectionRange;
                }
                else
                {
                    size = Math.Max(0, section.Size - symbol.Offset);
                    source = section.IsComdat ? SymbolSizeSource.Comdat : SymbolSizeSource.SectionRange;
                }

                output.Add(Create(symbol, section, objectName, size, source));
            }
        }
    }

    private static SymbolInfo Create(
        RawSymbol symbol,
        SectionEntry section,
        string objectName,
        long size,
        SymbolSizeSource source)
    {
        string qualified = Demangler.DemangleNameOnly(symbol.Name);
        (IReadOnlyList<string> path, string leaf) = SymbolNameParser.Split(qualified);

        return new SymbolInfo
        {
            MangledName = symbol.Name,
            DisplayName = Demangler.Demangle(symbol.Name),
            QualifiedName = qualified,
            NamespacePath = path,
            LeafName = leaf,
            ObjectName = objectName,
            SectionName = section.Name,
            SectionKind = section.Kind,
            Offset = symbol.Offset,
            Size = size,
            Kind = symbol.IsFunction
                ? SymbolKind.Function
                : section.Kind == SectionKind.Code ? SymbolKind.Function : SymbolKind.Data,
            IsComdat = section.IsComdat,
            IsStatic = symbol.IsStatic,
            SizeSource = source,
        };
    }

    private static string ReadSymbolName(ReadOnlySpan<byte> body, ReadOnlySpan<byte> nameField, long stringTableOffset)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(nameField) == 0)
        {
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(nameField[4..]);
            return ReadStringTableEntry(body, stringTableOffset + offset);
        }

        int end = nameField.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end >= 0 ? nameField[..end] : nameField);
    }

    private static string ReadSectionName(ReadOnlySpan<byte> body, ReadOnlySpan<byte> nameField, long stringTableOffset)
    {
        if (nameField[0] == (byte)'/' &&
            long.TryParse(Encoding.ASCII.GetString(nameField[1..]).Trim('\0').TrimEnd(), out long offset))
        {
            string resolved = ReadStringTableEntry(body, stringTableOffset + offset);
            if (resolved.Length > 0)
            {
                return resolved;
            }
        }

        int end = nameField.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end >= 0 ? nameField[..end] : nameField).TrimEnd();
    }

    private static string ReadStringTableEntry(ReadOnlySpan<byte> body, long absoluteOffset)
    {
        if (absoluteOffset < 0 || absoluteOffset >= body.Length)
        {
            return string.Empty;
        }

        ReadOnlySpan<byte> tail = body[(int)absoluteOffset..];
        int end = tail.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end >= 0 ? tail[..end] : tail);
    }
}
