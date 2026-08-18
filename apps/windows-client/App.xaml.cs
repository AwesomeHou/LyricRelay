using System.Windows;
using LyricRelay.Core;

namespace LyricRelay.Windows;

public partial class App : System.Windows.Application
{
    private AppController? _controller;
    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(false, "LyricRelay.Windows.SingleInstance");
        var restartRequested = e.Args.Any(argument => argument.Equals("--restart", StringComparison.OrdinalIgnoreCase));
        try
        {
            _ownsInstanceMutex = restartRequested
                ? _instanceMutex.WaitOne(TimeSpan.FromSeconds(10))
                : _instanceMutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _ownsInstanceMutex = true;
        }

        if (!_ownsInstanceMutex)
        {
            System.Windows.MessageBox.Show("LyricRelay 已经在运行中。", "LyricRelay", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        _controller = new AppController(window);
        try
        {
            await _controller.StartAsync();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show($"LyricRelay 启动失败：{exception.Message}", "LyricRelay", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_controller is not null)
        {
            await _controller.DisposeAsync();
        }

        base.OnExit(e);
        if (_ownsInstanceMutex)
        {
            _instanceMutex?.ReleaseMutex();
        }
        _instanceMutex?.Dispose();
    }
}
