using System.Diagnostics;

namespace LyricRelay.Core;

public interface IMonotonicClock
{
    TimeSpan Now { get; }
}

public sealed class StopwatchClock : IMonotonicClock
{
    private readonly long _origin = Stopwatch.GetTimestamp();

    public TimeSpan Now => Stopwatch.GetElapsedTime(_origin);
}

public sealed class ManualClock : IMonotonicClock
{
    public TimeSpan Now { get; private set; }

    public void Advance(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        Now += duration;
    }

    public void Set(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        Now = value;
    }
}

