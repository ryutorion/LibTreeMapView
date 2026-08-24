using System.Text;
using LibTreeMapView.Core.Model;

namespace LibTreeMapView.Core.Caching;

/// <summary>
/// キャッシュファイルの読み書き。UI が使う項目だけを、名前を共有プールに寄せた形で保存する。
/// セクション名は同じ文字列が大量に現れるため、プール化するとファイルは元の .lib より桁違いに小さくなる。
/// </summary>
internal static class LibraryCacheFormat
{
    /// <summary>形式を変えたら上げる。古いキャッシュは無視される。</summary>
    public const int Version = 1;

    private static ReadOnlySpan<byte> Magic => "LTMC"u8;

    private const byte FlagUninitialized = 1 << 0;
    private const byte FlagSynthetic = 1 << 1;

    public static void Write(Stream stream, LibraryInfo library, CacheKey key)
    {
        var pool = new StringPool();
        CollectStrings(library, pool);

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(key.FileSize);
        writer.Write(key.LastWriteUtcTicks);
        writer.Write(key.Fingerprint);
        writer.Write(library.FilePath);

        pool.Write(writer);

        writer.Write7BitEncodedInt(library.Warnings.Count);
        foreach (string warning in library.Warnings)
        {
            writer.Write7BitEncodedInt(pool.IndexOf(warning));
        }

        writer.Write7BitEncodedInt(library.Objects.Count);
        foreach (ObjectFileInfo obj in library.Objects)
        {
            writer.Write7BitEncodedInt(pool.IndexOf(obj.Name));
            writer.Write7BitEncodedInt(pool.IndexOf(obj.ShortName));
            writer.Write7BitEncodedInt64(obj.MemberSize);
            writer.Write((byte)obj.Kind);
            writer.Write(obj.Machine);
            writer.Write7BitEncodedInt(obj.SymbolCount);
            writer.Write7BitEncodedInt(pool.IndexOfOptional(obj.ImportDllName));
            writer.Write7BitEncodedInt(pool.IndexOfOptional(obj.Warning));

            writer.Write7BitEncodedInt(obj.Sections.Count);
            foreach (SectionInfo section in obj.Sections)
            {
                writer.Write7BitEncodedInt(pool.IndexOf(section.Name));
                writer.Write7BitEncodedInt(pool.IndexOf(section.GroupName));
                writer.Write7BitEncodedInt64(section.Size);
                writer.Write7BitEncodedInt64(section.RawDataSize);
                writer.Write7BitEncodedInt64(section.VirtualSize);
                writer.Write(section.Characteristics);
                writer.Write7BitEncodedInt(section.RelocationCount);
                writer.Write((byte)section.Kind);
                writer.Write((byte)((section.IsUninitialized ? FlagUninitialized : 0) |
                                    (section.IsSynthetic ? FlagSynthetic : 0)));
            }
        }
    }

