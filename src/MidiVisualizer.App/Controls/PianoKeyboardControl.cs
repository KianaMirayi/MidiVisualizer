using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MidiVisualizer.Core.Music;

namespace MidiVisualizer.App.Controls;

/// <summary>
/// 88 键屏幕钢琴，按下（或在延音踏板下仍在响）的键会高亮，并在上方显示音名。
/// </summary>
public sealed class PianoKeyboardControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<int>?> NotesProperty =
        AvaloniaProperty.Register<PianoKeyboardControl, IReadOnlyList<int>?>(nameof(Notes));

    public static readonly StyledProperty<bool> ShowNoteNamesProperty =
        AvaloniaProperty.Register<PianoKeyboardControl, bool>(nameof(ShowNoteNames));

    public static readonly StyledProperty<bool> PreferFlatsProperty =
        AvaloniaProperty.Register<PianoKeyboardControl, bool>(nameof(PreferFlats));

    static PianoKeyboardControl()
    {
        AffectsRender<PianoKeyboardControl>(NotesProperty, ShowNoteNamesProperty, PreferFlatsProperty);
    }

    public IReadOnlyList<int>? Notes
    {
        get => GetValue(NotesProperty);
        set => SetValue(NotesProperty, value);
    }

    public bool ShowNoteNames
    {
        get => GetValue(ShowNoteNamesProperty);
        set => SetValue(ShowNoteNamesProperty, value);
    }

    public bool PreferFlats
    {
        get => GetValue(PreferFlatsProperty);
        set => SetValue(PreferFlatsProperty, value);
    }

    private const int LowestNote = 21;    // A0
    private const int HighestNote = 108;  // C8

    private static readonly IBrush WhiteKeyBrush = new SolidColorBrush(Color.Parse("#E7E9EE"));
    private static readonly IBrush WhiteKeyPressedBrush = new SolidColorBrush(Color.Parse("#FFB454"));
    private static readonly IBrush BlackKeyBrush = new SolidColorBrush(Color.Parse("#15171C"));
    private static readonly IBrush BlackKeyPressedBrush = new SolidColorBrush(Color.Parse("#C8801F"));
    private static readonly IBrush KeyOutlineBrush = new SolidColorBrush(Color.Parse("#08090B"));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#FFC978"));
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    public override void Render(DrawingContext context)
    {
        Size size = Bounds.Size;
        double width = size.Width;
        double height = size.Height;
        if (width < 120 || height < 40)
        {
            return;
        }

        int whiteCount = 0;
        for (int note = LowestNote; note <= HighestNote; note++)
        {
            if (!Pitch.IsBlackKey(note))
            {
                whiteCount++;
            }
        }

        double labelHeight = ShowNoteNames ? Math.Min(26, height * 0.24) : 0;
        double keyTop = labelHeight;
        double keyHeight = height - keyTop;
        double whiteWidth = width / whiteCount;
        double blackWidth = whiteWidth * 0.62;
        double blackHeight = keyHeight * 0.62;

        HashSet<int> sounding = (Notes ?? Array.Empty<int>()).Where(n => n is >= LowestNote and <= HighestNote).ToHashSet();

        var outline = new Pen(KeyOutlineBrush, 1);

        // 白键
        var whiteLeft = new Dictionary<int, double>();
        double x = 0;
        for (int note = LowestNote; note <= HighestNote; note++)
        {
            if (Pitch.IsBlackKey(note))
            {
                continue;
            }

            whiteLeft[note] = x;
            var rect = new Rect(x + 0.5, keyTop, Math.Max(1, whiteWidth - 1), keyHeight);
            context.DrawRectangle(sounding.Contains(note) ? WhiteKeyPressedBrush : WhiteKeyBrush, outline, rect);
            x += whiteWidth;
        }

        // 黑键（中心落在相邻两个白键的交界处）
        for (int note = LowestNote; note <= HighestNote; note++)
        {
            if (!Pitch.IsBlackKey(note) || !whiteLeft.TryGetValue(note - 1, out double leftNeighbour))
            {
                continue;
            }

            double centerX = leftNeighbour + whiteWidth;
            var rect = new Rect(centerX - blackWidth / 2, keyTop, blackWidth, blackHeight);
            context.DrawRectangle(sounding.Contains(note) ? BlackKeyPressedBrush : BlackKeyBrush, outline, rect);
        }

        // 按下音的标签
        if (!ShowNoteNames || sounding.Count == 0)
        {
            return;
        }

        foreach (int note in sounding.OrderBy(n => n))
        {
            double centerX = Pitch.IsBlackKey(note)
                ? whiteLeft[note - 1] + whiteWidth
                : whiteLeft[note] + whiteWidth / 2;

            var label = new FormattedText(
                Pitch.Name(note, PreferFlats),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                Math.Max(10, labelHeight * 0.58),
                LabelBrush);

            context.DrawText(label, new Point(centerX - label.Width / 2, (labelHeight - label.Height) / 2));
        }
    }
}
