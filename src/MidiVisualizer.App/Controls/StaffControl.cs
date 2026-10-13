using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MidiVisualizer.App.Rendering;
using MidiVisualizer.Core.Music;

namespace MidiVisualizer.App.Controls;

/// <summary>
/// 常驻显示「高音谱号 + 低音谱号」的大谱表，谱号压在五线上。
///
/// 音符排版：先后弹奏的音从左往右依次排列（每列间距 3 个谱间距），
/// 只有几乎同时按下的柱式和弦才并进同一列纵向叠起来；
/// 收到 note-off 立刻把该音从显示里去掉（不做踏板延音）。
/// </summary>
public sealed class StaffControl : Control
{
    /// <summary>每一列的音（列按时间从左往右排；空列表示该位置的音已放开，位置保留以维持间距）。</summary>
    public static readonly StyledProperty<IReadOnlyList<int[]>?> ColumnsProperty =
        AvaloniaProperty.Register<StaffControl, IReadOnlyList<int[]>?>(nameof(Columns));

    /// <summary>变化音优先写成降号（如 D♭）而不是升号（如 C♯）。</summary>
    public static readonly StyledProperty<bool> PreferFlatsProperty =
        AvaloniaProperty.Register<StaffControl, bool>(nameof(PreferFlats));

    public static readonly StyledProperty<bool> ShowNoteNamesProperty =
        AvaloniaProperty.Register<StaffControl, bool>(nameof(ShowNoteNames));

    static StaffControl()
    {
        AffectsRender<StaffControl>(ColumnsProperty, PreferFlatsProperty, ShowNoteNamesProperty);
    }

    public IReadOnlyList<int[]>? Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public bool PreferFlats
    {
        get => GetValue(PreferFlatsProperty);
        set => SetValue(PreferFlatsProperty, value);
    }

    public bool ShowNoteNames
    {
        get => GetValue(ShowNoteNamesProperty);
        set => SetValue(ShowNoteNamesProperty, value);
    }

    // 全音阶序号参照点：G2 = 18（低音谱表最低线）、E4 = 30（高音谱表最低线）、F5 = 38（高音谱表最高线）
    private const int LowestLineIndex = 18;
    private const int TrebleBottomLineIndex = 30;
    private const int HighestLineIndex = 38;

    /// <summary>高音谱表与低音谱表的分界：中央 C（序号 28）及以上走高音谱表。</summary>
    private const int TrebleSplitIndex = 28;

    /// <summary>相邻两列（即相邻两次弹奏）的间距，单位是谱间距。</summary>
    private const double ColumnSpacingInSpaces = 3.0;

    // ---- 谱号外观参数（单位：谱间距）----
    // Bravura 的谱号是雕版比例（已经很细），所以不再横向压缩；
    // 但大谱表两行之间只有 2 个谱间距（严格的音高映射决定的），谱号容易怼在一起，
    // 因此两个谱号各自往回收一点，保证中间留出约 1.1 个谱间距的净空。

    /// <summary>高音谱号墨迹高度。</summary>
    private const double TrebleInkHeightInSpaces = 6.3;

    /// <summary>高音谱号横向压缩比（Bravura 已是雕版比例，保持 1.0）。</summary>
    private const double TrebleWidthScale = 1.0;

    /// <summary>高音谱号整体上移量，避免下探到低音谱号。</summary>
    private const double TrebleLiftInSpaces = 0.30;

    /// <summary>低音谱号墨迹高度（3.1 时两个圆点间距正好 1 个谱间距，与真实乐谱一致）。</summary>
    private const double BassInkHeightInSpaces = 3.1;

    private const double BassWidthScale = 1.0;

    /// <summary>低音谱号整体下移量。</summary>
    private const double BassDropInSpaces = 0.15;

