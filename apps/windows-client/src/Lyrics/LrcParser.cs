using System.Globalization;
using System.Text.RegularExpressions;

namespace LyricRelay.Core;

public static partial class LrcParser
{
    public static LyricsTimeline Parse(string lrc, int offsetMs = 0)
    {
        if (string.IsNullOrWhiteSpace(lrc))
        {
            return new LyricsTimeline(Array.Empty<TimedLine>());
        }

        var lines = new List<TimedLine>();
        foreach (var rawLine in lrc.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
        {
            var matches = TimeTagRegex().Matches(rawLine);
            if (matches.Count == 0)
            {
                continue;
            }

            var text = TimeTagRegex().Replace(rawLine, string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (Match match in matches)
            {
                if (!TryParseTimestamp(match.Groups[1].Value, out var timestampMs))
                {
                    continue;
                }

                var adjusted = Math.Max(0, timestampMs + offsetMs);
                lines.Add(new TimedLine(adjusted, text));
            }
        }

        return new LyricsTimeline(lines);
    }

    private static bool TryParseTimestamp(string value, out long milliseconds)
    {
        milliseconds = 0;
        var parts = value.Split(':', 2);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            !decimal.TryParse(parts[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds) ||
            minutes < 0 || seconds < 0 || seconds >= 60)
        {
            return false;
        }

        milliseconds = (long)Math.Round((minutes * 60m + seconds) * 1000m, MidpointRounding.AwayFromZero);
        return true;
    }

    [GeneratedRegex(@"\[(\d{1,3}:\d{1,2}(?:\.\d{1,3})?)\]")]
    private static partial Regex TimeTagRegex();
}

