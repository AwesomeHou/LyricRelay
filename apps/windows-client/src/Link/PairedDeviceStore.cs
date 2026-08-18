using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;

namespace LyricRelay.Windows;

public sealed record PairedDevice(string DeviceId, string DeviceKey, DateTimeOffset PairedAt);

public sealed class PairedDeviceStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LyricRelay",
        "paired-device.bin");

    public PairedDevice? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var protectedBytes = File.ReadAllBytes(_path);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<PairedDevice>(bytes);
        }
        catch
        {
            return null;
        }
    }

    public void Save(PairedDevice device)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(device));
        File.WriteAllBytes(_path, ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
    }

    public void Clear()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
