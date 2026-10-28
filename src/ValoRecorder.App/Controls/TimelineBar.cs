using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ValoRecorder.App.ViewModels;
using ValoRecorder.Core.Sessions;

namespace ValoRecorder.App.Controls;

/// <summary>
/// 動画全体を横棒で表し、イベントの位置に印を付ける。クリック／ドラッグでシーク、
/// 印にマウスを乗せると「01:24 キル」の吹き出しを出す（吹き出しはコントロールの上にはみ出して描く）。
/// </summary>
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
        MatchEventType.Kill => Palette.Kill,
        MatchEventType.Death => Palette.Death,
        MatchEventType.Assist => Palette.Assist,
        _ => Palette.Dim,
    };

    private const double BarHeight = 8;
    private EventItemViewModel? _hovered;

    public TimelineBar()
    {
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
        ClipToBounds = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        var barTop = (h - BarHeight) / 2;

        // マウス判定のため全面を透明で塗る
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        dc.DrawRoundedRectangle(Palette.Field, null, new Rect(0, barTop, w, BarHeight), 4, 4);
        if (Duration <= TimeSpan.Zero) return;

        var px = X(Position, w);
        dc.DrawRoundedRectangle(Palette.Played, null, new Rect(0, barTop, px, BarHeight), 4, 4);

        foreach (var m in Items())
        {
            var x = X(m.VideoTime, w);
            var rect = m.Type switch
            {
                MatchEventType.RoundStart => new Rect(x - 0.5, h * 0.62, 1, h * 0.38),
                MatchEventType.Assist => new Rect(x - 1.5, h * 0.25, 3, h * 0.5),
                _ => new Rect(x - 1.5, h * 0.1, 3, h * 0.8),
            };
            dc.DrawRoundedRectangle(BrushFor(m.Type), null, rect, 1.5, 1.5);
        }

        // 再生位置：縦線＋上端の丸いつまみ
        dc.DrawRectangle(Palette.Text, null, new Rect(px - 1, -4, 2, h + 6));
        dc.DrawEllipse(Palette.Text, null, new Point(px, -4), 6, 6);

        if (_hovered is not null) DrawTooltip(dc, _hovered, w);
    }

    private void DrawTooltip(DrawingContext dc, EventItemViewModel m, double width)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var time = new FormattedText(m.TimeText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(Palette.NumFont, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 12, Palette.Text, dpi);
        var label = new FormattedText(m.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(Palette.UiFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 12, Palette.Text, dpi);

        const double pad = 9, dot = 8, gap = 6, height = 24;
        var boxWidth = pad + dot + gap + time.Width + gap + label.Width + pad;
        var x = Math.Clamp(X(m.VideoTime, width) - boxWidth / 2, 0, Math.Max(0, width - boxWidth));
        var y = -height - 12;

        dc.DrawRoundedRectangle(Palette.Raised, Palette.TooltipPen, new Rect(x, y, boxWidth, height), 6, 6);
        dc.DrawEllipse(BrushFor(m.Type), null, new Point(x + pad + dot / 2, y + height / 2), dot / 2, dot / 2);
        dc.DrawText(time, new Point(x + pad + dot + gap, y + (height - time.Height) / 2));
        dc.DrawText(label, new Point(x + pad + dot + gap + time.Width + gap, y + (height - label.Height) / 2));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        CaptureMouse();
        RaiseSeek(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var x = e.GetPosition(this).X;
        if (IsMouseCaptured) RaiseSeek(x);

        // 6px 以内のいちばん近い印を吹き出しにする（ラウンド開始は除く）
        var near = Duration <= TimeSpan.Zero ? null : Items()
            .Where(m => m.Type != MatchEventType.RoundStart)
            .Select(m => (m, d: Math.Abs(X(m.VideoTime, ActualWidth) - x)))
            .Where(t => t.d <= 6)
            .OrderBy(t => t.d)
            .Select(t => t.m)
            .FirstOrDefault();
        if (near != _hovered)
        {
            _hovered = near;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hovered is null) return;
        _hovered = null;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) => ReleaseMouseCapture();

    private IEnumerable<EventItemViewModel> Items() => Markers?.OfType<EventItemViewModel>() ?? [];

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

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _hovered = null;
        InvalidateVisual();
    }
}
