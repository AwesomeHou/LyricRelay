using System.Globalization;
using System.Net;
using System.Text.Json;

namespace LyricRelay.Core;

public sealed class LrclibProvider : ILyricsProvider
{
    private readonly HttpClient _httpClient;

    public LrclibProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= new Uri("https://lrclib.net/");
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LyricRelay/0.1");
    }

    public string Name => "LRCLIB";

    public bool CanHandle(TrackQuery query) => true;

    public async Task<LyricsResult> SearchAsync(TrackQuery query, CancellationToken cancellationToken)
    {
        var parameters = new List<string>
        {
            $"track_name={Uri.EscapeDataString(query.Title)}"
        };
        AddOptional(parameters, "artist_name", query.Artist);
        AddOptional(parameters, "album_name", query.Album);
        if (query.DurationMs is > 0)
        {
            var seconds = query.DurationMs.Value / 1000d;
            parameters.Add($"duration={seconds.ToString(CultureInfo.InvariantCulture)}");
        }

        try
        {
            using var response = await _httpClient.GetAsync($"api/get?{string.Join("&", parameters)}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
            return LyricsResult.NotFound(Name);
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!json.RootElement.TryGetProperty("syncedLyrics", out var synced) ||
                synced.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(synced.GetString()))
            {
                return LyricsResult.NoSyncedLyrics(Name);
            }

            var timeline = LrcParser.Parse(synced.GetString()!);
            return timeline.Lines.Count == 0
                ? LyricsResult.Invalid(Name)
                : new LyricsResult(timeline, Name);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LyricsResult.Network(Name, LyricsFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            return LyricsResult.Network(Name, LyricsFailure.NetworkError);
        }
        catch (JsonException)
        {
            return LyricsResult.Invalid(Name);
        }
    }

    private static void AddOptional(List<string> parameters, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }
}
