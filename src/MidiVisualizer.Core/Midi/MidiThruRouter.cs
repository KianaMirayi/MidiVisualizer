namespace MidiVisualizer.Core.Midi;

/// <summary>
/// MIDI 直通：把输入端口收到的消息转发到一个或多个输出端口（例如 loopMIDI → Kontakt 7）。
/// 支持通道过滤和移调；断开时会先发 All Notes Off。
/// </summary>
public sealed class MidiThruRouter : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<int, MidiOutputService> _ports = new();

    /// <summary>直通总开关。</summary>
    public volatile bool Enabled = true;

    /// <summary>只转发这个通道（0~15）；-1 表示所有通道。</summary>
    public volatile int ChannelFilter = -1;

    /// <summary>转发前移调（半音，-24~+24）。</summary>
    public volatile int Transpose;

    /// <summary>已转发的消息条数（给界面显示用）。</summary>
    private int _forwardedCount;

    public int ForwardedCount => Volatile.Read(ref _forwardedCount);

    public IReadOnlyList<string> OpenPortNames
    {
        get
        {
            lock (_gate)
            {
                return _ports.Values.Select(p => p.DeviceName).ToArray();
            }
        }
    }

    public bool HasPorts
    {
        get
        {
            lock (_gate)
            {
                return _ports.Count > 0;
            }
        }
    }

    /// <summary>
    /// 让打开的输出端口正好等于 given 里的这些（多退少补）。
    /// 返回每个端口的打开结果，便于界面提示哪一个失败了。
    /// </summary>
    public List<(string Name, string? Error)> SetPorts(IEnumerable<int> deviceIndices)
    {
        var wanted = deviceIndices.Distinct().ToHashSet();
        var results = new List<(string, string?)>();

        lock (_gate)
        {
            // 关掉不再需要的
            foreach (int index in _ports.Keys.Where(i => !wanted.Contains(i)).ToArray())
            {
                _ports[index].Dispose();
                _ports.Remove(index);
            }

            var catalog = MidiOutputService.ListDevices();

            // 打开新增的
            foreach (int index in wanted.Where(i => !_ports.ContainsKey(i)))
            {
                var service = new MidiOutputService();
                try
                {
                    service.Open(index);
                    _ports[index] = service;
                    results.Add((service.DeviceName, null));
                }
                catch (Exception ex)
                {
                    service.Dispose();
                    string name = catalog.FirstOrDefault(d => d.Index == index)?.Name ?? $"#{index}";
                    results.Add((name, ex.Message));
                }
            }
        }

        return results;
    }

    /// <summary>把一条输入消息转发出去。回调线程上调用，必须快。</summary>
    public void Forward(in MidiMessage message)
    {
        if (!Enabled || message.Kind == MidiMessageKind.Other)
        {
            return;
        }

        int channelFilter = ChannelFilter;
        if (channelFilter >= 0 && message.Channel != channelFilter)
        {
            return;
        }

        int data1 = message.Data1;
        int data2 = message.Data2;

        if (message.IsNote)
        {
            int transpose = Transpose;
            data1 += transpose;
            if (data1 is < 0 or > 127)
            {
                return;   // 移调后跑出音域，直接丢掉，避免音源收到越界数据
            }
        }

        byte status = message.Kind switch
        {
            MidiMessageKind.NoteOn => (byte)(0x90 | message.Channel),
            MidiMessageKind.NoteOff => (byte)(0x80 | message.Channel),
            MidiMessageKind.ControlChange => (byte)(0xB0 | message.Channel),
            _ => (byte)0,
        };

        if (status == 0)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var port in _ports.Values)
            {
                port.Send(status, (byte)data1, (byte)data2);
            }
        }

        Interlocked.Increment(ref _forwardedCount);
    }

    /// <summary>给所有打开的端口补发一次踏板与按键复位。</summary>
    public void PanicAll()
    {
        lock (_gate)
        {
            foreach (var port in _ports.Values)
            {
                port.SendAllNotesOff();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var port in _ports.Values)
            {
                port.Dispose();
            }

            _ports.Clear();
        }
    }
}
