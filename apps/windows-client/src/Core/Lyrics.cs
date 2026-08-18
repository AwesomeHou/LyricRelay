namespace LyricRelay.Core;

public sealed record TimedLine(long StartMs, string Text);

public sealed class LyricsTimeline
{
    public LyricsTimeline(IReadOnlyList<TimedLine> lines)
    {
        Lines = lines
            .Where(line => line.StartMs >= 0 && !string.IsNullOrWhiteSpace(line.Text))
            .OrderBy(line => line.StartMs)
            .ToArray();
    }

    public IReadOnlyList<TimedLine> Lines { get; }

    public TimedLine? FindCurrent(long positionMs)
    {
        TimedLine? current = null;
        foreach (var line in Lines)
        {
            if (line.StartMs > positionMs)
            {
                break;
            }

            current = line;
        }

        return current;
    }
}

