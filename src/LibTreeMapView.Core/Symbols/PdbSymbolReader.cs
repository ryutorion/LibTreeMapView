using System.Buffers.Binary;
using System.Text;

namespace LibTreeMapView.Core.Symbols;

/// <summary>PDB から取り出した関数シンボル。</summary>
public sealed record PdbFunctionSymbol(string Name, long Size, ushort Segment, uint Offset);

/// <summary>PDB の読み取り結果。</summary>
public sealed class PdbSymbols
{
    public required bool HasDebugInfo { get; init; }

    public required IReadOnlyList<PdbFunctionSymbol> Functions { get; init; }

    /// <summary>デバッグ情報を持つモジュール (コンパイル単位) の数。</summary>
    public required int ModuleCount { get; init; }
}

/// <summary>PDB として読めなかったときに投げられる。</summary>
public sealed class PdbFormatException(string message) : Exception(message);

/// <summary>
/// PDB (MSF コンテナ) から関数シンボルとそのコードサイズを取り出す。
/// 必要なのはサイズの分かる S_GPROC32 / S_LPROC32 だけなので、型情報や行番号は読まない。
///
/// cl /Zi が作る vcXXX.pdb は型情報だけを持つ「型サーバー」で、シンボルを含まない。
/// その場合 <see cref="PdbSymbols.HasDebugInfo"/> が false になる。
/// </summary>
internal static class PdbSymbolReader
{
    private const long MaxFileSize = 1L << 31;

    /// <summary>MSF 7.00 の署名。</summary>
    private static ReadOnlySpan<byte> Magic => "Microsoft C/C++ MSF 7.00\r\nDS\0\0\0"u8;

    private const int DbiStreamIndex = 3;
    private const ushort NoStream = 0xFFFF;

    // CodeView のレコード種別
    private const ushort S_LPROC32 = 0x110F;
    private const ushort S_GPROC32 = 0x1110;
    private const ushort S_LPROC32_ID = 0x1146;
    private const ushort S_GPROC32_ID = 0x1147;

    public static PdbSymbols Read(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new PdbFormatException($"PDB が見つかりません: {path}");
        }

        if (file.Length >= MaxFileSize)
        {
            throw new PdbFormatException($"PDB が大きすぎます ({file.Length / 1024.0 / 1024.0:F1} MB)。");
        }

        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (IOException ex)
        {
            throw new PdbFormatException($"PDB を読み込めません: {ex.Message}");
        }

