using System.Net;
using System.Text.Json;

namespace LyricRelay.Core;

public sealed class NetEaseProvider : ILyricsProvider
{
    public const string PackageName = "com.netease.cloudmusic";
    private const string Referer = "https://music.163.com/";
    private readonly HttpClient _httpClient;

    public NetEaseProvider(HttpClient httpClient) => _httpClient = httpClient;

    public string Name => "NetEase";

    public bool CanHandle(TrackQuery query) =>
        query.PackageName?.StartsWith(PackageName, StringComparison.OrdinalIgnoreCase) == true;

    public async Task<LyricsResult> SearchAsync(TrackQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var keyword = string.Join(' ', new[] { query.Title, query.Artist }.Where(value => !string.IsNullOrWhiteSpace(value)));
            var searchUrl = "https://music.163.com/api/search/get/web?csrf_token=&s=" + Uri.EscapeDataString(keyword) + "&type=1&offset=0&limit=10";
            using var searchResponse = await SendAsync(searchUrl, cancellationToken);
            if (searchResponse.StatusCode == HttpStatusCode.NotFound) return LyricsResult.NotFound(Name);
            searchResponse.EnsureSuccessStatusCode();
            using var searchJson = JsonDocument.Parse(await searchResponse.Content.ReadAsStringAsync(cancellationToken));
            if (!searchJson.RootElement.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("songs", out var songs) || songs.ValueKind != JsonValueKind.Array)
            {
                return LyricsResult.Invalid(Name);
            }

            var candidate = songs.EnumerateArray()
                .Select(item => CreateCandidate(item, query))
                .Where(item => item is not null)
                .OrderByDescending(item => item!.Value.Score)
                .FirstOrDefault();
            if (candidate is null || candidate.Value.Score < 50) return LyricsResult.NotFound(Name);

            var lyricUrl = $"https://music.163.com/api/song/lyric?id={candidate.Value.SongId}&lv=1&kv=1&tv=-1";
            using var lyricResponse = await SendAsync(lyricUrl, cancellationToken);
            lyricResponse.EnsureSuccessStatusCode();
            using var lyricJson = JsonDocument.Parse(await lyricResponse.Content.ReadAsStringAsync(cancellationToken));
            var lrc = lyricJson.RootElement.TryGetProperty("lrc", out var lrcElement) &&
                      lrcElement.TryGetProperty("lyric", out var lyric)
                ? lyric.GetString()
                : null;
            var translatedLrc = lyricJson.RootElement.TryGetProperty("tlyric", out var translatedElement) &&
                                translatedElement.TryGetProperty("lyric", out var translatedLyric)
                ? translatedLyric.GetString()
                : null;
            return LyricsProviderSupport.ParseLrc(lrc, Name, translatedLrc);
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

    private async Task<HttpResponseMessage> SendAsync(string url, CancellationToken cancellationToken)
    {
        using var request = LyricsProviderSupport.CreateGet(url, Referer);
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static Candidate? CreateCandidate(JsonElement item, TrackQuery query)
    {
        var title = GetString(item, "name");
        var songId = GetLong(item, "id");
        if (string.IsNullOrWhiteSpace(title) || songId is null) return null;
        var artist = item.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array
            ? string.Join(",", artists.EnumerateArray().Select(artistItem => GetString(artistItem, "name")).Where(name => !string.IsNullOrWhiteSpace(name)))
            : null;
        var album = item.TryGetProperty("album", out var albumElement) ? GetString(albumElement, "name") : null;
        var durationMs = GetLong(item, "duration");
        return new Candidate(songId.Value, LyricsProviderSupport.Score(query, title, artist, album, durationMs));
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? GetLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : null;

    private readonly record struct Candidate(long SongId, int Score);
}