    private static readonly IBrush PanelBrush = new SolidColorBrush(Color.Parse("#14161A"));
    private static readonly IBrush StaffLineBrush = new SolidColorBrush(Color.Parse("#E6E8EC"));
    private static readonly IBrush ClefBrush = new SolidColorBrush(Color.Parse("#F7F8FA"));
    private static readonly IBrush NoteBrush = new SolidColorBrush(Color.Parse("#FFB454"));
    private static readonly IBrush AccidentalBrush = new SolidColorBrush(Color.Parse("#FFF1D6"));
    private static readonly IBrush NoteNameBrush = new SolidColorBrush(Color.Parse("#AAB2BF"));

    private static readonly Typeface NameTypeface = new("Segoe UI");

    public override void Render(DrawingContext context)
    {
        Size size = Bounds.Size;
        double width = size.Width;
        double height = size.Height;
        if (width < 160 || height < 90)
        {
            return;
        }

        context.FillRectangle(PanelBrush, new Rect(0, 0, width, height));

        double pad = Math.Max(8, height * 0.05);

        // 谱线间距：整张谱表按 13 个间距分配高度
        double space = Math.Clamp((height - 2 * pad) / 13.0, 6.5, 26);

        double yTop = pad + 1.6 * space;
        double YOf(int index) => yTop + (HighestLineIndex - index) * space / 2.0;

        double lineThickness = Math.Max(1.0, space * 0.08);
        var staffPen = new Pen(StaffLineBrush, lineThickness);

        double leftPad = Math.Max(6, pad * 0.45);
        double linesEnd = width - Math.Max(10, pad * 0.7);

        // ---- 谱号尺寸（按字形内部锚点对齐到谱线，再各自往外挪一点拉开净空）----
        double trebleInkHeight = TrebleInkHeightInSpaces * space;
        double g4LineY = YOf(32) - TrebleLiftInSpaces * space;
        double trebleWidth = MusicGlyphs.InkWidthFor(MusicGlyphs.TrebleClef, trebleInkHeight, TrebleWidthScale);

        double bassInkHeight = BassInkHeightInSpaces * space;
        double f3LineY = YOf(24) + BassDropInSpaces * space;
        double bassWidth = MusicGlyphs.InkWidthFor(MusicGlyphs.BassClef, bassInkHeight, BassWidthScale);

        double clefLeft = leftPad + space * 0.2;
        double clefRight = clefLeft + Math.Max(trebleWidth, bassWidth);

        double noteRx = space * 0.63;
        double noteRy = space * 0.47;
        double accidentalInk = space * 1.6;   // 比符头略大一点点即可，不能喧宾夺主
        double sharpWidth = MusicGlyphs.InkWidthFor(MusicGlyphs.Sharp, accidentalInk);

        // 升降号画在符头左侧，所以第一列必须把这部分宽度预留出来，否则会压到谱号上
        double firstColumnLead = noteRx + space * 0.3 + sharpWidth + space * 0.4;
        double noteAreaLeft = clefRight + firstColumnLead;

        // 音符区的裁剪左边界（紧贴谱号右侧）：横向滚动时音符不会盖住谱号
        double noteClipLeft = clefRight + space * 0.25;

        if (linesEnd - noteAreaLeft < space * 4)
        {
            return;
        }

        // ---- 两组五线：从最左边一直画到右边，让谱号压在线上（而不是被挤到五线谱旁边）----
        for (int i = 0; i < 5; i++)
        {
            double yTreble = YOf(TrebleBottomLineIndex + i * 2);
            double yBass = YOf(LowestLineIndex + i * 2);
            context.DrawLine(staffPen, new Point(leftPad, yTreble), new Point(linesEnd, yTreble));
            context.DrawLine(staffPen, new Point(leftPad, yBass), new Point(linesEnd, yBass));
        }

        // ---- 谱号（常驻）----
        MusicGlyphs.DrawAnchored(
            context, MusicGlyphs.TrebleClefChar, MusicGlyphs.TrebleClef, trebleInkHeight,
            clefLeft, g4LineY, MusicGlyphs.TrebleBowlCenterFractionY, ClefBrush, TrebleWidthScale);

        MusicGlyphs.DrawAnchored(
            context, MusicGlyphs.BassClefChar, MusicGlyphs.BassClef, bassInkHeight,
            clefLeft, f3LineY, MusicGlyphs.BassDotsCenterFractionY, ClefBrush, BassWidthScale);

        // ---- 音符 ----
        IReadOnlyList<int[]>? columns = Columns;
        if (columns is null || columns.Count == 0)
        {
            return;
        }

        var ledgerPen = new Pen(StaffLineBrush, lineThickness);

        // 每列一个 x：从左往右依次排开
        double cursor = noteAreaLeft;
        double totalWidth = cursor + Math.Max(0, columns.Count - 1) * space * ColumnSpacingInSpaces + space;
        double offset = totalWidth > linesEnd ? linesEnd - totalWidth : 0;

        // 裁剪到音符区（用 using 声明，方法结束时自动弹出裁剪）
        using var noteClip = context.PushClip(
            new Rect(noteClipLeft, 0, Math.Max(0, linesEnd - noteClipLeft), height));

        for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            int[] column = columns[columnIndex];
            if (column is null || column.Length == 0)
            {
                continue;   // 空列只占位，维持间距稳定
            }

            double noteX = cursor + columnIndex * space * ColumnSpacingInSpaces + offset;

            // 滚到谱号区域的列直接跳过（裁剪已保证不会盖住谱号）
            if (noteX + noteRx < noteClipLeft || noteX - space * 3 > width)
            {
                continue;
            }

            foreach (int note in column)
            {
                if (note is < 0 or > 127)
                {
                    continue;
                }

                int index = Pitch.DiatonicIndex(note, PreferFlats);
                double y = YOf(index);
                bool belongsToTreble = index >= TrebleSplitIndex;

                // 加线
                if (belongsToTreble)
                {
                    if (index == TrebleSplitIndex)
                    {
                        DrawLedger(context, ledgerPen, noteX, YOf(TrebleSplitIndex), noteRx, space);
                    }

                    for (int i = HighestLineIndex + 2; i <= index; i += 2)
                    {
                        DrawLedger(context, ledgerPen, noteX, YOf(i), noteRx, space);
                    }
                }
                else
                {
                    for (int i = LowestLineIndex - 2; i >= index; i -= 2)
                    {
                        DrawLedger(context, ledgerPen, noteX, YOf(i), noteRx, space);
                    }
                }

                // 符头（真实乐谱的符头是倾斜的）
                using (context.PushTransform(Matrix.CreateRotation(-0.32) * Matrix.CreateTranslation(noteX, y)))
                {
                    context.DrawEllipse(NoteBrush, null, new Point(0, 0), noteRx, noteRy);
                }

                // 升降号：紧贴符头左侧，字号明显小于之前的版本
                if (Pitch.NeedsAccidental(note))
                {
                    bool flat = PreferFlats;
                    MusicGlyphs.GlyphMetrics metrics = flat ? MusicGlyphs.Flat : MusicGlyphs.Sharp;
                    string glyph = flat ? MusicGlyphs.FlatChar : MusicGlyphs.SharpChar;
                    double accidentalWidth = MusicGlyphs.InkWidthFor(metrics, accidentalInk);
                    double accidentalLeft = noteX - noteRx - space * 0.3 - accidentalWidth;

                    MusicGlyphs.Draw(
                        context, glyph, metrics, accidentalInk,
                        accidentalLeft, y - accidentalInk / 2.0, AccidentalBrush);
                }

                // 音名
                if (ShowNoteNames)
                {
                    var label = new FormattedText(
                        Pitch.Name(note, PreferFlats),
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        NameTypeface,
                        Math.Max(9, space * 0.7),
                        NoteNameBrush);
                    context.DrawText(label, new Point(noteX + noteRx + space * 0.4, y - label.Height / 2.0));
                }
            }
        }
    }

    private static void DrawLedger(
        DrawingContext context,
        IPen pen,
        double centerX,
        double y,
        double noteRx,
        double space)
    {
        double half = noteRx + space * 0.6;
        context.DrawLine(pen, new Point(centerX - half, y), new Point(centerX + half, y));
    }
}
