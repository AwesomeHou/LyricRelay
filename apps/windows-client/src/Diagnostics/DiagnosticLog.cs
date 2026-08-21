using System.Security.Cryptography;
using System.Text;
using System.IO;
using LyricRelay.Core;
using LyricRelay.Protocol;

namespace LyricRelay.Windows;

internal static class DiagnosticLog
{
    private static readonly object Gate = new();
    private static readonly string SessionId = Guid.NewGuid().ToString("N")[..8];
    private static readonly string LogFilePath = ResolveLogFilePath();

    public static string FilePath => LogFilePath;

    public static void Info(string category, string message)
    {
        var line = $"{DateTimeOffset.Now:O} session={SessionId} category={category} {Sanitize(message)}{Environment.NewLine}";
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
                File.AppendAllText(LogFilePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics must never affect playback or rendering.
        }
    }

    public static string Hash(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "-";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(digest.AsSpan(0, 4)).ToLowerInvariant();
    }

    public static string StateSummary(TrackState state) =>
        $"track={Hash(state.TrackId)} pkg={state.PackageName ?? "-"} title={Hash(state.Title)} artist={Hash(state.Artist)} album={Hash(state.Album)} " +
        $"duration={state.DurationMs?.ToString() ?? "-"} playback={state.State} position={state.PositionMs} speed={state.PlaybackSpeed:0.###} version={state.StateVersion}";

    public static string StateChanges(TrackState? previous, TrackState current)
    {
        if (previous is null) return "first";

        var changes = new List<string>();
        if (previous.TrackId != current.TrackId) changes.Add("trackId");
        if (previous.Title != current.Title) changes.Add("title");
        if (previous.Artist != current.Artist) changes.Add("artist");
        if (previous.Album != current.Album) changes.Add("album");
        if (previous.DurationMs != current.DurationMs) changes.Add("duration");
        if (previous.PackageName != current.PackageName) changes.Add("package");
        if (previous.State != current.State) changes.Add("playbackState");
        return changes.Count == 0 ? "none" : string.Join(',', changes);
    }

    public static string LineSummary(TimedLine? line) =>
        line is null
            ? "none"
            : $"start={line.StartMs} text={Hash(line.Text)} translation={(string.IsNullOrWhiteSpace(line.Translation) ? "none" : "present")}";

    private static string ResolveLogFilePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) root = AppContext.BaseDirectory;
        return Path.Combine(root, "LyricRelay", "LyricRelay.diagnostics.log");
    }

    private static string Sanitize(string message) => message.Replace('\r', ' ').Replace('\n', ' ');
}
