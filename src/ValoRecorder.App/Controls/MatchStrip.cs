using System.Windows;
using System.Windows.Media;
using ValoRecorder.Core.Sessions;

namespace ValoRecorder.App.Controls;

/// <summary>試合一覧のカードに出す細いミニタイムライン（キル・デスの位置だけ）。</summary>
public sealed class MatchStrip : FrameworkElement
{
    public static readonly DependencyProperty EventsProperty = DependencyProperty.Register(
        nameof(Events), typeof(IReadOnlyList<MatchEvent>), typeof(MatchStrip),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(
        nameof(Duration), typeof(TimeSpan), typeof(MatchStrip),
        new FrameworkPropertyMetadata(TimeSpan.Zero, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<MatchEvent>? Events { get => (IReadOnlyList<MatchEvent>?)GetValue(EventsProperty); set => SetValue(EventsProperty, value); }
    public TimeSpan Duration { get => (TimeSpan)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRoundedRectangle(Palette.Field, null, new Rect(0, 0, w, h), h / 2, h / 2);
        if (Events is null || Duration <= TimeSpan.Zero) return;

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h), h / 2, h / 2));
        foreach (var e in Events)
        {
            var x = Math.Clamp((e.VideoTimeMs ?? 0) / Duration.TotalMilliseconds, 0, 1) * w;
            dc.DrawRectangle(TimelineBar.BrushFor(e.Type), null, new Rect(x - 1, 0, 2, h));
        }
        dc.Pop();
    }
}
