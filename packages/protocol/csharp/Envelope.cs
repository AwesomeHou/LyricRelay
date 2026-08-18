namespace LyricRelay.Protocol;

public sealed record Envelope<T>(
    int Version,
    string Type,
    string MessageId,
    string DeviceId,
    DateTimeOffset SentAt,
    T Payload);

public static class MessageTypes
{
    public const string LinkHello = "link.hello";
    public const string PairingConfirm = "pairing.confirm";
    public const string PairingAccept = "pairing.accept";
    public const string TrackState = "track.state";
    public const string TrackCleared = "track.cleared";
    public const string LinkPing = "link.ping";
    public const string LinkPong = "link.pong";
}

