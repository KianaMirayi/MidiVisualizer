namespace MidiStaffVisualizer.Core.Music;

/// <summary>
/// MIDI 音号（0~127）与五线谱位置之间的换算。
/// 中心思想：把每个音映射成一个"全音阶序号"（diatonic index），
/// 该序号每 +1 在五线谱上就往上走半格（半个线间距），谱线落在偶数序号的奇偶规律上。
/// </summary>
public static class Pitch
{
    /// <summary>升号音名。</summary>
    public static readonly string[] SharpNames =
        ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    /// <summary>降号音名。</summary>
    public static readonly string[] FlatNames =
        ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"];

    /// <summary>十二平均律里"带变化音"的音级：C# D# F# G# A#。</summary>
    private static readonly bool[] AccidentalPitchClasses =
        [false, true, false, true, false, false, true, false, true, false, true, false];

    /// <summary>升号写法下，音级在八度内的全音阶步数（C# 记在 C 的位置）。</summary>
    private static readonly int[] SharpStepOfPitchClass = [0, 0, 1, 1, 2, 3, 3, 4, 4, 5, 5, 6];

    /// <summary>降号写法下，音级在八度内的全音阶步数（D♭ 记在 D 的位置）。</summary>
    private static readonly int[] FlatStepOfPitchClass = [0, 1, 1, 2, 2, 3, 4, 4, 5, 5, 6, 6];

    public const int MiddleC = 60;

    public static int PitchClass(int note) => ((note % 12) + 12) % 12;

    /// <summary>科学音名八度：MIDI 60 = C4。</summary>
    public static int Octave(int note) => note / 12 - 1;

    /// <summary>全音阶步数（受升降号写法影响）。</summary>
    public static int DiatonicStep(int note, bool preferFlats)
        => (preferFlats ? FlatStepOfPitchClass : SharpStepOfPitchClass)[PitchClass(note)];

    /// <summary>
    /// 全音阶序号：C4 = 28，E4 = 30（高音谱表最下面一条线），G2 = 18（低音谱表最下面一条线）。
    /// 该序号每 +1，在五线谱上往上走半格（半个线间距）。
    /// </summary>
    public static int DiatonicIndex(int note, bool preferFlats)
        => Octave(note) * 7 + DiatonicStep(note, preferFlats);

    public static bool IsBlackKey(int note) => AccidentalPitchClasses[PitchClass(note)];

    /// <summary>该音是否需要用升降号记谱。</summary>
    public static bool NeedsAccidental(int note) => IsBlackKey(note);

    public static string Name(int note, bool preferFlats)
        => (preferFlats ? FlatNames : SharpNames)[PitchClass(note)] + Octave(note);

    /// <summary>只要音名，不带八度。</summary>
    public static string StepName(int note, bool preferFlats)
        => (preferFlats ? FlatNames : SharpNames)[PitchClass(note)];

    /// <summary>变化音记号（可能为空字符串）。</summary>
    public static string AccidentalSymbol(int note, bool preferFlats)
        => IsBlackKey(note) ? (preferFlats ? "\u266D" : "\u266F") : string.Empty;

    /// <summary>留给简谱/唱名显示用。</summary>
    public static string Solfege(int note, bool preferFlats)
    {
        string[] sharp = ["do", "di", "re", "ri", "mi", "fa", "fi", "sol", "si", "la", "li", "ti"];
        string[] flat = ["do", "ra", "re", "me", "mi", "fa", "se", "sol", "le", "la", "te", "ti"];
        return (preferFlats ? flat : sharp)[PitchClass(note)];
    }
}
