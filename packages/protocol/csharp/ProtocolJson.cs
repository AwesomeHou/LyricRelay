using System.Text.Json;
using System.Text.Json.Serialization;

namespace LyricRelay.Protocol;

public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize<T>(Envelope<T> message) =>
        JsonSerializer.Serialize(message, Options);

    public static Envelope<T>? Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<Envelope<T>>(json, Options);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

