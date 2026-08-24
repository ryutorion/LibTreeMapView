using System.Globalization;

namespace LibTreeMapView.Core.Model;

/// <summary>バイト数の表示用フォーマット。</summary>
public static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>1,234 KB のような可読形式。</summary>
    public static string Format(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        string format = value >= 100 ? "F0" : value >= 10 ? "F1" : "F2";
        return $"{value.ToString(format, CultureInfo.CurrentCulture)} {Units[unit]}";
    }

    /// <summary>正確なバイト数を桁区切りで。</summary>
    public static string FormatExact(long bytes) => $"{bytes.ToString("N0", CultureInfo.CurrentCulture)} バイト";

    public static string FormatPercent(long part, long total) =>
        total <= 0 ? "-" : (part * 100.0 / total).ToString("F1", CultureInfo.CurrentCulture) + " %";
}
