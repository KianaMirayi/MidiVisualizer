using CommunityToolkit.Mvvm.ComponentModel;

namespace MidiVisualizer.App.ViewModels;

/// <summary>「最近弹奏」列表里的一行。</summary>
public sealed class NoteLogItem
{
    public NoteLogItem(string time, string noteName, int noteNumber, int velocity)
    {
        Time = time;
        NoteName = noteName;
        NoteNumber = noteNumber;
        Velocity = velocity;
    }

    public string Time { get; }

    public string NoteName { get; }

    public int NoteNumber { get; }

    public int Velocity { get; }

    public string Display => $"{Time}   {NoteName,-4}  #{NoteNumber}   力度 {Velocity}";
}

/// <summary>一个可勾选的 MIDI 输出端口。</summary>
public partial class MidiOutputPortViewModel : ObservableObject
{
    private readonly Action _onSelectionChanged;

    public MidiOutputPortViewModel(int index, string name, Action onSelectionChanged)
    {
        Index = index;
        Name = name;
        _onSelectionChanged = onSelectionChanged;
    }

    public int Index { get; }

    public string Name { get; }

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged();
}
