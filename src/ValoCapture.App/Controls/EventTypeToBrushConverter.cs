using System.Globalization;
using System.Windows.Data;
using ValoCapture.Core.Sessions;

namespace ValoCapture.App.Controls;

/// <summary>イベント種別 → タイムラインと同じ色。</summary>
public sealed class EventTypeToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        TimelineBar.BrushFor((MatchEventType)value);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
