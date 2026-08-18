using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LyricRelay.Windows;

public sealed class DiscoveryResponder : IAsyncDisposable
{
    private const int DiscoveryPort = 47251;
    private readonly string _deviceId;
    private readonly int _tcpPort;
    private readonly string _certificateFingerprint;
    private readonly CancellationTokenSource _shutdown = new();
    private UdpClient? _udp;
    private Task? _loop;

    public DiscoveryResponder(string deviceId, int tcpPort, string certificateFingerprint)
    {
        _deviceId = deviceId;
        _tcpPort = tcpPort;
        _certificateFingerprint = certificateFingerprint;
    }

    public Task StartAsync()
    {
        _udp = new UdpClient(DiscoveryPort) { EnableBroadcast = true };
        _loop = LoopAsync(_shutdown.Token);
        return Task.CompletedTask;
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await _udp!.ReceiveAsync(cancellationToken);
                var request = Encoding.UTF8.GetString(result.Buffer);
                if (request != "LYRICRELAY_DISCOVER") continue;
                var response = JsonSerializer.Serialize(new
                {
                    protocol = 1,
                    deviceId = _deviceId,
                    port = _tcpPort,
                    certificateSha256 = _certificateFingerprint
                });
                var bytes = Encoding.UTF8.GetBytes(response);
                await _udp.SendAsync(bytes, bytes.Length, result.RemoteEndPoint);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException)
            {
                if (!cancellationToken.IsCancellationRequested) await Task.Delay(1000, cancellationToken);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _udp?.Dispose();
        return ValueTask.CompletedTask;
    }
}

