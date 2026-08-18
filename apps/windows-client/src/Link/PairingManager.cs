using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using QRCoder;

namespace LyricRelay.Windows;

public sealed record PairingPayload(
    string Host,
    int Port,
    string Token,
    string CertificateSha256,
    string WindowsDeviceId,
    DateTimeOffset ExpiresAt)
{
    public string Encode()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

public sealed class PairingManager
{
    private readonly string _deviceId;
    private readonly X509Certificate2 _certificate;
    private PairingPayload? _pending;

    public PairingManager(string deviceId, X509Certificate2 certificate)
    {
        _deviceId = deviceId;
        _certificate = certificate;
    }

    public string DeviceId => _deviceId;

    public PairingPayload Create(int port)
    {
        var host = GetPreferredLocalAddress()?.ToString() ?? "127.0.0.1";
        _pending = new PairingPayload(
            host,
            port,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            CertificateFingerprint(_certificate),
            _deviceId,
            DateTimeOffset.UtcNow.AddMinutes(2));
        return _pending;
    }

    public bool TryConsume(string token)
    {
        if (_pending is null || _pending.ExpiresAt <= DateTimeOffset.UtcNow ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(_pending.Token),
                Encoding.UTF8.GetBytes(token)))
        {
            return false;
        }

        _pending = null;
        return true;
    }

    public bool IsPending(string token) => _pending?.Token == token && _pending.ExpiresAt > DateTimeOffset.UtcNow;

    public static byte[] ToPng(PairingPayload payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload.Encode(), QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data);
        return png.GetGraphic(8);
    }

    public static string CertificateFingerprint(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();

    private static IPAddress? GetPreferredLocalAddress()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses
                .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => new
                {
                    Network = network,
                    Address = address.Address,
                    IsPrivate = IsPrivateIpv4(address.Address)
                }))
            .Where(candidate => !IPAddress.IsLoopback(candidate.Address) &&
                                !candidate.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .OrderByDescending(candidate => candidate.IsPrivate)
            .ThenByDescending(candidate => candidate.Network.NetworkInterfaceType is
                NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet)
            .ThenBy(candidate => candidate.Network.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            .ToList();

        return candidates.FirstOrDefault()?.Address;
    }

    private static bool IsPrivateIpv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
               (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168);
    }
}
