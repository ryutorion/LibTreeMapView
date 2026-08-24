using System.Globalization;

namespace LibTreeMapView.Converters;

/// <summary>bool を反転する。未読み込み時だけ案内を出す用途。</summary>
public sealed class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not bool flag || !flag;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not bool flag || !flag;
}
