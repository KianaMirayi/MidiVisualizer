using System.Runtime.InteropServices;

namespace MidiVisualizer.Core.Midi;

/// <summary>
/// 用一个 winmm MIDI 输出端口发消息。典型用途：指向 loopMIDI 之类的虚拟端口，
/// 再由 Kontakt 7（独立版或 DAW 里）接收，形成「键盘 → 本软件 → 音源」的直通链。
/// </summary>
public sealed class MidiOutputService : IDisposable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MidiOutCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        public uint DriverVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ProductName;

        public ushort Technology;
        public ushort Voices;
        public ushort Notes;
        public uint ChannelMask;
        public uint Support;
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint midiOutGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint midiOutGetDevCaps(UIntPtr deviceId, ref MidiOutCaps caps, uint size);

    [DllImport("winmm.dll")]
    private static extern uint midiOutOpen(out IntPtr handle, uint deviceId, IntPtr callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll")]
    private static extern uint midiOutClose(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern uint midiOutShortMsg(IntPtr handle, uint message);

    [DllImport("winmm.dll")]
    private static extern uint midiOutReset(IntPtr handle);

    /// <summary>枚举系统里的 MIDI 输出端口（loopMIDI 新建的端口也会出现在这里）。</summary>
    public static List<MidiDeviceInfo> ListDevices()
    {
        var result = new List<MidiDeviceInfo>();
        uint count = midiOutGetNumDevs();
        for (uint i = 0; i < count; i++)
        {
            var caps = new MidiOutCaps { ProductName = string.Empty };
            uint rc = midiOutGetDevCaps(new UIntPtr(i), ref caps, (uint)Marshal.SizeOf<MidiOutCaps>());
            result.Add(new MidiDeviceInfo
            {
                Index = (int)i,
                Name = rc == 0 && !string.IsNullOrWhiteSpace(caps.ProductName)
                    ? caps.ProductName
                    : $"MIDI 输出 #{i}",
            });
        }

        return result;
    }

    private IntPtr _handle = IntPtr.Zero;

    public int DeviceIndex { get; private set; } = -1;

    public string DeviceName { get; private set; } = string.Empty;

    public bool IsOpen => _handle != IntPtr.Zero;

    public void Open(int deviceIndex)
    {
        if (IsOpen)
        {
            throw new InvalidOperationException("该输出端口已经打开。");
        }

        uint rc = midiOutOpen(out _handle, (uint)deviceIndex, IntPtr.Zero, IntPtr.Zero, 0);
        if (rc != 0)
        {
            _handle = IntPtr.Zero;
            throw new InvalidOperationException(
                $"打开 MIDI 输出「{DeviceName}」失败（winmm 错误码 {rc}）。若端口被其它程序独占，请先关闭它。");
        }

        DeviceIndex = deviceIndex;
        DeviceName = ListDevices().FirstOrDefault(d => d.Index == deviceIndex)?.Name ?? $"#{deviceIndex}";
    }

    /// <summary>
    /// 线程安全地发一条短消息。winmm 的 midiOutShortMsg 本身可跨线程调用。
    /// </summary>
    public void Send(byte status, byte data1, byte data2)
    {
        IntPtr handle = _handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        uint packed = (uint)(status | (data1 << 8) | (data2 << 16));
        midiOutShortMsg(handle, packed);
    }

    /// <summary>把所有通道的按键与踏板复位，避免音源里留下卡住的长音。</summary>
    public void SendAllNotesOff()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        for (int channel = 0; channel < 16; channel++)
        {
            byte status = (byte)(0xB0 | channel);
            Send(status, 64, 0);    // 踏板抬起
            Send(status, 123, 0);   // All Notes Off
            Send(status, 120, 0);   // All Sound Off
        }
    }

    public void Close()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            SendAllNotesOff();
            midiOutReset(_handle);
            midiOutClose(_handle);
        }
        catch
        {
            // 关闭阶段忽略异常
        }
        finally
        {
            _handle = IntPtr.Zero;
            DeviceIndex = -1;
            DeviceName = string.Empty;
        }
    }

    public void Dispose() => Close();
}
