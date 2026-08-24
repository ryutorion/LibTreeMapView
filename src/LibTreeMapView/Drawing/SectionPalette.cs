using LibTreeMapView.Core.Model;

namespace LibTreeMapView.Drawing;

/// <summary>セクション種別ごとの配色と表示名。</summary>
public static class SectionPalette
{
    private static readonly Dictionary<SectionKind, Color> KindColors = new()
    {
        [SectionKind.Code] = Color.FromArgb("#4F8EF7"),
        [SectionKind.Data] = Color.FromArgb("#F2994A"),
        [SectionKind.ReadOnlyData] = Color.FromArgb("#27AE95"),
        [SectionKind.UninitializedData] = Color.FromArgb("#8892A6"),
        [SectionKind.Debug] = Color.FromArgb("#9B6BDF"),
        [SectionKind.ExceptionHandling] = Color.FromArgb("#E4657A"),
        [SectionKind.Directive] = Color.FromArgb("#A8B545"),
        [SectionKind.Import] = Color.FromArgb("#3FA9D6"),
        [SectionKind.Metadata] = Color.FromArgb("#7A7F87"),
        [SectionKind.Other] = Color.FromArgb("#6E7FA0"),
    };

    private static readonly Dictionary<SectionKind, string> KindNames = new()
    {
        [SectionKind.Code] = "コード (.text)",
        [SectionKind.Data] = "データ (.data)",
        [SectionKind.ReadOnlyData] = "読み取り専用 (.rdata)",
        [SectionKind.UninitializedData] = "未初期化 (.bss)",
        [SectionKind.Debug] = "デバッグ情報 (.debug$*)",
        [SectionKind.ExceptionHandling] = "例外処理 (.pdata/.xdata)",
        [SectionKind.Directive] = "ディレクティブ (.drectve など)",
        [SectionKind.Import] = "インポート (.idata)",
        [SectionKind.Metadata] = "メタデータ (シンボル等)",
        [SectionKind.Other] = "その他",
    };

    public static Color GetColor(SectionKind kind) => KindColors.GetValueOrDefault(kind, KindColors[SectionKind.Other]);

    public static string GetName(SectionKind kind) => KindNames.GetValueOrDefault(kind, "その他");

    /// <summary>深さに応じて明度をずらし、入れ子の境界を見やすくする。</summary>
    public static Color GetTileColor(SectionKind kind, int depth, bool darkTheme)
    {
        Color baseColor = GetColor(kind);
        float shift = Math.Clamp((depth - 1) * 0.10f, 0f, 0.30f);

        return darkTheme
            ? baseColor.WithLuminosity(Math.Clamp(baseColor.GetLuminosity() - 0.10f + shift, 0.16f, 0.78f))
            : baseColor.WithLuminosity(Math.Clamp(baseColor.GetLuminosity() + shift, 0.30f, 0.86f));
    }

    /// <summary>背景色に対して読みやすい文字色。</summary>
    public static Color GetTextColor(Color background) =>
        background.GetLuminosity() > 0.58f ? Color.FromArgb("#14181F") : Color.FromArgb("#FFFFFF");
}
