using System.Windows.Threading;
using System.Net.Http;
using LyricRelay.Core;
using LyricRelay.Protocol;

namespace LyricRelay.Windows;

public sealed class AppController : IAsyncDisposable
{
    private readonly MainWindow _window;
    private readonly AppSettingsStore _settingsStore = new();
    private readonly StartupManager _startupManager = new();
    private readonly IdentityCertificateStore _identityStore = new();
    private readonly PairedDeviceStore _pairedDeviceStore = new();
    private readonly DispatcherTimer _renderTimer;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private AppSettings _settings = new();
    private DeviceLinkServer? _linkServer;
    private PairingManager? _pairing;
    private DiscoveryResponder? _discovery;
    private TaskbarRenderer? _renderer;
    private TimelineEngine? _timeline;
    private LyricsTimeline? _lyrics;
    private LyricsCoordinator? _lyricsCoordinator;
    private CancellationTokenSource? _lyricsCancellation;
    private string? _currentTrackId;
    private string? _currentTrackContext;

    public AppController(MainWindow window)
    {
        _window = window;
        _renderTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _renderTimer.Tick += (_, _) => RenderCurrentLine();
        _window.SettingsChanged += (_, _) => SaveSettingsFromWindow();
        _window.PairingRefreshRequested += (_, _) => RefreshPairing();
        _window.ConnectionRefreshRequested += (_, _) => RefreshConnectionStatus();
    }

    public async Task StartAsync()
    {
        _settings = _settingsStore.Load();
        if (string.IsNullOrWhiteSpace(_settings.DeviceId))
        {
            _settings.DeviceId = Guid.NewGuid().ToString("N");
            _settingsStore.Save(_settings);
        }

        _window.ApplySettings(_settings);
        _startupManager.Apply(_settings.StartWithWindows);
        CreateTrayIcon();
        _renderer = new TaskbarRenderer();
        _timeline = new TimelineEngine(new StopwatchClock());
        _lyricsCoordinator = new LyricsCoordinator(new ILyricsProvider[]
        {
            new QqMusicProvider(new HttpClient { Timeout = TimeSpan.FromSeconds(8) }),
            new NetEaseProvider(new HttpClient { Timeout = TimeSpan.FromSeconds(8) }),
            new KuGouProvider(new HttpClient { Timeout = TimeSpan.FromSeconds(8) }),
            new LrclibProvider(new HttpClient { Timeout = TimeSpan.FromSeconds(8) })
        });

        var certificate = _identityStore.GetOrCreate(_settings.DeviceId);
        _pairing = new PairingManager(_settings.DeviceId, certificate);
        _linkServer = new DeviceLinkServer(certificate, _pairing, _pairedDeviceStore);
        _linkServer.AllowKnownConnections = _settings.AutoConnect;
        _linkServer.StatusChanged += (_, status) => DispatchConnectionStatus(status);
        _linkServer.TrackStateReceived += (_, args) => DispatchTrackState(args.State);
        await _linkServer.StartAsync();

        RefreshPairing();
        _window.SetLyrics("QQ 音乐 / 网易云 / 酷狗 / LRCLIB");

        _discovery = new DiscoveryResponder(_settings.DeviceId, _linkServer.Port, PairingManager.CertificateFingerprint(certificate));
        if (_settings.AutoConnect)
        {
            await _discovery.StartAsync();
        }
        _renderTimer.Start();
        _linkServer.RefreshStatus();
        if (Environment.GetCommandLineArgs().Any(argument => argument.Equals("--background", StringComparison.OrdinalIgnoreCase)))
        {
            _window.Hide();
        }
    }

