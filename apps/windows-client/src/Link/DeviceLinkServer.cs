using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using System.Text.Json;
using System.Collections.Concurrent;
using LyricRelay.Protocol;

namespace LyricRelay.Windows;

public sealed class DeviceLinkServer : IAsyncDisposable
{
    private readonly X509Certificate2 _certificate;
    private readonly PairingManager _pairing;
    private readonly PairedDeviceStore _pairedDevices;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<TcpClient, string> _clients = new();
    private TcpListener? _listener;
    private Task? _acceptLoop;

    public DeviceLinkServer(X509Certificate2 certificate, PairingManager pairing, PairedDeviceStore pairedDevices)
    {
        _certificate = certificate;
        _pairing = pairing;
        _pairedDevices = pairedDevices;
    }

    public int Port { get; private set; }
    public bool AllowKnownConnections { get; set; } = true;

    public event EventHandler<TrackStateReceivedEventArgs>? TrackStateReceived;
    public event EventHandler<string>? StatusChanged;

    public void RefreshStatus()
    {
        var deviceId = _clients.Values.FirstOrDefault();
        StatusChanged?.Invoke(this, deviceId is null ? "等待 Android" : $"设备已连接：{deviceId}");
    }

    public Task StartAsync(int port = 47250)
    {
        if (_listener is not null) return Task.CompletedTask;
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = AcceptLoopAsync(_shutdown.Token);
        StatusChanged?.Invoke(this, $"监听端口 {Port}");
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serverCancellation)
    {
        using (client)
        {
        await using var network = client.GetStream();
        using var ssl = new SslStream(network, leaveInnerStreamOpen: false);
        try
        {
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = _certificate,
                ClientCertificateRequired = false,
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                                      System.Security.Authentication.SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, serverCancellation);

            using var reader = new StreamReader(ssl);
            await using var writer = new StreamWriter(ssl) { AutoFlush = true, NewLine = "\n" };
            var firstLine = await reader.ReadLineAsync(serverCancellation);
            if (string.IsNullOrWhiteSpace(firstLine)) return;
            var first = JsonSerializer.Deserialize<Envelope<JsonElement>>(firstLine, ProtocolJson.Options);
            if (first is null) return;

            var deviceId = first.DeviceId;
            if (first.Type == MessageTypes.PairingConfirm)
            {
                await HandlePairingAsync(first, writer, serverCancellation);
                deviceId = first.Payload.TryGetProperty("androidDeviceId", out var pairingId)
                    ? pairingId.GetString() ?? string.Empty
                    : string.Empty;
            }
            else if (first.Type == MessageTypes.LinkHello)
            {
                if (!AllowKnownConnections || !IsPaired(deviceId, first.Payload))
                {
                    StatusChanged?.Invoke(this, "拒绝未知 Android 设备");
                    return;
                }

                await WriteAsync(writer, MessageTypes.LinkHello, new { accepted = true }, deviceId, serverCancellation);
            }
            else
            {
                return;
            }

            _clients[client] = deviceId;
            StatusChanged?.Invoke(this, $"设备已连接：{deviceId}");
            while (!serverCancellation.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(serverCancellation);
                if (line is null) break;
                var message = JsonSerializer.Deserialize<Envelope<JsonElement>>(line, ProtocolJson.Options);
                if (message is null || message.Version != 1 || message.DeviceId != deviceId) continue;
                switch (message.Type)
                {
                    case MessageTypes.TrackState:
                        var state = message.Payload.Deserialize<TrackState>(ProtocolJson.Options);
                        if (state is not null && state.IsValid(out _))
                        {
                            TrackStateReceived?.Invoke(this, new TrackStateReceivedEventArgs(state));
                        }
                        break;
                    case MessageTypes.LinkPing:
                        await WriteAsync(writer, MessageTypes.LinkPong, new { }, deviceId, serverCancellation);
                        break;
                    case MessageTypes.TrackCleared:
                        TrackStateReceived?.Invoke(this, new TrackStateReceivedEventArgs(null));
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (serverCancellation.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
            StatusChanged?.Invoke(this, "设备连接已断开");
        }
        catch (AuthenticationException)
        {
            StatusChanged?.Invoke(this, "TLS 认证失败");
        }
        catch (JsonException)
        {
            StatusChanged?.Invoke(this, "收到无效协议消息");
        }
        }
        _clients.TryRemove(client, out _);
    }

    private async Task HandlePairingAsync(
        Envelope<JsonElement> message,
        StreamWriter writer,
        CancellationToken cancellationToken)
    {
        if (!message.Payload.TryGetProperty("token", out var tokenElement) ||
            !message.Payload.TryGetProperty("androidDeviceId", out var deviceElement))
        {
            throw new InvalidDataException("pairing.confirm payload is incomplete");
        }

        var token = tokenElement.GetString() ?? string.Empty;
        var deviceId = deviceElement.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(deviceId) || !_pairing.TryConsume(token))
        {
            StatusChanged?.Invoke(this, "拒绝无效或过期的配对请求");
            throw new AuthenticationException("invalid pairing token");
        }

        var deviceKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        _pairedDevices.Save(new PairedDevice(deviceId, deviceKey, DateTimeOffset.UtcNow));
        await WriteAsync(writer, MessageTypes.PairingAccept, new
        {
            androidDeviceId = deviceId,
            windowsDeviceId = _pairing.DeviceId,
            deviceKey
        }, deviceId, cancellationToken);
    }

    private bool IsPaired(string deviceId, JsonElement payload)
    {
        var paired = _pairedDevices.Load();
        var submittedKey = payload.TryGetProperty("deviceKey", out var keyElement)
            ? keyElement.GetString()
            : null;
        return paired is not null &&
               paired.DeviceId == deviceId &&
               !string.IsNullOrWhiteSpace(submittedKey) &&
               CryptographicOperations.FixedTimeEquals(
                   System.Text.Encoding.UTF8.GetBytes(paired.DeviceKey),
                   System.Text.Encoding.UTF8.GetBytes(submittedKey));
    }

    private static async Task WriteAsync(
        StreamWriter writer,
        string type,
        object payload,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var envelope = new Envelope<object>(1, type, Guid.NewGuid().ToString("N"), deviceId, DateTimeOffset.UtcNow, payload);
        await writer.WriteLineAsync(JsonSerializer.Serialize(envelope, ProtocolJson.Options).AsMemory(), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _listener?.Stop();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; } catch (OperationCanceledException) { }
        }

        _shutdown.Dispose();
    }
}

public sealed class TrackStateReceivedEventArgs : EventArgs
{
    public TrackStateReceivedEventArgs(TrackState? state) => State = state;
    public TrackState? State { get; }
}
