using LyricRelay.Protocol;

namespace LyricRelay.Core;

public sealed class TimelineEngine
{
    private readonly IMonotonicClock _clock;
    private TrackState? _state;
    private TimeSpan _baseClock;
    private long _basePositionMs;

    public TimelineEngine(IMonotonicClock clock)
    {
        _clock = clock;
    }

    public string? TrackId => _state?.TrackId;

    public long? StateVersion => _state?.StateVersion;

    public void Apply(TrackState state)
    {
        if (!state.IsValid(out var error))
        {
            throw new ArgumentException(error, nameof(state));
        }

        if (_state is not null &&
            _state.TrackId == state.TrackId &&
            state.StateVersion < _state.StateVersion)
        {
            return;
        }

        _state = state;
        _baseClock = _clock.Now;
        _basePositionMs = Clamp(state.PositionMs, state.DurationMs);
    }

    public long? GetPositionMs()
    {
        if (_state is null)
        {
            return null;
        }

        if (_state.State != TrackPlaybackState.Playing)
        {
            return _basePositionMs;
        }

        var elapsedMs = (_clock.Now - _baseClock).TotalMilliseconds;
        var position = _basePositionMs + (long)Math.Round(elapsedMs * _state.PlaybackSpeed, MidpointRounding.AwayFromZero);
        return Clamp(position, _state.DurationMs);
    }

    public TimedLine? GetCurrentLine(LyricsTimeline timeline, int offsetMs = 0)
    {
        var position = GetPositionMs();
        return position is null ? null : timeline.FindCurrent(position.Value + offsetMs);
    }

    private static long Clamp(long position, long? durationMs)
    {
        var result = Math.Max(0, position);
        return durationMs is null ? result : Math.Min(result, durationMs.Value);
    }
}
