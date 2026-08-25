using System.Runtime.InteropServices;

namespace LibTreeMapView.Core.Symbols;

/// <summary>
/// MSVC のマングル名を読める形に戻す。dbghelp の UnDecorateSymbolName を使う。
/// dbghelp はスレッドセーフではないので呼び出しは直列化する。
/// </summary>
public static class Demangler
{
    /// <summary>戻り値・引数まで含む完全な表記。</summary>
    private const uint UndnameComplete = 0x0000;

    /// <summary>名前空間付きの名前だけ。</summary>
    private const uint UndnameNameOnly = 0x1000;

    private const int BufferLength = 4096;

    private static readonly Lock Gate = new();

    private static bool available = OperatingSystem.IsWindows();

    /// <summary>デマングルできる環境かどうか (Windows で dbghelp が使える)。</summary>
    public static bool IsAvailable => available;

    /// <summary>戻り値や引数まで含めた表記に戻す。失敗したら元の名前を返す。</summary>
    public static string Demangle(string mangledName) => Undecorate(mangledName, UndnameComplete);

    /// <summary>名前空間付きの名前だけに戻す。失敗したら元の名前を返す。</summary>
    public static string DemangleNameOnly(string mangledName) => Undecorate(mangledName, UndnameNameOnly);

    private static string Undecorate(string mangledName, uint flags)
    {
        if (string.IsNullOrEmpty(mangledName) || !available)
        {
            return mangledName;
        }

        // MSVC のマングル名は '?' で始まる。C のシンボルはそのままで読める。
        if (mangledName[0] != '?')
        {
            return mangledName;
        }

        try
        {
            char[] buffer = new char[BufferLength];

            lock (Gate)
            {
                int length = UnDecorateSymbolNameW(mangledName, buffer, BufferLength, flags);
                return length > 0 ? new string(buffer, 0, length) : mangledName;
            }
        }
        catch (DllNotFoundException)
        {
            available = false; // dbghelp が無い環境では以後試さない
            return mangledName;
        }
        catch (EntryPointNotFoundException)
        {
            available = false;
            return mangledName;
        }
    }

    // LibraryImport は AllowUnsafeBlocks を要求するので、ここでは従来の DllImport を使う。
    [DllImport("dbghelp.dll", EntryPoint = "UnDecorateSymbolNameW", CharSet = CharSet.Unicode)]
    private static extern int UnDecorateSymbolNameW(string name, [Out] char[] outputString, int maxStringLength, uint flags);
}
