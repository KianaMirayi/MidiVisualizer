namespace MidiStaffVisualizer.Core.Midi;

/// <summary>短消息类别（只关心演奏相关的几类）。</summary>
public enum MidiMessageKind
{
    NoteOn,
    NoteOff,
    ControlChange,
    Other,
}

/// <summary>
/// 一条已解析的 MIDI 短消息。回调来自系统线程，字段全是值类型，保证不会被 GC 影响。
/// </summary>
public readonly struct MidiMessage
{
    public MidiMessageKind Kind { get; init; }

    /// <summary>0~15</summary>
    public int Channel { get; init; }

    /// <summary>音号（NoteOn/NoteOff）或控制器号（CC）。</summary>
    public int Data1 { get; init; }

    /// <summary>力度 / 控制器值。</summary>
    public int Data2 { get; init; }

    /// <summary>Stopwatch 时间戳，用于计算延迟与时长。</summary>
    public long Timestamp { get; init; }

    public bool IsNote => Kind is MidiMessageKind.NoteOn or MidiMessageKind.NoteOff;

    public bool IsSustainPedal => Kind == MidiMessageKind.ControlChange && Data1 == 64;

    /// <summary>延音踏板踩下（CC64 &gt;= 64）。</summary>
    public bool SustainDown => IsSustainPedal && Data2 >= 64;

    /// <summary>CC120 = All Sound Off，CC123 = All Notes Off。</summary>
    public bool IsAllNotesOff => Kind == MidiMessageKind.ControlChange && (Data1 == 120 || Data1 == 123);
}

/// <summary>系统里枚举出来的一个 MIDI 输入端口。</summary>
public sealed class MidiDeviceInfo
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;

    public override string ToString() => Name;
}
