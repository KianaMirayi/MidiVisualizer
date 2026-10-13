using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MidiStaffVisualizer.App.ViewModels;
using MidiStaffVisualizer.App.Views;

namespace MidiStaffVisualizer.App;

public partial class App : Application
{
    private MainViewModel? _viewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _viewModel = new MainViewModel();
            var window = new MainWindow { DataContext = _viewModel };
            desktop.MainWindow = window;

            // 退出时先断开端口并复位，避免 Kontakt 7 里留下卡住的长音
            desktop.ShutdownRequested += (_, _) => _viewModel.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
