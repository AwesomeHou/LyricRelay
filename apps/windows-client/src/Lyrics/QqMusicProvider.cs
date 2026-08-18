using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LyricRelay.Core;

public sealed class QqMusicProvider : ILyricsProvider
{
    public const string PackageName = "com.tencent.qqmusic";
    private const string Referer = "https://y.qq.com/";
    private readonly HttpClient _httpClient;

    public QqMusicProvider(HttpClient httpClient) => _httpClient = httpClient;

    public string Name => "QQ Music";

    public bool CanHandle(TrackQuery query) =>
        string.Equals(query.PackageName, PackageName, StringComparison.OrdinalIgnoreCase);

    public async Task<LyricsResult> SearchAsync(TrackQuery query, CancellationToken cancellationToken)
    {
        try
        {
            Candidate? candidate = null;
            foreach (var searchQuery in BuildSearchQueries(query))
            {
                var searchUrl = "https://c.y.qq.com/soso/fcgi-bin/client_search_cp?format=json&p=1&n=10&w=" +
                                Uri.EscapeDataString(string.Join(' ', new[] { searchQuery.Title, searchQuery.Artist }.Where(value => !string.IsNullOrWhiteSpace(value))));
                using var searchResponse = await SendAsync(searchUrl, cancellationToken);
                if (searchResponse.StatusCode == HttpStatusCode.NotFound) continue;
                searchResponse.EnsureSuccessStatusCode();
                using var searchJson = JsonDocument.Parse(await searchResponse.Content.ReadAsStringAsync(cancellationToken));
                if (!searchJson.RootElement.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("song", out var songs) ||
                    !songs.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var bestForQuery = list.EnumerateArray()
                    .Select(item => CreateCandidate(item, searchQuery))
                    .Where(item => item is not null)
                    .OrderByDescending(item => item!.Value.Score)
                    .FirstOrDefault();
                if (bestForQuery is not null && (candidate is null || bestForQuery.Value.Score > candidate.Value.Score))
                {
                    candidate = bestForQuery;
                }

                if (candidate?.Score >= 50) break;
            }

            if (candidate is null || candidate.Value.Score < 50) return LyricsResult.NotFound(Name);

            var lyricUrl = "https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid=" +
                           Uri.EscapeDataString(candidate.Value.SongMid) + "&format=json";
            using var lyricResponse = await SendAsync(lyricUrl, cancellationToken);
            lyricResponse.EnsureSuccessStatusCode();
            using var lyricJson = JsonDocument.Parse(await lyricResponse.Content.ReadAsStringAsync(cancellationToken));
            var encoded = lyricJson.RootElement.TryGetProperty("lyric", out var lyric) ? lyric.GetString() : null;
            return LyricsProviderSupport.ParseLrc(LyricsProviderSupport.DecodeBase64(encoded), Name);
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
        var title = GetString(item, "songname");
        var songMid = GetString(item, "songmid");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(songMid)) return null;
        var artist = item.TryGetProperty("singer", out var singers) && singers.ValueKind == JsonValueKind.Array
            ? string.Join(",", singers.EnumerateArray().Select(singer => GetString(singer, "name")).Where(name => !string.IsNullOrWhiteSpace(name)))
            : null;
        var album = GetString(item, "albumname");
        long? durationMs = GetLong(item, "interval") is long seconds ? (long?)(seconds * 1000) : null;
        var score = Math.Max(
            LyricsProviderSupport.Score(query, title, artist, album, durationMs),
            LyricsProviderSupport.ScorePlayerMetadataFallback(query, title, artist, album, durationMs));
        return new Candidate(songMid, score);
    }

    private static IEnumerable<TrackQuery> BuildSearchQueries(TrackQuery query)
    {
        yield return query;

        // Some QQ Music MediaSessions expose a lyric fragment as title and
        // put the real song title and artist into one hyphenated artist field.
        // Keep this narrow fallback behind the QQ adapter so normal metadata
        // is still matched first.
        var parts = Regex.Split(query.Artist ?? string.Empty, @"\s*[-–—]\s*")
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToArray();
        if (parts.Length == 2)
        {
            yield return query with { Title = parts[0], Artist = parts[1] };
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? GetLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : null;

    private readonly record struct Candidate(string SongMid, int Score);
}