        return Parse(data);
    }

    private static PdbSymbols Parse(byte[] data)
    {
        var msf = MsfFile.Open(data);
        byte[] dbi = msf.ReadStream(DbiStreamIndex);

        if (dbi.Length < 64)
        {
            // DBI ストリームが空 = デバッグ情報を持たない (型サーバー PDB)。
            return new PdbSymbols { HasDebugInfo = false, Functions = [], ModuleCount = 0 };
        }

        if (BinaryPrimitives.ReadInt32LittleEndian(dbi) != -1)
        {
            throw new PdbFormatException("DBI ストリームの形式が想定と違います。");
        }

        int moduleInfoSize = BinaryPrimitives.ReadInt32LittleEndian(dbi.AsSpan(24));
        if (moduleInfoSize < 0 || 64 + moduleInfoSize > dbi.Length)
        {
            throw new PdbFormatException("DBI のモジュール情報が壊れています。");
        }

        var functions = new List<PdbFunctionSymbol>();
        int moduleCount = 0;

        foreach ((ushort streamIndex, int symbolBytes) in EnumerateModules(dbi.AsSpan(64, moduleInfoSize)))
        {
            moduleCount++;

            if (streamIndex == NoStream || symbolBytes <= 4)
            {
                continue;
            }

            byte[] moduleStream = msf.ReadStream(streamIndex);
            if (moduleStream.Length < symbolBytes)
            {
                continue;
            }

            // 先頭 4 バイトは署名 (CV_SIGNATURE_C13)。
            ReadSymbols(moduleStream.AsSpan(4, symbolBytes - 4), functions);
        }

        return new PdbSymbols
        {
            HasDebugInfo = true,
            Functions = functions,
            ModuleCount = moduleCount,
        };
    }

    /// <summary>モジュール情報から、シンボルストリームの番号とサイズを取り出す。</summary>
    private static List<(ushort StreamIndex, int SymbolBytes)> EnumerateModules(ReadOnlySpan<byte> moduleInfo)
    {
        var modules = new List<(ushort, int)>();
        int offset = 0;

        while (offset + 64 <= moduleInfo.Length)
        {
            ReadOnlySpan<byte> record = moduleInfo[offset..];

            ushort streamIndex = BinaryPrimitives.ReadUInt16LittleEndian(record[34..]);
            int symbolBytes = BinaryPrimitives.ReadInt32LittleEndian(record[36..]);
            modules.Add((streamIndex, symbolBytes));

            // 64 バイトのあとにモジュール名とオブジェクト名 (どちらも NUL 終端) が続く。
            int cursor = 64;
            cursor = SkipString(record, cursor);
            cursor = SkipString(record, cursor);
            cursor = (cursor + 3) & ~3; // 4 バイト境界

            if (cursor <= 0 || offset + cursor <= offset)
            {
                break;
            }

            offset += cursor;
        }

        return modules;
    }

    private static int SkipString(ReadOnlySpan<byte> data, int offset)
    {
        while (offset < data.Length && data[offset] != 0)
        {
            offset++;
        }

        return offset + 1;
    }

    /// <summary>シンボルレコードを読み、サイズの分かる関数だけを集める。</summary>
    private static void ReadSymbols(ReadOnlySpan<byte> symbols, List<PdbFunctionSymbol> functions)
    {
        int offset = 0;

        while (offset + 4 <= symbols.Length)
        {
            int length = BinaryPrimitives.ReadUInt16LittleEndian(symbols[offset..]);
            if (length < 2 || offset + 2 + length > symbols.Length)
            {
                break;
            }

            ushort kind = BinaryPrimitives.ReadUInt16LittleEndian(symbols[(offset + 2)..]);
            ReadOnlySpan<byte> record = symbols.Slice(offset + 4, length - 2);

            if (kind is S_GPROC32 or S_LPROC32 or S_GPROC32_ID or S_LPROC32_ID && record.Length >= 35)
            {
                // Parent, End, Next, CodeSize, DbgStart, DbgEnd, TypeIndex, Offset, Segment, Flags, Name
                long codeSize = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
                uint codeOffset = BinaryPrimitives.ReadUInt32LittleEndian(record[28..]);
                ushort segment = BinaryPrimitives.ReadUInt16LittleEndian(record[32..]);
                string name = ReadNullTerminated(record[35..]);

                if (name.Length > 0)
                {
                    functions.Add(new PdbFunctionSymbol(name, codeSize, segment, codeOffset));
                }
            }

            offset += 2 + length;
            offset = (offset + 3) & ~3; // レコードは 4 バイト境界
        }
    }

    private static string ReadNullTerminated(ReadOnlySpan<byte> data)
    {
        int end = data.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end >= 0 ? data[..end] : data);
    }

    /// <summary>MSF コンテナ。ストリームをブロックの並びから組み立てる。</summary>
    private sealed class MsfFile
    {
        private readonly byte[] data;
        private readonly int blockSize;
        private readonly List<(int Size, int[] Blocks)> streams;

        private MsfFile(byte[] data, int blockSize, List<(int, int[])> streams)
        {
            this.data = data;
            this.blockSize = blockSize;
            this.streams = streams;
        }

        public static MsfFile Open(byte[] data)
        {
            if (data.Length < 56 || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            {
                throw new PdbFormatException("PDB (MSF 7.00) の署名が見つかりません。");
            }

            int blockSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(32));
            int numBlocks = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(40));
            int directoryBytes = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(44));
            int blockMapAddr = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(52));

            if (blockSize <= 0 || (blockSize & (blockSize - 1)) != 0 || directoryBytes < 0)
            {
                throw new PdbFormatException("PDB のブロックサイズが不正です。");
            }

            if ((long)numBlocks * blockSize > data.Length)
            {
                throw new PdbFormatException("PDB が途中で切れています。");
            }

            // ディレクトリ本体の位置は、blockMapAddr のブロックに並んだブロック番号で示される。
            int directoryBlockCount = CeilDiv(directoryBytes, blockSize);
            int[] directoryBlocks = ReadBlockNumbers(data, blockMapAddr, blockSize, directoryBlockCount);
            byte[] directory = Gather(data, blockSize, directoryBlocks, directoryBytes);

            return new MsfFile(data, blockSize, ReadStreamDirectory(directory, blockSize));
        }

        public byte[] ReadStream(int index)
        {
            if (index < 0 || index >= streams.Count)
            {
                return [];
            }

            (int size, int[] blocks) = streams[index];
            return size <= 0 ? [] : Gather(data, blockSize, blocks, size);
        }

        private static List<(int Size, int[] Blocks)> ReadStreamDirectory(byte[] directory, int blockSize)
        {
            if (directory.Length < 4)
            {
                throw new PdbFormatException("PDB のストリームディレクトリが空です。");
            }

            int streamCount = BinaryPrimitives.ReadInt32LittleEndian(directory);
            if (streamCount < 0 || 4 + ((long)streamCount * 4) > directory.Length)
            {
                throw new PdbFormatException("PDB のストリーム数が不正です。");
            }

            var sizes = new int[streamCount];
            for (int i = 0; i < streamCount; i++)
            {
                int size = BinaryPrimitives.ReadInt32LittleEndian(directory.AsSpan(4 + (i * 4)));
                sizes[i] = size == -1 ? 0 : size; // -1 は未使用のストリーム
            }

            var streams = new List<(int, int[])>(streamCount);
            int offset = 4 + (streamCount * 4);

            for (int i = 0; i < streamCount; i++)
            {
                int count = CeilDiv(sizes[i], blockSize);
                var blocks = new int[count];

                for (int b = 0; b < count; b++)
                {
                    if (offset + 4 > directory.Length)
                    {
                        throw new PdbFormatException("PDB のストリームディレクトリが途中で切れています。");
                    }

                    blocks[b] = BinaryPrimitives.ReadInt32LittleEndian(directory.AsSpan(offset));
                    offset += 4;
                }

                streams.Add((sizes[i], blocks));
            }

            return streams;
        }

        private static int[] ReadBlockNumbers(byte[] data, int blockIndex, int blockSize, int count)
        {
            long start = (long)blockIndex * blockSize;
            if (start < 0 || start + ((long)count * 4) > data.Length)
            {
                throw new PdbFormatException("PDB のブロックマップが範囲外です。");
            }

            var blocks = new int[count];
            for (int i = 0; i < count; i++)
            {
                blocks[i] = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan((int)start + (i * 4)));
            }

            return blocks;
        }

        private static byte[] Gather(byte[] data, int blockSize, int[] blocks, int size)
        {
            var result = new byte[size];
            int written = 0;

            foreach (int block in blocks)
            {
                long start = (long)block * blockSize;
                if (start < 0 || start >= data.Length)
                {
                    throw new PdbFormatException("PDB のブロック番号が範囲外です。");
                }

                int length = Math.Min(blockSize, size - written);
                length = Math.Min(length, data.Length - (int)start);
                if (length <= 0)
                {
                    break;
                }

                data.AsSpan((int)start, length).CopyTo(result.AsSpan(written));
                written += length;
            }

            return result;
        }

        private static int CeilDiv(int value, int divisor) => (value + divisor - 1) / divisor;
    }
}
