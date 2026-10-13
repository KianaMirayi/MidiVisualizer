using Avalonia;
using MidiStaffVisualizer.Core.Diagnostics;
using MidiStaffVisualizer.Core.Midi;
using MidiStaffVisualizer.Core.Music;

namespace MidiStaffVisualizer.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 设备自检：dotnet run --project src/MidiStaffVisualizer.App -- --list-devices
        // 用来确认 MIDI 输入/输出端口（loopMIDI）和虚拟声卡是否被系统认出来。
        if (args.Contains("--list-devices"))
        {
            PrintDeviceReport();
            return;
        }

        // 自检渲染：dotnet run --project src/MidiStaffVisualizer.App -- --render-preview out.png
        int previewIndex = Array.IndexOf(args, "--render-preview");
        if (previewIndex >= 0)
        {
            string outputPath = previewIndex + 1 < args.Length
                ? args[previewIndex + 1]
                : Path.Combine(AppContext.BaseDirectory, "staff-preview.png");
            PreviewRenderer.RenderToFile(outputPath);
            Console.WriteLine($"已生成预览：{outputPath}");
            return;
        }

        // 逻辑自检（不需要 MIDI 设备）：dotnet run --project src/MidiStaffVisualizer.App -- --selftest
        if (args.Contains("--selftest"))
        {
            RunSelfTest();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void RunSelfTest()
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch
        {
            // 忽略
        }

        int passed = 0;
        int failed = 0;

        void Check(string name, bool ok)
        {
            Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + name);
            if (ok)
            {
                passed++;
            }
            else
            {
                failed++;
            }
        }

        Console.WriteLine("== 五线谱横向排版逻辑自检 ==");

        var staff = new LiveStaff();

        // 1) 几乎同时按下 → 同一列纵向叠起来
        staff.NoteOn(60, 0.000);
        staff.NoteOn(64, 0.010);
        staff.NoteOn(67, 0.020);
        Check("柱式和弦并进同一列（3 个音 1 列）",
            staff.Columns.Count == 1 && staff.Columns[0].Count == 3);

        // 2) note-off 立刻取消显示
        staff.NoteOff(64);
        Check("note-off 立刻取消该音显示",
            staff.Columns[0].Count == 2 && !staff.Columns[0].Contains(64));

        // 3) 前一个音还按着、但间隔超过时间窗 → 另起一列（连奏不会被并成和弦）
        staff.NoteOn(72, 0.500);
        Check("连奏（按住上一个音 480ms 后再弹）另起一列",
            staff.Columns.Count == 2 && staff.Columns[1].Count == 1 && staff.Columns[1][0] == 72);

        // 4) 全部放开后谱面清空 → 下一次弹奏从最左边重排
        staff.NoteOff(60);
        staff.NoteOff(67);
        staff.NoteOff(72);
        staff.NoteOn(55, 9.0);
        Check("谱面空了以后重新从最左列开始",
            staff.Columns.Count == 1 && staff.Columns[0].Count == 1 && staff.Columns[0][0] == 55);

        // 5) 同音重触发不会留下两个符头
        staff.NoteOn(55, 9.1);
        Check("同音重复触发不会叠出两个符头", staff.Columns.Count == 1 && staff.Columns[0].Count == 1);

        // 7) 和弦识别（用户参考视频里那种写法）
        var chord = ChordDetector.Detect([55, 57], preferFlats: false);
        Check($"sus2(no5) 风格命名：得到 \"{chord.Display}\"",
            chord.Root == "G" && chord.Quality == "sus2(no5)");

        var cMajor = ChordDetector.Detect([60, 64, 67, 71], preferFlats: false);
        Check($"大七和弦命名：得到 \"{cMajor.Display}\"", cMajor.Display == "C maj7");

        Console.WriteLine($"== 结果：{passed} 通过 / {failed} 失败 ==");
        Environment.Exit(failed == 0 ? 0 : 1);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void PrintDeviceReport()
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch
        {
            // 某些终端不支持切编码，忽略
        }

        Console.WriteLine("==== MIDI 输入端口（键盘接哪个就选哪个）====");
        var inputs = MidiInputService.ListDevices();
        if (inputs.Count == 0)
        {
            Console.WriteLine("  （无）—— 检查键盘 USB 连接，或先打开键盘电源");
        }

        foreach (var device in inputs)
        {
            Console.WriteLine($"  [{device.Index}] {device.Name}");
        }

        Console.WriteLine();
        Console.WriteLine("==== MIDI 输出端口（直通目标：loopMIDI 端口 / Kontakt 的输入）====");
        var outputs = MidiOutputService.ListDevices();
        if (outputs.Count == 0)
        {
            Console.WriteLine("  （无）—— 需要安装 loopMIDI 之类的虚拟 MIDI 端口");
        }

        foreach (var device in outputs)
        {
            Console.WriteLine($"  [{device.Index}] {device.Name}");
        }

        Console.WriteLine();
        Console.WriteLine("==== 音频端点（虚拟声卡会标出来）====");
        Console.WriteLine($"  [播放端点 {AudioEndpointProbe.RenderDeviceCount} 个 / 录音端点 {AudioEndpointProbe.CaptureDeviceCount} 个]");
        Console.WriteLine($"  [diag] {AudioEndpointProbe.DiagnoseRenderCaps()}");
        foreach (var endpoint in AudioEndpointProbe.ListAll())
        {
            Console.WriteLine($"  {endpoint.Display}");
        }

        Console.WriteLine();
        var cables = AudioEndpointProbe.ListAll().Where(e => e.LooksLikeVirtualCable).ToArray();
        Console.WriteLine(cables.Length == 0
            ? "提示：没发现虚拟声卡。要让对方听到 Kontakt 7，需要装 VB-CABLE 或 VoiceMeeter。"
            : "提示：发现虚拟声卡 —— " + string.Join("、", cables.Select(c => c.Name)));
    }
}