    private async Task HandleTrackStateAsync(TrackState? state)
    {
        if (state is null)
        {
            _currentTrackId = null;
            _currentTrackContext = null;
            _lyrics = null;
            _lyricsCancellation?.Cancel();
            _renderer?.Hide();
            return;
        }

        if (_timeline is null || _lyricsCoordinator is null) return;
        _timeline.Apply(state);
        var trackContext = TrackContext(state);
        if (_currentTrackId == state.TrackId && _currentTrackContext == trackContext)
        {
            RenderCurrentLine();
            return;
        }

        _currentTrackId = state.TrackId;
        _currentTrackContext = trackContext;
        _lyrics = null;
        _lyricsCancellation?.Cancel();
        _lyricsCancellation?.Dispose();
        _lyricsCancellation = new CancellationTokenSource();
        var cancellationToken = _lyricsCancellation.Token;
        try
        {
            var result = await _lyricsCoordinator.LoadAsync(state, cancellationToken);
            if (cancellationToken.IsCancellationRequested || _currentTrackId != state.TrackId || _currentTrackContext != trackContext) return;
            _lyrics = result.Timeline;
            _window.SetLyrics(result.IsSuccess ? result.Source ?? "同步歌词" : $"无同步歌词（{result.Failure}）");
            RenderCurrentLine();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _window.SetLyrics("歌词加载失败");
            }
        }
    }

    private void DispatchConnectionStatus(string status)
    {
        try
        {
            if (_window.Dispatcher.HasShutdownStarted || _window.Dispatcher.HasShutdownFinished) return;
            _window.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => _window.SetConnection(status)));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void DispatchTrackState(TrackState? state)
    {
        try
        {
            if (_window.Dispatcher.HasShutdownStarted || _window.Dispatcher.HasShutdownFinished) return;
            _window.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => _ = HandleTrackStateAsync(state)));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string TrackContext(TrackState state)
    {
        // QQ Music can expose the changing lyric fragment as TITLE. Android's
        // QQ adapter derives TrackId from the normalized metadata, so the
        // title must not make an otherwise unchanged request look like a new
        // song on every media-session update.
        var metadata = $"{state.PackageName}|{state.Artist}|{state.Album}|{state.DurationMs}";
        return string.Equals(state.PackageName, "com.tencent.qqmusic", StringComparison.OrdinalIgnoreCase)
            ? metadata
            : $"{metadata}|{state.Title}";
    }

    private void RefreshPairing()
    {
        if (_pairing is null || _linkServer is null || _linkServer.Port == 0) return;

        var payload = _pairing.Create(_linkServer.Port);
        _window.SetPairing(
            $"请使用 Android Companion 扫描此二维码。二维码有效期至 {payload.ExpiresAt.ToLocalTime():HH:mm:ss}。",
            PairingManager.ToPng(payload));
    }

    private void RefreshConnectionStatus()
    {
        if (_linkServer is null)
        {
            _window.SetConnection("尚未启动");
            return;
        }

        _linkServer.RefreshStatus();
    }

    private void RenderCurrentLine()
    {
        if (_renderer is null || _timeline is null || _lyrics is null)
        {
            _renderer?.Hide();
            return;
        }

        var current = _timeline.GetCurrentLine(_lyrics, _settings.OffsetMs);
        var next = current is null ? null : _lyrics.Lines.FirstOrDefault(line => line.StartMs > current.StartMs);
        _renderer.Render(current, next, _settings);
    }

    private void SaveSettingsFromWindow()
    {
        _settings.ShowLyrics = _window.IsLyricsEnabled;
        _settings.StartWithWindows = _window.IsStartWithWindowsEnabled;
        _settings.AutoConnect = _window.IsAutoConnectEnabled;
        _settings.DoubleLine = _window.IsDoubleLineEnabled;
        _settings.FontSize = _window.SelectedFontSize;
        _settings.OffsetMs = _window.SelectedOffsetMs;
        _settings.Alignment = _window.SelectedAlignment;
        _settings.FontWeightValue = _window.SelectedFontWeight;
        _settings.Color = _window.SelectedColor;
        _settings.FontFamily = _window.SelectedFontFamily;
        _startupManager.Apply(_settings.StartWithWindows);
        if (_linkServer is not null)
        {
            _linkServer.AllowKnownConnections = _settings.AutoConnect;
        }
        _settingsStore.Save(_settings);
        RenderCurrentLine();
    }

    private void CreateTrayIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开设置", null, (_, _) =>
        {
            _window.Show();
            _window.WindowState = System.Windows.WindowState.Normal;
            _window.Activate();
        });
        menu.Items.Add("重启客户端", null, (_, _) => RestartClient());
        menu.Items.Add("退出", null, (_, _) => System.Windows.Application.Current.Shutdown());
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "LyricRelay",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) =>
        {
            _window.Show();
            _window.WindowState = System.Windows.WindowState.Normal;
            _window.Activate();
        };
    }

    private static void RestartClient()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--restart",
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true
            });
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show($"无法重启 LyricRelay：{exception.Message}", "LyricRelay", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _renderTimer.Stop();
        _lyricsCancellation?.Cancel();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _renderer?.Dispose();
        if (_lyricsCoordinator is not null) await _lyricsCoordinator.DisposeAsync();
        if (_discovery is not null) await _discovery.DisposeAsync();
        if (_linkServer is not null) await _linkServer.DisposeAsync();
    }
}
