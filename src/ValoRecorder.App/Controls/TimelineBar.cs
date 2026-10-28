using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ValoRecorder.App.ViewModels;
using ValoRecorder.Core.Sessions;

namespace ValoRecorder.App.Controls;

/// <summary>動画全体を横棒で表し、イベントの位置に印を付ける。クリックでその位置へシーク。</summary>
public sealed class TimelineBar : FrameworkElement
{
    public static readonly DependencyProperty MarkersProperty = DependencyProperty.Register(
        nameof(Markers), typeof(IEnumerable), typeof(TimelineBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnMarkersChanged));

    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(
        nameof(Duration), typeof(TimeSpan), typeof(TimelineBar),
        new FrameworkPropertyMetadata(TimeSpan.Zero, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(TimeSpan), typeof(TimelineBar),
        new FrameworkPropertyMetadata(TimeSpan.Zero, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? Markers { get => (IEnumerable?)GetValue(MarkersProperty); set => SetValue(MarkersProperty, value); }
    public TimeSpan Duration { get => (TimeSpan)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public TimeSpan Position { get => (TimeSpan)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }

    /// <summary>クリック／ドラッグされた位置。</summary>
    public event Action<TimeSpan>? SeekRequested;

    public static Brush BrushFor(MatchEventType type) => type switch
    {
        MatchEventType.Kill => KillBrush,
        MatchEventType.Death => DeathBrush,
        MatchEventType.Assist => AssistBrush,
        _ => RoundBrush,
    };

    private static readonly Brush KillBrush = Frozen(Color.FromRgb(0x4a, 0xde, 0x9b));
    private static readonly Brush DeathBrush = Frozen(Color.FromRgb(0xff, 0x55, 0x5f));
    private static readonly Brush AssistBrush = Frozen(Color.FromRgb(0x6b, 0xb6, 0xff));
    private static readonly Brush RoundBrush = Frozen(Color.FromRgb(0x8a, 0x8f, 0x99));
    private static readonly Brush TrackBrush = Frozen(Color.FromRgb(0x2a, 0x2e, 0x36));
    private static readonly Brush PlayedBrush = Frozen(Color.FromRgb(0x3a, 0x40, 0x4c));
    private static readonly Pen PlayheadPen = FrozenPen(Brushes.White, 2);

    public TimelineBar()
    {
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, 0, w, h), 4, 4);
        if (Duration <= TimeSpan.Zero) return;

        dc.DrawRoundedRectangle(PlayedBrush, null, new Rect(0, 0, X(Position, w), h), 4, 4);

        if (Markers is not null)
        {
            foreach (var m in Markers.OfType<EventItemViewModel>())
            {
                var x = X(m.VideoTime, w);
                // ラウンド開始は細く低く、キル等は太く全高で
                var rect = m.Type == MatchEventType.RoundStart
                    ? new Rect(x - 0.5, h * 0.55, 1, h * 0.45)
                    : new Rect(x - 1.5, 2, 3, h - 4);
                dc.DrawRectangle(BrushFor(m.Type), null, rect);
            }
        }

        var px = X(Position, w);
        dc.DrawLine(PlayheadPen, new Point(px, -2), new Point(px, h + 2));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        CaptureMouse();
        RaiseSeek(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured) RaiseSeek(e.GetPosition(this).X);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) => ReleaseMouseCapture();

    private void RaiseSeek(double x)
    {
        if (Duration <= TimeSpan.Zero || ActualWidth <= 0) return;
        var ratio = Math.Clamp(x / ActualWidth, 0, 1);
        SeekRequested?.Invoke(TimeSpan.FromMilliseconds(Duration.TotalMilliseconds * ratio));
    }

    private double X(TimeSpan t, double width) =>
        Math.Clamp(t.TotalMilliseconds / Duration.TotalMilliseconds, 0, 1) * width;

    private static void OnMarkersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (TimelineBar)d;
        if (e.OldValue is INotifyCollectionChanged oldList) oldList.CollectionChanged -= bar.OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newList) newList.CollectionChanged += bar.OnCollectionChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Pen FrozenPen(Brush brush, double thickness)
    {
        var p = new Pen(brush, thickness);
        p.Freeze();
        return p;
    }
}
