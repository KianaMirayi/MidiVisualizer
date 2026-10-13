using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MidiStaffVisualizer.Core.Diagnostics;
using MidiStaffVisualizer.Core.Midi;
using MidiStaffVisualizer.Core.Music;

namespace MidiStaffVisualizer.App.ViewModels;

/// <summary>
/// 主界面的 ViewModel：MIDI 输入 → 五线谱显示 + MIDI 直通（给 Kontakt 7）。
/// 本软件不参与任何音频处理。
/// </summary>
public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly MidiHub _hub = new();
    private readonly LiveStaff _staff = new();
    private readonly DispatcherTimer _deviceWatchTimer;
    private string _lastInputSignature = string.Empty;
    private string _lastOutputSignature = string.Empty;
    private int _updatePending;
    private bool _suppressPortSync;
    private bool _disposed;

    public MainViewModel()
    {
        _hub.Changed += OnHubChanged;

        ReloadInputDevices();
        ReloadThruPorts();
        RefreshAudioEndpoints();
        ApplyThruSettings();

        _deviceWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _deviceWatchTimer.Tick += (_, _) => WatchDevices();
        _deviceWatchTimer.Start();

        // 插上键盘就能直接用：自动连接第一个输入端口
        if (InputDevices.Count > 0)
        {
            SelectedInputDevice = InputDevices[0];
            Connect();
        }
    }

    // ---------------- 集合 ----------------

    public ObservableCollection<MidiDeviceInfo> InputDevices { get; } = [];

    public ObservableCollection<MidiOutputPortViewModel> OutputPorts { get; } = [];

    public ObservableCollection<NoteLogItem> RecentNotes { get; } = [];

    public ObservableCollection<AudioEndpointInfo> AudioEndpoints { get; } = [];

    public IReadOnlyList<string> ChannelOptions { get; } =
        new[] { "全部通道" }.Concat(Enumerable.Range(1, 16).Select(i => $"通道 {i}")).ToArray();

    public IReadOnlyList<string> TransposeOptions { get; } =
        Enumerable.Range(-12, 25)
            .Select(v => v == 0 ? "不移调" : v > 0 ? $"+{v} 半音" : $"{v} 半音")
            .ToArray();

    // ---------------- 绑定属性 ----------------

    [ObservableProperty]
    private MidiDeviceInfo? _selectedInputDevice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleButtonText))]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusText = "未连接";

    [ObservableProperty]
    private string _statusDetail = "选择一个 MIDI 输入端口，然后点「连接」。";

    /// <summary>当前手指按着的音（屏幕键盘高亮用）。</summary>
    [ObservableProperty]
    private IReadOnlyList<int> _soundingNotes = Array.Empty<int>();

    /// <summary>五线谱的列快照：先后弹奏的列从左往右排，柱式和弦在同一列。</summary>
    [ObservableProperty]
    private IReadOnlyList<int[]> _staffColumns = Array.Empty<int[]>();

    [ObservableProperty]
    private string _chordRoot = string.Empty;

    [ObservableProperty]
    private string _chordQuality = string.Empty;

    [ObservableProperty]
    private string _chordBass = string.Empty;

    [ObservableProperty]
    private bool _preferFlats;

    [ObservableProperty]
    private bool _showNoteNames = true;

    [ObservableProperty]
    private bool _thruEnabled = true;

    [ObservableProperty]
    private int _transposeIndex = 12;

    [ObservableProperty]
    private int _channelFilterIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ForwardedText))]
    private int _forwardedCount;

    [ObservableProperty]
    private string _thruStatusText = "尚未选择 MIDI 输出端口";

    [ObservableProperty]
    private string _audioHint = string.Empty;

    [ObservableProperty]
    private string _routingGuide =
        "目标：让对方同时听到「你的麦克风」+「Kontakt 7 的钢琴」。\n" +
        "Focusrite 4i4 自带 Loopback，不需要虚拟声卡，按这 5 步接：\n" +
        "① 任务栏 Focusrite Notifier（F 图标）→ 右键 → Expose/Hide Windows Channels → 勾上 Loopback L + R\n" +
        "② Kontakt 7 的音频输出设备选 Focusrite USB Audio（WDM/WASAPI）。不要用 ASIO，否则 Loopback 混音里抓不到它的声音\n" +
        "③ Focusrite Control（4th Gen 是 Control 2）里给 Loopback 1-2 建一个 Custom Mix，推子只推两路：Analogue 1（麦克风）+ Playback 1-2（Kontakt）\n" +
        "④ 语音聊天的麦克风选 Loopback L + R（可能显示成 Microphone (Focusrite USB Audio)）\n" +
        "⑤ 语音聊天的扬声器必须换成另一个输出（主板声卡 / USB 耳机 / 或被 Expose 出来的 Playback 3-4），否则对方的声音会被灌回 Loopback，对方就听到自己的回声\n" +
        "提示：QQ 里的自动增益、回声消除、降噪会明显吃掉钢琴音色，建议全部关掉并手动设麦克风音量。";

    // ---------------- 命令 ----------------

    public string ToggleButtonText => IsConnected ? "断开" : "连接";

    public string ForwardedText => $"已转发 {ForwardedCount} 条 MIDI 消息";

    [RelayCommand]
    private void ToggleConnection()
    {
        if (IsConnected)
        {
            Disconnect();
        }
        else
        {
            Connect();
        }
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        ReloadInputDevices();
        ReloadThruPorts();
        RefreshAudioEndpoints();
    }

    [RelayCommand]
    private void Panic()
    {
        _hub.Thru.PanicAll();
        StatusDetail = "已向所有输出端口发送 All Notes Off / 踏板复位。";
    }

    [RelayCommand]
    private void ClearRecentNotes() => RecentNotes.Clear();

    /// <summary>打开 Windows 声音控制面板，方便设置虚拟声卡与"侦听此设备"。</summary>
    [RelayCommand]
    private void OpenSoundSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("mmsys.cpl") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusDetail = $"打不开声音控制面板：{ex.Message}";
        }
    }

    // ---------------- 连接管理 ----------------

    private void Connect()
    {
        if (SelectedInputDevice is null)
        {
            StatusText = "未选择输入端口";
            return;
        }

        try
        {
            _hub.Open(SelectedInputDevice.Index);
            IsConnected = true;
            StatusText = $"已连接：{SelectedInputDevice.Name}";
            StatusDetail = _hub.Thru.HasPorts
                ? "音符会显示在五线谱上，并已直通到所选输出端口。"
                : "音符会显示在五线谱上；还没选 MIDI 输出端口，Kontakt 7 收不到信号。";
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusText = "连接失败";
            StatusDetail = ex.Message;
        }
    }

    private void Disconnect()
    {
        try
        {
            _hub.Close();
        }
        catch
        {
            // 忽略关闭异常
        }

        IsConnected = false;
        _staff.Clear();
        StaffColumns = Array.Empty<int[]>();
        SoundingNotes = Array.Empty<int>();
        ChordRoot = ChordQuality = ChordBass = string.Empty;
        StatusText = "未连接";
        StatusDetail = "已断开输入端口，并已向输出端口发送复位。";
    }

    // ---------------- 设备枚举 ----------------

    private void ReloadInputDevices()
    {
        var devices = MidiInputService.ListDevices();
        _lastInputSignature = string.Join("|", devices.Select(d => d.Name));

        int? previous = SelectedInputDevice?.Index;
        MidiDeviceInfo? keep = previous is null
            ? null
            : devices.FirstOrDefault(d => d.Index == previous.Value);

        InputDevices.Clear();
        foreach (var device in devices)
        {
            InputDevices.Add(device);
        }

        if (keep is not null)
        {
            SelectedInputDevice = keep;
        }
        else if (InputDevices.Count > 0 && SelectedInputDevice is not null)
        {
            SelectedInputDevice = InputDevices[0];
        }
    }

    private void ReloadThruPorts()
    {
        var ports = MidiOutputService.ListDevices();
        _lastOutputSignature = string.Join("|", ports.Select(p => p.Name));

        var selected = _suppressPortSync
            ? new HashSet<int>()
            : OutputPorts.Where(p => p.IsSelected).Select(p => p.Index).ToHashSet();

        _suppressPortSync = true;

        OutputPorts.Clear();
        foreach (var port in ports)
        {
            var item = new MidiOutputPortViewModel(port.Index, port.Name, SyncThruPorts)
            {
                IsSelected = selected.Contains(port.Index),
            };
            OutputPorts.Add(item);
        }

        _suppressPortSync = false;
        SyncThruPorts();
    }

    private void SyncThruPorts()
    {
        if (_suppressPortSync)
        {
            return;
        }

        int[] wanted = OutputPorts.Where(p => p.IsSelected).Select(p => p.Index).ToArray();

        List<(string Name, string? Error)> results;
        try
        {
            results = _hub.Thru.SetPorts(wanted);
        }
        catch (Exception ex)
        {
            ThruStatusText = $"打开输出端口失败：{ex.Message}";
            return;
        }

        _suppressPortSync = true;
        foreach (var port in OutputPorts)
        {
            var failure = results.FirstOrDefault(r => r.Name == port.Name && r.Error is not null);
            if (failure.Error is not null)
            {
                port.IsSelected = false;
                ThruStatusText = $"「{port.Name}」打开失败：{failure.Error}";
            }
        }

        _suppressPortSync = false;

        var open = _hub.Thru.OpenPortNames;
        if (open.Count > 0)
        {
            ThruStatusText = "已直通到：" + string.Join("、", open);

            // 连接状态里那句提示也要跟着刷新，否则会一直显示"还没选输出端口"
            if (IsConnected)
            {
                StatusDetail = "音符会显示在五线谱上，并已直通到 " + string.Join("、", open) + "。";
            }
        }
        else if (OutputPorts.Count == 0)
        {
            ThruStatusText = "系统里没有 MIDI 输出端口。要让 Kontakt 7 收到信号，请先安装 loopMIDI 之类的虚拟 MIDI 端口。";
        }
        else
        {
            ThruStatusText = "尚未选择 MIDI 输出端口";

            if (IsConnected)
            {
                StatusDetail = "音符会显示在五线谱上；还没选 MIDI 输出端口，Kontakt 7 收不到信号。";
            }
        }
    }

    private void RefreshAudioEndpoints()
    {
        AudioEndpoints.Clear();
        List<AudioEndpointInfo> all;
        try
        {
            all = AudioEndpointProbe.ListAll();
        }
        catch (Exception ex)
        {
            AudioHint = $"枚举音频设备失败：{ex.Message}";
            return;
        }

        foreach (var endpoint in all)
        {
            AudioEndpoints.Add(endpoint);
        }

        var virtualCables = all.Where(e => e.LooksLikeVirtualCable).ToArray();
        bool hasLoopback = all.Any(e => e.IsHardwareLoopback);

        AudioHint = hasLoopback
            ? "检测到 Focusrite 的 Loopback 通道：语音软件的麦克风选它，就能让对方同时听到「你的麦克风 + Kontakt」。完全不需要虚拟声卡。"
            : virtualCables.Length == 0
                ? "没检测到虚拟声卡。若用 Focusrite 4i4，请在 Focusrite Notifier 里 Expose 出 Loopback L + R；其它声卡则需安装 VB-CABLE 或 VoiceMeeter。"
                : "检测到虚拟声卡：" + string.Join("、", virtualCables.Select(v => v.Name)) + "。";
    }

    /// <summary>每 2 秒检查一次热插拔：设备增减就刷新列表；正在用的输入掉了就断开。</summary>
    private void WatchDevices()
    {
        try
        {
            if (MidiInputService.DeviceSignature() != _lastInputSignature)
            {
                ReloadInputDevices();

                if (IsConnected && !_hub.IsOpen)
                {
                    IsConnected = false;
                    StatusText = "输入端口已断开";
                    StatusDetail = "原来的 MIDI 输入设备不见了，请重新选择端口并连接。";
                }
            }

            string outputSignature = string.Join("|", MidiOutputService.ListDevices().Select(p => p.Name));
            if (outputSignature != _lastOutputSignature)
            {
                ReloadThruPorts();
            }
        }
        catch
        {
            // 轮询失败不影响主流程
        }
    }

    private void ApplyThruSettings()
    {
        _hub.Thru.Enabled = ThruEnabled;
        _hub.Thru.Transpose = TransposeIndex - 12;
        _hub.Thru.ChannelFilter = ChannelFilterIndex - 1;
    }

    partial void OnThruEnabledChanged(bool value) => ApplyThruSettings();

    partial void OnTransposeIndexChanged(int value) => ApplyThruSettings();

    partial void OnChannelFilterIndexChanged(int value) => ApplyThruSettings();

    partial void OnPreferFlatsChanged(bool value) => RefreshChord();

    // ---------------- MIDI 线程 → 界面线程 ----------------

    private void OnHubChanged()
    {
        // 同一个 UI tick 里只投递一次，避免刷屏
        if (Interlocked.Exchange(ref _updatePending, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _updatePending, 0);
            ApplyHubState();
        });
    }

    private void ApplyHubState()
    {
        SoundingNotes = _hub.GetHeldNotes();
        ForwardedCount = _hub.Thru.ForwardedCount;
        RefreshChord();

        foreach (var e in _hub.DrainRecentEvents())
        {
            if (e.IsNoteOn)
            {
                _staff.NoteOn(e.Note, (double)e.Timestamp / Stopwatch.Frequency);

                RecentNotes.Insert(0, new NoteLogItem(
                    DateTime.Now.ToString("HH:mm:ss"),
                    Pitch.Name(e.Note, PreferFlats),
                    e.Note,
                    e.Velocity));

                while (RecentNotes.Count > 60)
                {
                    RecentNotes.RemoveAt(RecentNotes.Count - 1);
                }
            }
            else
            {
                // 不做踏板延音：note-off 立刻取消显示
                _staff.NoteOff(e.Note);
            }
        }

        StaffColumns = _staff.Snapshot();
    }

    private void RefreshChord()
    {
        var chord = _hub.GetChord(PreferFlats);
        ChordRoot = chord.Root;
        ChordQuality = chord.Quality;
        ChordBass = chord.BassLabel;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _deviceWatchTimer.Stop();
        _hub.Changed -= OnHubChanged;
        _hub.Dispose();
    }
}
