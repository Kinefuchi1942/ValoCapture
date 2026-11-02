using System.Globalization;
using System.Windows.Data;
using ValoCapture.Core.Sessions;

namespace ValoCapture.App.Controls;

/// <summary>ラウンドの詳細のイベント種別 → 色（キル・デスはタイムラインと同じ、スパイクは黄色）。</summary>
public sealed class RoundEventTypeToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (RoundEventType)value switch
    {
        RoundEventType.Kill => Palette.Kill,
        RoundEventType.Death => Palette.Death,
        _ => Palette.Spike,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
