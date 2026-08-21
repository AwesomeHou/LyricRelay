using System.Text;
using System.Text.RegularExpressions;

namespace LyricRelay.Core;

internal static class LyricsProviderSupport
{
    private const string UserAgent = "LyricRelay/0.1";

    public static HttpRequestMessage CreateGet(string url, string? referer = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        if (!string.IsNullOrWhiteSpace(referer))
        {
            request.Headers.Referrer = new Uri(referer);
        }

        return request;
    }

    public static string Normalize(string? value) =>
        Regex.Replace(value?.Trim().ToLowerInvariant() ?? string.Empty, @"[^\p{L}\p{N}]", string.Empty);

    public static bool Matches(TrackQuery query, string title, string? artist, long? durationMs)
    {
        var queryTitle = Normalize(query.Title);
        var candidateTitle = Normalize(title);
        if (queryTitle.Length == 0 || candidateTitle.Length == 0 ||
            (queryTitle != candidateTitle && !queryTitle.Contains(candidateTitle) && !candidateTitle.Contains(queryTitle)))
        {
            return false;
        }

        if (!ArtistMatches(query.Artist, artist))
        {
            return false;
        }

        return !query.DurationMs.HasValue || !durationMs.HasValue ||
               Math.Abs(query.DurationMs.Value - durationMs.Value) <= 15_000;
    }

    public static int Score(TrackQuery query, string title, string? artist, string? album, long? durationMs)
    {
        if (!Matches(query, title, artist, durationMs)) return -1;

        var score = Normalize(query.Title) == Normalize(title) ? 60 : 40;
        if (!string.IsNullOrWhiteSpace(query.Artist) && ArtistMatches(query.Artist, artist)) score += 25;
        if (!string.IsNullOrWhiteSpace(query.Album) &&
            !string.IsNullOrWhiteSpace(album) &&
            (Normalize(query.Album) == Normalize(album) || Normalize(album).Contains(Normalize(query.Album))))
        {
            score += 10;
        }

        if (query.DurationMs.HasValue && durationMs.HasValue)
        {
            score += Math.Max(0, 10 - (int)(Math.Abs(query.DurationMs.Value - durationMs.Value) / 2_000));
        }

        return score;
    }

    public static int ScorePlayerMetadataFallback(TrackQuery query, string title, string? artist, string? album, long? durationMs)
    {
        if (string.IsNullOrWhiteSpace(query.Album) || string.IsNullOrWhiteSpace(album) ||
            !AlbumMatches(query.Album, album) ||
            !ArtistMatches(query.Artist, artist) ||
            (query.DurationMs.HasValue && durationMs.HasValue && Math.Abs(query.DurationMs.Value - durationMs.Value) > 15_000))
        {
            return -1;
        }

        var metadataArtist = Normalize(query.Artist);
        var candidateTitle = Normalize(title);
        var candidateArtist = Normalize(artist);
        return metadataArtist.Contains(candidateTitle) || metadataArtist.Contains(candidateArtist) ? 55 : -1;
    }

    public static string? DecodeBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static LyricsResult ParseLrc(string? lrc, string source, string? translatedLrc = null)
    {
        if (string.IsNullOrWhiteSpace(lrc)) return LyricsResult.NoSyncedLyrics(source);
        var timeline = LrcParser.Parse(lrc, translatedLrc: translatedLrc);
        return timeline.Lines.Count == 0
            ? LyricsResult.Invalid(source)
            : new LyricsResult(timeline, source);
    }

    private static bool ArtistMatches(string? queryArtist, string? candidateArtist)
    {
        if (string.IsNullOrWhiteSpace(queryArtist) || string.IsNullOrWhiteSpace(candidateArtist)) return true;

        var candidate = Normalize(candidateArtist);
        var parts = Regex.Split(queryArtist, @"[/,&、;|]+")
            .Select(Normalize)
            .Where(part => part.Length > 0)
            .ToArray();
        return parts.Length == 0 || parts.Any(part => candidate.Contains(part) || part.Contains(candidate));
    }

    private static bool AlbumMatches(string queryAlbum, string candidateAlbum)
    {
        var queryValue = Normalize(queryAlbum);
        var candidateValue = Normalize(candidateAlbum);
        return queryValue.Length > 0 && candidateValue.Length > 0 &&
               (queryValue == candidateValue || queryValue.Contains(candidateValue) || candidateValue.Contains(queryValue));
    }
}
