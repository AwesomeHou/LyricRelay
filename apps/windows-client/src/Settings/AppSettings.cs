using System.Text.Json;
using System.IO;

namespace LyricRelay.Windows;

public sealed class AppSettings
{
    public string DeviceId { get; set; } = string.Empty;
    public bool StartWithWindows { get; set; }
    public bool ShowLyrics { get; set; } = true;
    public bool AutoConnect { get; set; } = true;
    public double FontSize { get; set; } = 16;
    public int OffsetMs { get; set; }
    public string FontFamily { get; set; } = "Segoe UI";
    public int FontWeightValue { get; set; } = 600;
    public string Alignment { get; set; } = "Center";
    public string Color { get; set; } = "#FFFFFF";
}

public sealed class AppSettingsStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LyricRelay",
        "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
            }
        }
        catch
        {
            // A corrupt local settings file should not prevent the client from starting.
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (UnauthorizedAccessException)
        {
            // Settings are optional; a restricted profile must not stop the client.
        }
        catch (IOException)
        {
            // Settings are optional; a transient file-system failure must not stop the client.
        }
    }
}
