namespace LyricRelay.Protocol;

public enum TrackPlaybackState
{
    Playing,
    Paused,
    Stopped
}

public sealed record TrackState(
    string TrackId,
    string Title,
    string? Artist,
    string? Album,
    long? DurationMs,
    string? PackageName,
    TrackPlaybackState State,
    long PositionMs,
    double PlaybackSpeed,
    long StateVersion)
{
    public bool IsValid(out string error)
    {
        if (string.IsNullOrWhiteSpace(TrackId))
        {
            error = "trackId is required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Title))
        {
            error = "title is required";
            return false;
        }

        if (DurationMs is < 0)
        {
            error = "durationMs must be null or non-negative";
            return false;
        }

        if (PositionMs < 0)
        {
            error = "positionMs must be non-negative";
            return false;
        }

        if (PlaybackSpeed <= 0 || PlaybackSpeed > 8)
        {
            error = "playbackSpeed must be greater than 0 and no greater than 8";
            return false;
        }

        if (StateVersion < 0)
        {
            error = "stateVersion must be non-negative";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

