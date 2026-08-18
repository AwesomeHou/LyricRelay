using System.Net;
using System.Text.Json;

namespace LyricRelay.Core;

public sealed class KuGouProvider : ILyricsProvider
{
    public const string PackagePrefix = "com.kugou.android";
    private const string Referer = "https://www.kugou.com/";
    private readonly HttpClient _httpClient;

    public KuGouProvider(HttpClient httpClient) => _httpClient = httpClient;

    public string Name => "KuGou";

    public bool CanHandle(TrackQuery query) =>
        query.PackageName?.StartsWith(PackagePrefix, StringComparison.OrdinalIgnoreCase) == true;

    public async Task<LyricsResult> SearchAsync(TrackQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var keyword = string.Join(' ', new[] { query.Title, query.Artist }.Where(value => !string.IsNullOrWhiteSpace(value)));
            var searchUrl = "https://songsearch.kugou.com/song_search_v2?keyword=" + Uri.EscapeDataString(keyword) + "&page=1&pagesize=10";
            using var searchResponse = await SendAsync(searchUrl, cancellationToken);
            if (searchResponse.StatusCode == HttpStatusCode.NotFound) return LyricsResult.NotFound(Name);
            searchResponse.EnsureSuccessStatusCode();
            using var searchJson = JsonDocument.Parse(await searchResponse.Content.ReadAsStringAsync(cancellationToken));
            if (!searchJson.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("lists", out var lists) || lists.ValueKind != JsonValueKind.Array)
            {
                return LyricsResult.Invalid(Name);
            }

            var candidate = lists.EnumerateArray()
                .Select(item => CreateCandidate(item, query))
                .Where(item => item is not null)
                .OrderByDescending(item => item!.Value.Score)
                .FirstOrDefault();
            if (candidate is null || candidate.Value.Score < 50) return LyricsResult.NotFound(Name);

            var lyricSearchUrl = "https://krcs.kugou.com/search?ver=1&man=yes&client=pc&keyword=" +
                                 Uri.EscapeDataString($"{candidate.Value.Artist} - {candidate.Value.Title}") +
                                 $"&hash={candidate.Value.Hash}&timelength={candidate.Value.DurationMs}";
            using var lyricSearchResponse = await SendAsync(lyricSearchUrl, cancellationToken);
            lyricSearchResponse.EnsureSuccessStatusCode();
            using var lyricSearchJson = JsonDocument.Parse(await lyricSearchResponse.Content.ReadAsStringAsync(cancellationToken));
            if (!lyricSearchJson.RootElement.TryGetProperty("candidates", out var lyricCandidates) ||
                lyricCandidates.ValueKind != JsonValueKind.Array)
            {
                return LyricsResult.NotFound(Name);
            }

            var lyricCandidate = lyricCandidates.EnumerateArray()
                .Select(item => CreateLyricCandidate(item, query))
                .Where(item => item is not null)
                .OrderByDescending(item => item!.Value.Score)
                .FirstOrDefault();
            if (lyricCandidate is null || lyricCandidate.Value.Score < 50) return LyricsResult.NotFound(Name);

            var downloadUrl = "https://lyrics.kugou.com/download?ver=1&client=pc&fmt=lrc&charset=utf8" +
                              $"&accesskey={Uri.EscapeDataString(lyricCandidate.Value.AccessKey)}" +
                              $"&id={Uri.EscapeDataString(lyricCandidate.Value.Id)}" +
                              $"&hash={candidate.Value.Hash}&timelength={candidate.Value.DurationMs}";
            using var downloadResponse = await SendAsync(downloadUrl, cancellationToken);
            downloadResponse.EnsureSuccessStatusCode();
            using var downloadJson = JsonDocument.Parse(await downloadResponse.Content.ReadAsStringAsync(cancellationToken));
            var encoded = downloadJson.RootElement.TryGetProperty("content", out var content) ? content.GetString() : null;
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
        var title = GetString(item, "SongName");
        var hash = GetString(item, "FileHash");
        var artist = GetString(item, "SingerName");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(hash)) return null;
        var album = GetString(item, "AlbumName");
        long? durationMs = GetLong(item, "Duration") is long seconds ? (long?)(seconds * 1000) : null;
        return new Candidate(title, artist, hash, durationMs, LyricsProviderSupport.Score(query, title, artist, album, durationMs));
    }

    private static LyricCandidate? CreateLyricCandidate(JsonElement item, TrackQuery query)
    {
        var title = GetString(item, "song");
        var artist = GetString(item, "singer");
        var id = GetString(item, "id");
        var accessKey = GetString(item, "accesskey");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(accessKey)) return null;
        var durationMs = GetLong(item, "duration");
        return new LyricCandidate(id, accessKey, LyricsProviderSupport.Score(query, title ?? query.Title, artist, null, durationMs));
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? GetLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.TryGetInt64(out var number)) return number;
        return long.TryParse(value.GetString(), out number) ? number : null;
    }

    private readonly record struct Candidate(string Title, string? Artist, string Hash, long? DurationMs, int Score);
    private readonly record struct LyricCandidate(string Id, string AccessKey, int Score);
}
