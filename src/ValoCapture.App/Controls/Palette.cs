using System.Windows;
using System.Windows.Media;

namespace ValoCapture.App.Controls;

/// <summary>自前描画のコントロールで使う色・フォント（App.xaml の同名リソースと同じ値）。</summary>
internal static class Palette
{
    public static readonly Brush Text = Frozen(0xE8, 0xEC, 0xF2);
    public static readonly Brush Dim = Frozen(0x85, 0x92, 0xA5);
    public static readonly Brush Field = Frozen(0x0B, 0x10, 0x16);
    public static readonly Brush Played = Frozen(0x2B, 0x3A, 0x4C);
    public static readonly Brush Raised = Frozen(0x22, 0x30, 0x42);
    public static readonly Brush Kill = Frozen(0x6F, 0xD3, 0xA8);
    public static readonly Brush Death = Frozen(0xFF, 0x5D, 0x5D);
    public static readonly Brush Assist = Frozen(0x7F, 0xB2, 0xF0);
    public static readonly Pen TooltipPen = FrozenPen(Frozen(0x3A, 0x4A, 0x5E), 1);

    public static readonly FontFamily NumFont = new("Bahnschrift, Segoe UI");
    public static readonly FontFamily UiFont = new("Yu Gothic UI, Meiryo UI, Segoe UI");

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}