    /// <summary>
    /// キャッシュを読む。対象ファイルと一致しない、あるいは壊れている場合は null を返す。
    /// </summary>
    public static LibraryInfo? TryRead(Stream stream, CacheKey expectedKey)
    {
        try
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            Span<byte> magic = stackalloc byte[Magic.Length];
            if (reader.Read(magic) != Magic.Length || !magic.SequenceEqual(Magic))
            {
                return null;
            }

            if (reader.ReadInt32() != Version)
            {
                return null;
            }

            var key = new CacheKey(reader.ReadInt64(), reader.ReadInt64(), reader.ReadUInt64());
            if (key != expectedKey)
            {
                return null;
            }

            string filePath = reader.ReadString();
            string[] pool = StringPool.Read(reader);

            int warningCount = reader.Read7BitEncodedInt();
            var warnings = new List<string>(warningCount);
            for (int i = 0; i < warningCount; i++)
            {
                warnings.Add(pool[reader.Read7BitEncodedInt()]);
            }

            int objectCount = reader.Read7BitEncodedInt();
            var objects = new List<ObjectFileInfo>(objectCount);
            for (int i = 0; i < objectCount; i++)
            {
                objects.Add(ReadObject(reader, pool));
            }

            return new LibraryInfo
            {
                FilePath = filePath,
                FileSize = key.FileSize,
                Objects = objects,
                Warnings = warnings,
            };
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException or
                                       IndexOutOfRangeException or ArgumentException or OverflowException)
        {
            // 壊れたキャッシュは黙って捨てて、元の .lib を解析し直す。
            return null;
        }
    }

    private static ObjectFileInfo ReadObject(BinaryReader reader, string[] pool)
    {
        string name = pool[reader.Read7BitEncodedInt()];
        string shortName = pool[reader.Read7BitEncodedInt()];
        long memberSize = reader.Read7BitEncodedInt64();
        var kind = (ObjectFileKind)reader.ReadByte();
        ushort machine = reader.ReadUInt16();
        int symbolCount = reader.Read7BitEncodedInt();
        string? importDll = StringPool.Optional(pool, reader.Read7BitEncodedInt());
        string? warning = StringPool.Optional(pool, reader.Read7BitEncodedInt());

        int sectionCount = reader.Read7BitEncodedInt();
        var sections = new List<SectionInfo>(sectionCount);
        for (int i = 0; i < sectionCount; i++)
        {
            sections.Add(ReadSection(reader, pool));
        }

        return new ObjectFileInfo
        {
            Name = name,
            ShortName = shortName,
            MemberSize = memberSize,
            Kind = kind,
            Machine = machine,
            SymbolCount = symbolCount,
            Sections = sections,
            ImportDllName = importDll,
            Warning = warning,
        };
    }

    private static SectionInfo ReadSection(BinaryReader reader, string[] pool)
    {
        string name = pool[reader.Read7BitEncodedInt()];
        string groupName = pool[reader.Read7BitEncodedInt()];
        long size = reader.Read7BitEncodedInt64();
        long rawDataSize = reader.Read7BitEncodedInt64();
        long virtualSize = reader.Read7BitEncodedInt64();
        uint characteristics = reader.ReadUInt32();
        int relocationCount = reader.Read7BitEncodedInt();
        var kind = (SectionKind)reader.ReadByte();
        byte flags = reader.ReadByte();

        return new SectionInfo
        {
            Name = name,
            GroupName = groupName,
            Size = size,
            RawDataSize = rawDataSize,
            VirtualSize = virtualSize,
            Characteristics = characteristics,
            RelocationCount = relocationCount,
            IsUninitialized = (flags & FlagUninitialized) != 0,
            Kind = kind,
            IsSynthetic = (flags & FlagSynthetic) != 0,
        };
    }

    private static void CollectStrings(LibraryInfo library, StringPool pool)
    {
        foreach (string warning in library.Warnings)
        {
            pool.Add(warning);
        }

        foreach (ObjectFileInfo obj in library.Objects)
        {
            pool.Add(obj.Name);
            pool.Add(obj.ShortName);
            pool.AddOptional(obj.ImportDllName);
            pool.AddOptional(obj.Warning);

            foreach (SectionInfo section in obj.Sections)
            {
                pool.Add(section.Name);
                pool.Add(section.GroupName);
            }
        }
    }

    /// <summary>同じ文字列を 1 度だけ保存するための対応表。</summary>
    private sealed class StringPool
    {
        private readonly Dictionary<string, int> indexes = new(StringComparer.Ordinal);
        private readonly List<string> values = [];

        public void Add(string value)
        {
            if (indexes.TryAdd(value, values.Count))
            {
                values.Add(value);
            }
        }

        public void AddOptional(string? value)
        {
            if (value is not null)
            {
                Add(value);
            }
        }

        public int IndexOf(string value) => indexes[value];

        /// <summary>null を 0 として書けるよう、インデックスを 1 つずらす。</summary>
        public int IndexOfOptional(string? value) => value is null ? 0 : indexes[value] + 1;

        public void Write(BinaryWriter writer)
        {
            writer.Write7BitEncodedInt(values.Count);
            foreach (string value in values)
            {
                writer.Write(value);
            }
        }

        public static string[] Read(BinaryReader reader)
        {
            int count = reader.Read7BitEncodedInt();
            var values = new string[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = reader.ReadString();
            }

            return values;
        }

        public static string? Optional(string[] pool, int index) => index == 0 ? null : pool[index - 1];
    }
}
