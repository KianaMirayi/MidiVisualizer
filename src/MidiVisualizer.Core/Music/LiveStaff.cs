namespace MidiVisualizer.Core.Music;

/// <summary>
/// 实时五线谱的横向排版模型。
///
/// 规则（按用户要求）：
/// 1. 先后弹奏的音各自占一「列」，从左往右依次排列；
/// 2. 几乎同时按下（默认 70ms 内，且前一个音还按着）的音算柱式和弦，并进同一列纵向叠起来；
/// 3. 一收到 note-off 就把该音从显示里去掉（不做踏板延音）；
/// 4. 谱面上已经没有任何可见音符时，下一次弹奏从最左边重新开始。
/// </summary>
public sealed class LiveStaff
{
    /// <summary>判定为「同时按下」的时间窗（秒）。人类弹和弦的先后差通常在 50ms 内。</summary>
    private const double ChordWindowSeconds = 0.07;

    private const int MaxColumns = 160;

    private readonly List<List<int>> _columns = [];
    private readonly Dictionary<int, List<int>> _active = [];
    private double _lastNoteOnTime = double.NegativeInfinity;

    /// <summary>每一列的音（已按音高排序；空列表示该位置的音已经放开了，但位置保留以维持间距）。</summary>
    public IReadOnlyList<List<int>> Columns => _columns;

    public bool HasVisibleNotes => _columns.Any(column => column.Count > 0);

    public void NoteOn(int note, double timeSeconds)
    {
        // 同音重复触发：先把上一个实例去掉
        if (_active.ContainsKey(note))
        {
            NoteOff(note);
        }

        bool joinCurrent = _columns.Count > 0
                           && _active.Count > 0
                           && timeSeconds - _lastNoteOnTime <= ChordWindowSeconds;

        if (!joinCurrent && !HasVisibleNotes)
        {
            // 谱面已经空了：从头开始
            Clear();
        }

        List<int> target;
        if (joinCurrent)
        {
            target = _columns[^1];
        }
        else
        {
            target = [];
            _columns.Add(target);
        }

        if (!target.Contains(note))
        {
            target.Add(note);
            target.Sort();
        }

        _active[note] = target;
        _lastNoteOnTime = timeSeconds;

        Trim();
    }

    public void NoteOff(int note)
    {
        if (!_active.Remove(note, out List<int>? column))
        {
            return;
        }

        column.Remove(note);
    }

    public void Clear()
    {
        _columns.Clear();
        _active.Clear();
        _lastNoteOnTime = double.NegativeInfinity;
    }

    /// <summary>给渲染用的快照：列顺序 + 每列的音。</summary>
    public List<int[]> Snapshot() => _columns.Select(column => column.ToArray()).ToList();

    private void Trim()
    {
        while (_columns.Count > MaxColumns && _columns[0].Count == 0)
        {
            _columns.RemoveAt(0);
        }
    }
}
