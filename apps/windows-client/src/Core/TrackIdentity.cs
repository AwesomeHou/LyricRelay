using LyricRelay.Protocol;

namespace LyricRelay.Core;

public static class TrackIdentity
{
    private const string QqMusicPackage = "com.tencent.qqmusic";

    public static string LyricsContext(TrackState state)
    {
        // Duration is a query hint, not track identity. The Android QQ adapter
        // derives a stable TrackId from normalized song metadata, so use it to
        // distinguish different songs from the same artist and album while
        // ignoring QQ's changing lyric-fragment title.
        var metadata = $"{state.PackageName}|{state.Artist}|{state.Album}";
        return string.Equals(state.PackageName, QqMusicPackage, StringComparison.OrdinalIgnoreCase)
            ? $"{metadata}|{state.TrackId}"
            : $"{metadata}|{state.Title}";
    }
}
