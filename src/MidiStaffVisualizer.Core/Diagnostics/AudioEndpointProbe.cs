using System.Runtime.InteropServices;

namespace MidiStaffVisualizer.Core.Diagnostics;

/// <summary>一个音频端点（只做检测/提示，本软件不参与音频处理）。</summary>
public sealed record AudioEndpointInfo(string Name, string Kind, bool LooksLikeVirtualCable)
{
    /// <summary>Focusrite 声卡暴露出来的 Loopback 通道 —— 可以直接当聊天软件的麦克风用。</summary>
    public bool IsHardwareLoopback =>
        Name.Contains("Loopback", StringComparison.OrdinalIgnoreCase)
        && Name.Contains("Focusrite", StringComparison.OrdinalIgnoreCase);

    public string Display
    {
        get
        {
            string tag = IsHardwareLoopback
                ? "  ← 可直接当聊天麦克风（声卡 Loopback）"
                : LooksLikeVirtualCable ? "  ← 虚拟线缆" : string.Empty;
            return $"[{Kind}] {Name}{tag}";
        }
    }

    public override string ToString() => Display;
}

/// <summary>
/// 用 winmm 的 waveOut / waveIn 枚举音频端点。
/// 目的只有一个：告诉用户系统里有没有装虚拟声卡（VB-CABLE / VoiceMeeter 等），
/// 从而把 Kontakt 7 的声音送进语音聊天的麦克风轨。本软件不播放、不采集任何音频。
/// </summary>
public static class AudioEndpointProbe
{
    [DllImport("winmm.dll")] private static extern uint waveOutGetNumDevs();

    [DllImport("winmm.dll")] private static extern uint waveInGetNumDevs();

    // WAVEOUTCAPSW / WAVEINCAPSW 都是固定 80 字节：2+2+4 + 32*2(设备名) + 2+2+4，设备名从偏移 8 开始。
    // 这里用原始指针手动封送，避免 ref 结构体封送差异（实测 ref 版本会返回 MMSYSERR_INVALPARAM=11）。
    [DllImport("winmm.dll", EntryPoint = "waveOutGetDevCapsW", CharSet = CharSet.Unicode)]
    private static extern uint waveOutGetDevCaps(UIntPtr deviceId, IntPtr caps, uint size);

    [DllImport("winmm.dll", EntryPoint = "waveInGetDevCapsW", CharSet = CharSet.Unicode)]
    private static extern uint waveInGetDevCaps(UIntPtr deviceId, IntPtr caps, uint size);

    private const int WaveCapsSize = 80;
    private const int WaveCapsNameOffset = 8;
    private const int WaveCapsNameChars = 32;

    private static readonly string[] VirtualCableHints =
        ["cable", "voicemeeter", "vb-audio", "vbaudio", "soundwire"];

    /// <summary>winmm 报出来的播放端点数量（诊断用）。</summary>
    public static int RenderDeviceCount => (int)waveOutGetNumDevs();

    /// <summary>winmm 报出来的录音端点数量（诊断用）。</summary>
    public static int CaptureDeviceCount => (int)waveInGetNumDevs();

    public static bool LooksVirtual(string name)
    {
        string lower = name.ToLowerInvariant();
        return VirtualCableHints.Any(hint => lower.Contains(hint, StringComparison.Ordinal));
    }

    private static List<string> Enumerate(
        uint count,
        Func<UIntPtr, IntPtr, uint, uint> getCaps,
        string fallbackPrefix)
    {
        var names = new List<string>();

        for (uint i = 0; i < count; i++)
        {
            IntPtr buffer = Marshal.AllocHGlobal(WaveCapsSize);
            try
            {
                for (int offset = 0; offset < WaveCapsSize; offset++)
                {
                    Marshal.WriteByte(buffer, offset, 0);
                }

                uint rc = getCaps(new UIntPtr(i), buffer, WaveCapsSize);
                if (rc != 0)
                {
                    // 读不到就跳过（个别系统上 waveOut 枚举会整体失效，录音端仍然可用）
                    continue;
                }

                string? name = Marshal.PtrToStringUni(buffer + WaveCapsNameOffset, WaveCapsNameChars);
                names.Add(string.IsNullOrWhiteSpace(name) ? $"{fallbackPrefix} #{i}" : name.TrimEnd('\0').Trim());
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return names;
    }

    /// <summary>枚举播放端点（Kontakt 7 的声音从这里出去）。</summary>
    public static List<AudioEndpointInfo> ListRenderEndpoints()
    {
        var list = new List<AudioEndpointInfo>();
        foreach (string name in Enumerate(waveOutGetNumDevs(), waveOutGetDevCaps, "播放设备"))
        {
            list.Add(new AudioEndpointInfo(name, "播放", LooksVirtual(name)));
        }

        return list;
    }

    /// <summary>枚举录音端点（语音聊天把哪一个当"麦克风"，这里就能看到）。</summary>
    public static List<AudioEndpointInfo> ListCaptureEndpoints()
    {
        var list = new List<AudioEndpointInfo>();
        foreach (string name in Enumerate(waveInGetNumDevs(), waveInGetDevCaps, "录音设备"))
        {
            list.Add(new AudioEndpointInfo(name, "录音", LooksVirtual(name)));
        }

        return list;
    }

    public static List<AudioEndpointInfo> ListAll()
    {
        var list = new List<AudioEndpointInfo>();
        list.AddRange(ListRenderEndpoints());
        list.AddRange(ListCaptureEndpoints());
        return list;
    }

    /// <summary>原始探测结果，用于排查 waveOut 枚举异常（诊断用）。</summary>
    public static string DiagnoseRenderCaps()
    {
        uint count = waveOutGetNumDevs();
        var detail = new List<string>();

        for (uint i = 0; i < count; i++)
        {
            IntPtr buffer = Marshal.AllocHGlobal(WaveCapsSize);
            try
            {
                for (int offset = 0; offset < WaveCapsSize; offset++)
                {
                    Marshal.WriteByte(buffer, offset, 0);
                }

                uint rc = waveOutGetDevCaps(new UIntPtr(i), buffer, WaveCapsSize);
                string? name = rc == 0
                    ? Marshal.PtrToStringUni(buffer + WaveCapsNameOffset, WaveCapsNameChars)
                    : null;
                detail.Add(rc == 0 ? $"[{i}] '{(name ?? string.Empty).TrimEnd('\0')}'" : $"[{i}] rc={rc}");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return $"count={count} {string.Join(" ", detail)}";
    }
}
