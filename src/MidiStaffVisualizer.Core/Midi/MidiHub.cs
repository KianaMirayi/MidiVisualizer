using MidiStaffVisualizer.Core.Music;

namespace MidiStaffVisualizer.Core.Midi;

/// <summary>给界面「最近弹奏」列表用的一条记录。</summary>
public readonly record struct NoteEventInfo(int Note, int Velocity, bool IsNoteOn, long Timestamp);

/// <summary>
/// 把输入、直通、按键状态串在一起的中枢。
/// 显示层只关心「物理按下的音」：note-off 立刻生效，踏板只做原样直通给音源。
/// 所有状态更新都发生在 winmm 回调线程上；界面只读快照，通过 Changed 事件得到通知。
/// </summary>
public sealed class MidiHub : IDisposable
{
    private const int MaxRecentEvents = 512;

    private readonly MidiInputService _input = new();
    private readonly object _gate = new();
    private readonly Dictionary<int, int> _held = new();   // 手指正按着的音 → 力度
    private readonly Queue<NoteEventInfo> _recent = new();

    public MidiHub()
    {
        _input.MessageReceived += OnMessage;
    }

    /// <summary>状态发生变化（按键）时触发；在 MIDI 线程上。</summary>
    public event Action? Changed;

    public MidiThruRouter Thru { get; } = new();

    public bool IsOpen => _input.IsOpen;

    public string DeviceName => _input.DeviceName;

    /// <summary>当前手指按着的音（不含任何延音残留）。</summary>
    public int[] GetHeldNotes()
    {
        lock (_gate)
        {
            return _held.Keys.OrderBy(n => n).ToArray();
        }
    }

    public ChordInfo GetChord(bool preferFlats) => ChordDetector.Detect(GetHeldNotes(), preferFlats);

    /// <summary>取走并清空最近的音符事件（界面线程调用）。</summary>
    public List<NoteEventInfo> DrainRecentEvents()
    {
        lock (_gate)
        {
            var list = new List<NoteEventInfo>(_recent);
            _recent.Clear();
            return list;
        }
    }

    public void Open(int deviceIndex)
    {
        _input.Open(deviceIndex);
        Changed?.Invoke();
    }

    public void Close()
    {
        _input.Close();
        Thru.PanicAll();

        lock (_gate)
        {
            _held.Clear();
        }

        Changed?.Invoke();
    }

    private void OnMessage(MidiMessage message)
    {
        bool stateChanged = false;

        lock (_gate)
        {
            switch (message.Kind)
            {
                case MidiMessageKind.NoteOn:
                    _held[message.Data1] = message.Data2;
                    _recent.Enqueue(new NoteEventInfo(message.Data1, message.Data2, true, message.Timestamp));
                    stateChanged = true;
                    break;

                case MidiMessageKind.NoteOff:
                    // 按用户要求：不做踏板延音，note-off 就是取消显示
                    _held.Remove(message.Data1);
                    _recent.Enqueue(new NoteEventInfo(message.Data1, 0, false, message.Timestamp));
                    stateChanged = true;
                    break;

                case MidiMessageKind.ControlChange when message.IsAllNotesOff:
                    if (_held.Count > 0)
                    {
                        foreach (int note in _held.Keys)
                        {
                            _recent.Enqueue(new NoteEventInfo(note, 0, false, message.Timestamp));
                        }

                        _held.Clear();
                        stateChanged = true;
                    }

                    break;
            }

            while (_recent.Count > MaxRecentEvents)
            {
                _recent.Dequeue();
            }
        }

        // 直通原样转发（含 CC64 延音踏板、弯音、CC1 等），音源那边照常工作
        Thru.Forward(message);

        if (stateChanged)
        {
            Changed?.Invoke();
        }
    }

    public void Dispose()
    {
        _input.MessageReceived -= OnMessage;
        _input.Dispose();
        Thru.Dispose();
    }
}
