using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MidiStaffVisualizer.App.Controls;

namespace MidiStaffVisualizer.App;

/// <summary>
/// 自检渲染：把五线谱控件直接画成 PNG，方便在没有窗口/没有 MIDI 设备时核对排版。
/// 用法：dotnet run --project src/MidiStaffVisualizer.App -- --render-preview out.png
/// </summary>
internal static class PreviewRenderer
{
    public static void RenderToFile(string outputPath)
    {
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .SetupWithoutStarting();

        const double width = 1120;
        const double height = 330;

        // 模拟一段连奏：单音依次向右排，遇到和弦就在同一列纵向叠起来。
        // 第一列刻意放带升降号的音，用来验证升降号不会压到谱号上。
        int[][] sampleColumns =
        [
            [44, 61],           // 第一列就带升号（G#2 + C#4）
            [64],
            [67],
            [58, 63, 68],       // 柱式和弦（含变化音）
            [72],
            [76, 79],
            [84],               // 上加线
        ];

        var staff = new StaffControl
        {
            Columns = sampleColumns,
            PreferFlats = false,
            ShowNoteNames = true,
        };

        var host = new Border
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(Color.Parse("#0B0C0F")),
            Padding = new Thickness(14),
            Child = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#14161A")),
                CornerRadius = new CornerRadius(12),
                Child = staff,
            },
        };

        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));

        using var bitmap = new RenderTargetBitmap(new PixelSize((int)width, (int)height), new Vector(96, 96));
        bitmap.Render(host);

        string fullPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

#pragma warning disable CS0618 // Bitmap.Save(string) 在 Avalonia 12 里标记过时，但仍是对外最稳的写法
        bitmap.Save(fullPath);
#pragma warning restore CS0618

        // 生成完就退出，避免 Avalonia 的后台线程把进程吊住
        Environment.Exit(0);
    }
}
