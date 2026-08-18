using LyricRelay.Protocol;

namespace LyricRelay.Core;

public sealed record TrackQuery(
    string Title,
    string? Artist,
    string? Album,
    long? DurationMs,
    string? PackageName)
{
    public static TrackQuery From(TrackState state) =>
        new(state.Title, state.Artist, state.Album, state.DurationMs, state.PackageName);
}

public enum LyricsFailure
{
    None,
    NotFound,
    NoSyncedLyrics,
    InvalidFormat,
    Timeout,
    NetworkError
}

public sealed record LyricsResult(
    LyricsTimeline? Timeline,
    string? Source,
    LyricsFailure Failure = LyricsFailure.None)
{
    public bool IsSuccess => Timeline is not null && Timeline.Lines.Count > 0 && Failure == LyricsFailure.None;

    public static LyricsResult NotFound(string? source = null) => new(null, source, LyricsFailure.NotFound);
    public static LyricsResult NoSyncedLyrics(string source) => new(null, source, LyricsFailure.NoSyncedLyrics);
    public static LyricsResult Invalid(string source) => new(null, source, LyricsFailure.InvalidFormat);
    public static LyricsResult Network(string source, LyricsFailure failure) => new(null, source, failure);
}

public interface ILyricsProvider
{
    string Name { get; }

    bool CanHandle(TrackQuery query);

    Task<LyricsResult> SearchAsync(TrackQuery query, CancellationToken cancellationToken);
}
