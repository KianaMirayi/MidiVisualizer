using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MidiStaffVisualizer.Core.Midi;

/// <summary>
/// 用 winmm.dll 打开 MIDI 输入端口。
/// 系统回调来自独立线程：必须立刻返回，绝不能抛异常，否则会直接把进程带走。
/// </summary>
public sealed class MidiInputService : IDisposable
{
    private const int CallbackFunction = 0x00030000;
    private const int MimData = 0x3C3;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MidiInCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        public uint DriverVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ProductName;

        public uint Support;
    }

    private delegate void MidiInProc(IntPtr handle, uint message, IntPtr instance, IntPtr param1, IntPtr param2);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint midiInGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint midiInGetDevCaps(UIntPtr deviceId, ref MidiInCaps caps, uint size);

    [DllImport("winmm.dll")]
    private static extern uint midiInOpen(out IntPtr handle, uint deviceId, MidiInProc callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll")]
    private static extern uint midiInStart(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern uint midiInStop(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern uint midiInReset(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern uint midiInClose(IntPtr handle);

    /// <summary>枚举当前系统里的所有 MIDI 输入端口。</summary>
    public static List<MidiDeviceInfo> ListDevices()
    {
        var result = new List<MidiDeviceInfo>();
        uint count = midiInGetNumDevs();
        for (uint i = 0; i < count; i++)
        {
            var caps = new MidiInCaps { ProductName = string.Empty };
            uint rc = midiInGetDevCaps(new UIntPtr(i), ref caps, (uint)Marshal.SizeOf<MidiInCaps>());
            result.Add(new MidiDeviceInfo
            {
                Index = (int)i,
                Name = rc == 0 && !string.IsNullOrWhiteSpace(caps.ProductName)
                    ? caps.ProductName
                    : $"MIDI 输入 #{i}",
            });
        }

        return result;
    }

    /// <summary>设备名拼成的指纹，用来做热插拔检测。</summary>
    public static string DeviceSignature() => string.Join("|", ListDevices().Select(d => d.Name));

    /// <summary>收到消息时触发；在 winmm 回调线程上执行。</summary>
    public event Action<MidiMessage>? MessageReceived;

    private readonly MidiInProc _proc;
    private IntPtr _handle = IntPtr.Zero;

    public MidiInputService()
    {
        // 必须一直持有委托，否则 GC 回收后系统回调会踩空
        _proc = OnMidiIn;
    }

    public bool IsOpen => _handle != IntPtr.Zero;

    public int DeviceIndex { get; private set; } = -1;

    public string DeviceName { get; private set; } = string.Empty;

    public void Open(int deviceIndex)
    {
        Close();

        uint rc = midiInOpen(out _handle, (uint)deviceIndex, _proc, IntPtr.Zero, CallbackFunction);
        if (rc != 0)
        {
            _handle = IntPtr.Zero;
            throw new InvalidOperationException(
                $"打开 MIDI 输入失败（winmm 错误码 {rc}）。常见原因：端口被别的程序独占，或该端口不是输入端口。");
        }

        rc = midiInStart(_handle);
        if (rc != 0)
        {
            midiInClose(_handle);
            _handle = IntPtr.Zero;
            throw new InvalidOperationException($"启动 MIDI 输入失败（winmm 错误码 {rc}）。");
        }

        DeviceIndex = deviceIndex;
        DeviceName = ListDevices().FirstOrDefault(d => d.Index == deviceIndex)?.Name ?? $"#{deviceIndex}";
    }

    public void Close()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            midiInStop(_handle);
            midiInReset(_handle);
            midiInClose(_handle);
        }
        catch
        {
            // 关闭阶段的异常没有意义，忽略
        }
        finally
        {
            _handle = IntPtr.Zero;
            DeviceIndex = -1;
            DeviceName = string.Empty;
        }
    }

    private void OnMidiIn(IntPtr handle, uint message, IntPtr instance, IntPtr param1, IntPtr param2)
    {
        if (message != MimData)
        {
            return;
        }

        try
        {
            long packed = param1.ToInt64();
            byte status = (byte)(packed & 0xFF);
            byte data1 = (byte)((packed >> 8) & 0xFF);
            byte data2 = (byte)((packed >> 16) & 0xFF);

            int command = status & 0xF0;
            MidiMessageKind kind = command switch
            {
                0x90 => data2 > 0 ? MidiMessageKind.NoteOn : MidiMessageKind.NoteOff,
                0x80 => MidiMessageKind.NoteOff,
                0xB0 => MidiMessageKind.ControlChange,
                _ => MidiMessageKind.Other,
            };

            MessageReceived?.Invoke(new MidiMessage
            {
                Kind = kind,
                Channel = status & 0x0F,
                Data1 = data1,
                Data2 = data2,
                Timestamp = Stopwatch.GetTimestamp(),
            });
        }
        catch
        {
            // 回调里绝不能抛异常
        }
    }

    public void Dispose() => Close();
}
