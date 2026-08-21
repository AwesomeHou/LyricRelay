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
    private System.Drawing.Icon? _trayIconResource;
    private AppSettings _settings = new();
    private DeviceLinkServer? _linkServer;
    private PairingManager? _pairing;
    private DiscoveryResponder? _discovery;
    private TaskbarRenderer? _renderer;
    private TimelineEngine? _timeline;
    private LyricsTimeline? _lyrics;
    private LyricsCoordinator? _lyricsCoordinator;
    private CancellationTokenSource? _lyricsCancellation;
    private CancellationTokenSource? _clearCancellation;
    private string? _currentTrackId;
    private string? _currentTrackContext;
    private TrackState? _lastDiagnosticState;
    private string? _lastRenderDiagnosticKey;
    private long _nextRenderDiagnosticAt;

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
        DiagnosticLog.Info("startup", $"pid={Environment.ProcessId} diagnostics={DiagnosticLog.FilePath} showLyrics={_settings.ShowLyrics} offset={_settings.OffsetMs}");
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
            DiagnosticLog.Info("state", "received=cleared; starting clear grace timer");
            _clearCancellation?.Cancel();
            _clearCancellation?.Dispose();
            _clearCancellation = new CancellationTokenSource();
            _ = ClearAfterGraceAsync(_clearCancellation.Token);
            return;
        }

        _clearCancellation?.Cancel();
        _clearCancellation?.Dispose();
        _clearCancellation = null;

        if (_timeline is null || _lyricsCoordinator is null) return;
        var previousDiagnosticState = _lastDiagnosticState;
        _lastDiagnosticState = state;
        var timelineVersionBefore = _timeline.StateVersion;
        _timeline.Apply(state);
        DiagnosticLog.Info(
            "state",
            $"received {DiagnosticLog.StateSummary(state)} changes={DiagnosticLog.StateChanges(previousDiagnosticState, state)} " +
            $"timelineVersionBefore={timelineVersionBefore?.ToString() ?? "-"} timelineVersionAfter={_timeline.StateVersion?.ToString() ?? "-"}");
        var trackContext = TrackContext(state);
        // Android may derive a new TrackId when MediaSession metadata is
        // corrected. The metadata context is the stable identity for lyric
        // loading; position/state updates still reach TimelineEngine above.
        var sameTrackContext = _currentTrackContext == trackContext;
        DiagnosticLog.Info(
            "context",
            $"track={DiagnosticLog.Hash(state.TrackId)} context={DiagnosticLog.Hash(trackContext)} same={sameTrackContext} " +
            $"currentTrack={DiagnosticLog.Hash(_currentTrackId)} currentContext={DiagnosticLog.Hash(_currentTrackContext)}");
        if (sameTrackContext)
        {
            RenderCurrentLine();
            return;
        }

        _currentTrackId = state.TrackId;
        _currentTrackContext = trackContext;
        _lyrics = null;
        DiagnosticLog.Info("lyrics", $"cleared-before-load track={DiagnosticLog.Hash(state.TrackId)} reason=track-or-context-changed");
        _lyricsCancellation?.Cancel();
        _lyricsCancellation?.Dispose();
        _lyricsCancellation = new CancellationTokenSource();
        var cancellationToken = _lyricsCancellation.Token;
        var loadStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        DiagnosticLog.Info("lyrics", $"load-start track={DiagnosticLog.Hash(state.TrackId)} context={DiagnosticLog.Hash(trackContext)}");
        try
        {
            var result = await _lyricsCoordinator.LoadAsync(state, cancellationToken);
            var loadElapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(loadStartedAt).TotalMilliseconds;
            DiagnosticLog.Info(
                "lyrics",
                $"load-end track={DiagnosticLog.Hash(state.TrackId)} context={DiagnosticLog.Hash(trackContext)} elapsedMs={loadElapsedMs:0} " +
                $"success={result.IsSuccess} source={result.Source ?? "-"} failure={result.Failure} lines={result.Timeline?.Lines.Count ?? 0} " +
                $"cancelled={cancellationToken.IsCancellationRequested}");
            if (cancellationToken.IsCancellationRequested || _currentTrackContext != trackContext)
            {
                DiagnosticLog.Info("lyrics", $"load-discarded track={DiagnosticLog.Hash(state.TrackId)} reason=stale-request");
                return;
            }
            _lyrics = result.Timeline;
            _window.SetLyrics(result.IsSuccess ? result.Source ?? "同步歌词" : $"无同步歌词（{result.Failure}）");
            RenderCurrentLine();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DiagnosticLog.Info("lyrics", $"load-cancelled track={DiagnosticLog.Hash(state.TrackId)}");
        }
        catch (Exception)
        {
            DiagnosticLog.Info("lyrics", $"load-exception track={DiagnosticLog.Hash(state.TrackId)}");
            if (!cancellationToken.IsCancellationRequested)
            {
                _window.SetLyrics("歌词加载失败");
            }
        }
    }

    private async Task ClearAfterGraceAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Android keeps the last valid state for four seconds while a
            // player refreshes its MediaSession. Keep a small extra margin
            // here so the Windows renderer does not flicker in that window.
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested) return;
        DiagnosticLog.Info("state", "clear grace expired; clearing lyrics and renderer");
        _currentTrackId = null;
        _currentTrackContext = null;
        _lastDiagnosticState = null;
        _lyrics = null;
        _lyricsCancellation?.Cancel();
        _renderer?.Hide();
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
        return TrackIdentity.LyricsContext(state);
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
            var missingKey = $"missing:renderer={_renderer is not null};timeline={_timeline is not null};lyrics={_lyrics is not null}";
            LogRenderDiagnostic(missingKey, $"hide reason={missingKey}");
            _renderer?.Hide();
            return;
        }

        var current = _timeline.GetCurrentLine(_lyrics, _settings.OffsetMs);
        var position = _timeline.GetPositionMs();
        var renderKey = $"line={DiagnosticLog.LineSummary(current)}";
        LogRenderDiagnostic(
            renderKey,
            $"render position={position?.ToString() ?? "-"} offset={_settings.OffsetMs} {renderKey}");
        _renderer.Render(current, _settings);
    }

    private void LogRenderDiagnostic(string key, string message)
    {
        var now = Environment.TickCount64;
        if (key == _lastRenderDiagnosticKey && now < _nextRenderDiagnosticAt) return;
        _lastRenderDiagnosticKey = key;
        _nextRenderDiagnosticAt = now + 1000;
        DiagnosticLog.Info("render", message);
    }

    private void SaveSettingsFromWindow()
    {
        _settings.ShowLyrics = _window.IsLyricsEnabled;
        _settings.StartWithWindows = _window.IsStartWithWindowsEnabled;
        _settings.AutoConnect = _window.IsAutoConnectEnabled;
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
        _trayIconResource = Environment.ProcessPath is { } processPath
            ? System.Drawing.Icon.ExtractAssociatedIcon(processPath)
            : null;
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _trayIconResource ?? System.Drawing.SystemIcons.Application,
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
        _clearCancellation?.Cancel();
        _lyricsCancellation?.Cancel();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _trayIconResource?.Dispose();
        _renderer?.Dispose();
        if (_lyricsCoordinator is not null) await _lyricsCoordinator.DisposeAsync();
        if (_discovery is not null) await _discovery.DisposeAsync();
        if (_linkServer is not null) await _linkServer.DisposeAsync();
    }
}
