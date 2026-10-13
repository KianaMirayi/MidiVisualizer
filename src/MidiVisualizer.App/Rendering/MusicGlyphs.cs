using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace MidiVisualizer.App.Rendering;

/// <summary>
/// 乐谱字形：使用随程序打包的 <b>Bravura</b>（SIL Open Font License 1.1 开源乐谱字体，
/// 见 Assets/LICENSE.txt）。它是雕版比例的字形，笔画比 Windows 自带的 Segoe UI Symbol 细得多。
///
/// 度量按"每 em"记录，坐标以基线为原点向下为正 —— 全部由实测得到
/// （把字形渲染成大图后做连通域分析），用它可以把字形精确地按目标墨迹高度摆到指定位置。
/// </summary>
internal static class MusicGlyphs
{
    public readonly record struct GlyphMetrics(double Left, double Top, double Right, double Bottom)
    {
        public double InkHeight => Bottom - Top;

        public double InkWidth => Right - Left;
    }

    public const string TrebleClefChar = "\U0001D11E";
    public const string BassClefChar = "\U0001D122";
    public const string SharpChar = "\u266F";
    public const string FlatChar = "\u266D";
    public const string NaturalChar = "\u266E";

    // 实测值（Bravura.otf）
    public static readonly GlyphMetrics TrebleClef = new(0.0000, -1.0971, 0.6700, 0.6557);
    public static readonly GlyphMetrics BassClef = new(-0.0043, -0.2629, 0.6829, 0.6329);
    public static readonly GlyphMetrics Sharp = new(0.0000, -0.3500, 0.2471, 0.3471);
    public static readonly GlyphMetrics Flat = new(0.0000, -0.4386, 0.2243, 0.1729);
    public static readonly GlyphMetrics Natural = new(0.0000, -0.3500, 0.2560, 0.3471);

    // ---- 字形内部锚点（实测：把字形渲染成大图后，用连通域分析量出来） ----
    // 高音谱号：螺旋圈心的孔洞重心，位于墨迹高度的 61.2% 处。乐理上它必须落在 G 线（小字一组 G4）上。
    public const double TrebleBowlCenterFractionY = 0.612;

    // 低音谱号：两个圆点的重心，位于墨迹高度的 29.2% 处；两点间距占墨迹高度的 27.8%（正好等于一个谱间距）。
    public const double BassDotsCenterFractionY = 0.292;
    public const double BassDotSpacingFraction = 0.278;

    private static readonly Typeface MusicTypeface =
        new(new FontFamily("avares://MidiVisualizer/Assets/Bravura.otf#Bravura"));

    public static double EmSizeForInkHeight(GlyphMetrics metrics, double inkHeight)
        => inkHeight / metrics.InkHeight;

    /// <summary>
    /// 字形的墨迹宽度。<paramref name="widthScale"/> 可把字形横向压窄（纵高不变）。
    /// Bravura 已经是雕版比例，一般用默认的 1.0。
    /// </summary>
    public static double InkWidthFor(GlyphMetrics metrics, double inkHeight, double widthScale = 1.0)
        => metrics.InkWidth * EmSizeForInkHeight(metrics, inkHeight) * widthScale;

    /// <summary>
    /// 按「字形内部锚点的目标 y」绘制：先把墨迹顶算出来，再摆字形。
    /// 用于谱号这种必须对齐到特定谱线的字形。
    /// </summary>
    public static void DrawAnchored(
        DrawingContext context,
        string text,
        GlyphMetrics metrics,
        double inkHeight,
        double inkLeft,
        double anchorY,
        double anchorFractionY,
        IBrush brush,
        double widthScale = 1.0)
    {
        double inkTop = anchorY - anchorFractionY * inkHeight;
        Draw(context, text, metrics, inkHeight, inkLeft, inkTop, brush, widthScale);
    }

    /// <summary>按「目标墨迹高度 + 墨迹左上角」绘制字形。</summary>
    public static void Draw(
        DrawingContext context,
        string text,
        GlyphMetrics metrics,
        double inkHeight,
        double inkLeft,
        double inkTop,
        IBrush brush,
        double widthScale = 1.0)
    {
        double emSize = EmSizeForInkHeight(metrics, inkHeight);
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            MusicTypeface,
            emSize,
            brush);

        // FormattedText 从"行盒左上角"开始画，先换算到基线，再减去墨迹相对基线的偏移
        double baselineY = inkTop - metrics.Top * emSize;

        if (Math.Abs(widthScale - 1.0) < 0.001)
        {
            context.DrawText(
                formatted,
                new Point(inkLeft - metrics.Left * emSize, baselineY - formatted.Baseline));
            return;
        }

        // 横向压窄：先把墨迹左边缘挪到局部原点，缩放后再平移到目标位置
        using (context.PushTransform(Matrix.CreateScale(widthScale, 1.0) * Matrix.CreateTranslation(inkLeft, 0)))
        {
            context.DrawText(formatted, new Point(-metrics.Left * emSize, baselineY - formatted.Baseline));
        }
    }
}
