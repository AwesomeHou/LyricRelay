using LyricRelay.Core;
using LyricRelay.Protocol;

if (args.Contains("--online", StringComparer.OrdinalIgnoreCase))
{
    RunOnlineProviderChecks();
    return;
}

var tests = new (string Name, Action Run)[]
{
    ("LRC parses multiple tags and sorts lines", LrcParsesMultipleTags),
    ("LRC aligns optional translations by timestamp", LrcAlignsTranslations),
    ("LRC keeps original when translation is absent", LrcKeepsOriginalWithoutTranslation),
    ("Timeline advances with monotonic clock and speed", TimelineAdvances),
    ("Pause freezes position", PauseFreezes),
    ("Seek rebases position", SeekRebases),
    ("Old state version is ignored", OldStateIsIgnored),
    ("Duration clamps position", DurationClamps),
    ("Current line uses last timestamp", CurrentLineUsesLastTimestamp),
    ("Offset is applied to parsed lyrics", OffsetIsApplied),
    ("Protocol state round trips as JSON", ProtocolRoundTrips),
    ("LRCLIB synced lyrics are parsed", LrclibResponseIsParsed),
    ("Provider routes by player package", ProvidersRouteByPackage),
    ("QQ Music response is parsed", QqMusicResponseIsParsed),
    ("QQ Music translation response is parsed", QqMusicTranslationResponseIsParsed),
    ("QQ Music downloaded translation response is parsed", QqMusicDownloadedTranslationResponseIsParsed),
    ("NetEase response is parsed", NetEaseResponseIsParsed),
    ("NetEase translation response is parsed", NetEaseTranslationResponseIsParsed),
    ("KuGou response is parsed", KuGouResponseIsParsed),
    ("Provider failures fall back", ProviderFailuresFallBack),
    ("QQ metadata anomaly can match by album", QqMetadataAnomalyMatches),
    ("QQ split metadata can recover the real title", QqSplitMetadataMatches),
    ("Lyrics context ignores media-session duration jitter", LyricsContextIgnoresDurationJitter)
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS {test.Name}");
}

static void LrcParsesMultipleTags()
{
    var timeline = LrcParser.Parse("[00:01.20][00:02.200] first\n[00:00.500] start\n[bad] ignored");
    Assert.Equal(3, timeline.Lines.Count);
    Assert.Equal(500L, timeline.Lines[0].StartMs);
    Assert.Equal(1200L, timeline.Lines[1].StartMs);
    Assert.Equal(2200L, timeline.Lines[2].StartMs);
}

static void LrcAlignsTranslations()
{
    var timeline = LrcParser.Parse(
        "[00:01.000] original one\n[00:02.000] original two",
        translatedLrc: "[00:01.000] 中文一\n[00:02.000] 中文二");

    Assert.Equal("中文一", timeline.Lines[0].Translation);
    Assert.Equal("中文二", timeline.Lines[1].Translation);
}

static void LrcKeepsOriginalWithoutTranslation()
{
    var timeline = LrcParser.Parse("[00:01.000] original");

    Assert.Equal("original", timeline.Lines[0].Text);
    Assert.Equal<string?>(null, timeline.Lines[0].Translation);
}

static void TimelineAdvances()
{
    var clock = new ManualClock();
    var engine = new TimelineEngine(clock);
    engine.Apply(State("song", TrackPlaybackState.Playing, 1000, 1.5, 1));
    clock.Advance(TimeSpan.FromMilliseconds(500));
    Assert.Equal(1750L, engine.GetPositionMs());
}

static void PauseFreezes()
{
    var clock = new ManualClock();
    var engine = new TimelineEngine(clock);
    engine.Apply(State("song", TrackPlaybackState.Paused, 3000, 1, 1));
    clock.Advance(TimeSpan.FromSeconds(10));
    Assert.Equal(3000L, engine.GetPositionMs());
}

static void SeekRebases()
{
    var clock = new ManualClock();
    var engine = new TimelineEngine(clock);
    engine.Apply(State("song", TrackPlaybackState.Playing, 1000, 1, 1));
    clock.Advance(TimeSpan.FromSeconds(2));
    engine.Apply(State("song", TrackPlaybackState.Playing, 120000, 1, 2));
    Assert.Equal(120000L, engine.GetPositionMs());
}

static void OldStateIsIgnored()
{
    var clock = new ManualClock();
    var engine = new TimelineEngine(clock);
    engine.Apply(State("song", TrackPlaybackState.Paused, 5000, 1, 2));
    engine.Apply(State("song", TrackPlaybackState.Paused, 1000, 1, 1));
    Assert.Equal(5000L, engine.GetPositionMs());
}

static void DurationClamps()
{
    var clock = new ManualClock();
    var engine = new TimelineEngine(clock);
    engine.Apply(State("song", TrackPlaybackState.Playing, 900, 1, 1, 1000));
    clock.Advance(TimeSpan.FromSeconds(2));
    Assert.Equal(1000L, engine.GetPositionMs());
}

static void CurrentLineUsesLastTimestamp()
{
    var clock = new ManualClock();
    var engine = new TimelineEngine(clock);
    engine.Apply(State("song", TrackPlaybackState.Paused, 2500, 1, 1));
    var timeline = LrcParser.Parse("[00:01.000] one\n[00:02.000] two\n[00:03.000] three");
    Assert.Equal("two", engine.GetCurrentLine(timeline)?.Text);
}

static void LyricsContextIgnoresDurationJitter()
{
    var first = State("track-1", TrackPlaybackState.Playing, 1000, 1, 1, 180000) with
    {
        PackageName = "com.tencent.qqmusic",
        Title = "song",
        Artist = "artist",
        Album = "album"
    };
    var corrected = first with
    {
        DurationMs = 181000,
        PositionMs = 1500,
        StateVersion = 2
    };

    Assert.Equal(TrackIdentity.LyricsContext(first), TrackIdentity.LyricsContext(corrected));

    var differentSong = first with { TrackId = "track-2" };
    Assert.Equal(false, TrackIdentity.LyricsContext(first) == TrackIdentity.LyricsContext(differentSong));
}

static void OffsetIsApplied()
{
    var timeline = LrcParser.Parse("[00:01.000] line", offsetMs: -250);
    Assert.Equal(750L, timeline.Lines[0].StartMs);
}

static void ProtocolRoundTrips()
{
    var state = State("song", TrackPlaybackState.Playing, 1000, 1, 7);
    var message = new Envelope<TrackState>(1, MessageTypes.TrackState, "message", "android", DateTimeOffset.UnixEpoch, state);
    var json = ProtocolJson.Serialize(message);
    var parsed = ProtocolJson.Deserialize<TrackState>(json);
    Assert.Equal("song", parsed?.Payload.TrackId);
    Assert.Equal(TrackPlaybackState.Playing, parsed?.Payload.State);
}

static void LrclibResponseIsParsed()
{
    var provider = new LrclibProvider(new HttpClient(new StubHandler("{\"syncedLyrics\":\"[00:01.000] test\"}")));
    var result = provider.SearchAsync(new TrackQuery("title", "artist", null, 1000, "player"), CancellationToken.None).GetAwaiter().GetResult();
    Assert.Equal(true, result.IsSuccess);
    Assert.Equal("test", result.Timeline?.Lines[0].Text);
}

static void ProvidersRouteByPackage()
{
    var qq = new TrackQuery("title", "artist", null, 1000, QqMusicProvider.PackageName);
    var netease = new TrackQuery("title", "artist", null, 1000, NetEaseProvider.PackageName);
    var kugou = new TrackQuery("title", "artist", null, 1000, KuGouProvider.PackagePrefix + ".lite");
    var other = new TrackQuery("title", "artist", null, 1000, "com.spotify.music");

    Assert.Equal(true, new QqMusicProvider(new HttpClient()).CanHandle(qq));
    Assert.Equal(false, new QqMusicProvider(new HttpClient()).CanHandle(other));
    Assert.Equal(true, new NetEaseProvider(new HttpClient()).CanHandle(netease));
    Assert.Equal(true, new KuGouProvider(new HttpClient()).CanHandle(kugou));
}

static void QqMusicResponseIsParsed()
{
    var lyric = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("[00:01.000] qq"));
    var handler = new QueueHandler(
        "{\"data\":{\"song\":{\"list\":[{\"songname\":\"title\",\"songmid\":\"mid\",\"albumname\":\"album\",\"interval\":1,\"singer\":[{\"name\":\"artist\"}]}]}}}",
        $"{{\"lyric\":\"{lyric}\"}}");
    var result = new QqMusicProvider(new HttpClient(handler)).SearchAsync(
        new TrackQuery("title", "artist", "album", 1000, QqMusicProvider.PackageName), CancellationToken.None).GetAwaiter().GetResult();

    Assert.Equal(true, result.IsSuccess);
    Assert.Equal("QQ Music", result.Source);
    Assert.Equal("qq", result.Timeline?.Lines[0].Text);
}

static void QqMusicTranslationResponseIsParsed()
{
    var lyric = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("[00:01.000] Japanese"));
    var translation = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("[00:01.000] Chinese"));
    var handler = new QueueHandler(
        "{\"data\":{\"song\":{\"list\":[{\"songname\":\"title\",\"songmid\":\"mid\",\"albumname\":\"album\",\"interval\":1,\"singer\":[{\"name\":\"artist\"}]}]}}}",
        $"{{\"lyric\":\"{lyric}\",\"trans\":\"{translation}\"}}");
    var result = new QqMusicProvider(new HttpClient(handler)).SearchAsync(
        new TrackQuery("title", "artist", "album", 1000, QqMusicProvider.PackageName), CancellationToken.None).GetAwaiter().GetResult();

    Assert.Equal("Chinese", result.Timeline?.Lines[0].Translation);
}

static void QqMusicDownloadedTranslationResponseIsParsed()
{
    var lyric = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("[00:01.000] English"));
    var handler = new QueueHandler(
        "{\"data\":{\"song\":{\"list\":[{\"songname\":\"title\",\"songmid\":\"mid\",\"songid\":42,\"albumname\":\"album\",\"interval\":1,\"singer\":[{\"name\":\"artist\"}]}]}}}",
        $"{{\"lyric\":\"{lyric}\"}}",
        "<!-- <command-lable-xwl78-qq-music><lyric><contentts><![CDATA[[00:01.000] Chinese]]></contentts></lyric></command-lable-xwl78-qq-music> -->");
    var result = new QqMusicProvider(new HttpClient(handler)).SearchAsync(
        new TrackQuery("title", "artist", "album", 1000, QqMusicProvider.PackageName), CancellationToken.None).GetAwaiter().GetResult();

    Assert.Equal("Chinese", result.Timeline?.Lines[0].Translation);
}

static void NetEaseResponseIsParsed()
{
    var handler = new QueueHandler(
        "{\"result\":{\"songs\":[{\"name\":\"title\",\"id\":42,\"duration\":1000,\"artists\":[{\"name\":\"artist\"}],\"album\":{\"name\":\"album\"}}]}}",
        "{\"lrc\":{\"lyric\":\"[00:01.000] netease\"}}");
    var result = new NetEaseProvider(new HttpClient(handler)).SearchAsync(
        new TrackQuery("title", "artist", "album", 1000, NetEaseProvider.PackageName), CancellationToken.None).GetAwaiter().GetResult();

    Assert.Equal(true, result.IsSuccess);
    Assert.Equal("NetEase", result.Source);
    Assert.Equal("netease", result.Timeline?.Lines[0].Text);
}

static void NetEaseTranslationResponseIsParsed()
{
    var handler = new QueueHandler(
        "{\"result\":{\"songs\":[{\"name\":\"title\",\"id\":42,\"duration\":1000,\"artists\":[{\"name\":\"artist\"}],\"album\":{\"name\":\"album\"}}]}}",
        "{\"lrc\":{\"lyric\":\"[00:01.000] Japanese\"},\"tlyric\":{\"lyric\":\"[00:01.000] Chinese\"}}");
    var result = new NetEaseProvider(new HttpClient(handler)).SearchAsync(
        new TrackQuery("title", "artist", "album", 1000, NetEaseProvider.PackageName), CancellationToken.None).GetAwaiter().GetResult();

    Assert.Equal("Chinese", result.Timeline?.Lines[0].Translation);
}

static void KuGouResponseIsParsed()
{
    var lyric = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("[00:01.000] kugou"));
    var handler = new QueueHandler(
        "{\"data\":{\"lists\":[{\"SongName\":\"title\",\"SingerName\":\"artist\",\"FileHash\":\"hash\",\"Duration\":1,\"AlbumName\":\"album\"}]}}",
        "{\"candidates\":[{\"id\":\"id\",\"accesskey\":\"key\",\"song\":\"title\",\"singer\":\"artist\",\"duration\":1000}]}",
        $"{{\"content\":\"{lyric}\"}}");
    var result = new KuGouProvider(new HttpClient(handler)).SearchAsync(
        new TrackQuery("title", "artist", "album", 1000, KuGouProvider.PackagePrefix), CancellationToken.None).GetAwaiter().GetResult();

    Assert.Equal(true, result.IsSuccess);
    Assert.Equal("KuGou", result.Source);
    Assert.Equal("kugou", result.Timeline?.Lines[0].Text);
}

static void ProviderFailuresFallBack()
{
    var first = new FakeProvider("first", LyricsResult.NotFound("first"));
    var second = new FakeProvider("second", new LyricsResult(LrcParser.Parse("[00:01.000] fallback"), "second"));
    var coordinator = new LyricsCoordinator(new ILyricsProvider[] { first, second });
    var state = State("song", TrackPlaybackState.Playing, 1000, 1, 1);
    var result = coordinator.LoadAsync(state, CancellationToken.None).GetAwaiter().GetResult();

    Assert.Equal(true, result.IsSuccess);
    Assert.Equal("second", result.Source);
    Assert.Equal(1, first.Calls);
    Assert.Equal(1, second.Calls);
}

static void QqMetadataAnomalyMatches()
{
    var lyric = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("[00:01.000] qq anomaly"));
    var handler = new QueueHandler(
        "{\"data\":{\"song\":{\"list\":[{\"songname\":\"walking proud\",\"songmid\":\"mid\",\"albumname\":\"album\",\"interval\":1,\"singer\":[{\"name\":\"artist\"}]}]}}}",
        $"{{\"lyric\":\"{lyric}\"}}");
    var result = new QqMusicProvider(new HttpClient(handler)).SearchAsync(
        new TrackQuery("garbled title", "walking proud-artist", "album", 1000, QqMusicProvider.PackageName), CancellationToken.None).GetAwaiter().GetResult();

    Assert.Equal(true, result.IsSuccess);
    Assert.Equal("qq anomaly", result.Timeline?.Lines[0].Text);
}

static void QqSplitMetadataMatches()
{
    var lyric = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("[00:01.000] qq split"));
    var handler = new QueueHandler(
        "{\"data\":{\"song\":{\"list\":[]}}}",
        "{\"data\":{\"song\":{\"list\":[{\"songname\":\"Poker Face\",\"songmid\":\"mid\",\"albumname\":\"Prom 2022 (Explicit)\",\"interval\":239,\"singer\":[{\"name\":\"Lady Gaga\"}]}]}}}",
        $"{{\"lyric\":\"{lyric}\"}}");
    var result = new QqMusicProvider(new HttpClient(handler)).SearchAsync(
        new TrackQuery("Mum mum mum mah", "Poker Face-Lady Gaga", "Prom 2022 (Explicit)", 239000, QqMusicProvider.PackageName), CancellationToken.None).GetAwaiter().GetResult();

    Assert.Equal(true, result.IsSuccess);
    Assert.Equal("qq split", result.Timeline?.Lines[0].Text);
}

static void RunOnlineProviderChecks()
{
    var qq = new QqMusicProvider(new HttpClient { Timeout = TimeSpan.FromSeconds(15) });
    var qqResult = qq.SearchAsync(new TrackQuery("晴天", "周杰伦", "叶惠美", 269000, QqMusicProvider.PackageName), CancellationToken.None).GetAwaiter().GetResult();
    ReportOnlineResult(qq.Name, qqResult);

    var kugou = new KuGouProvider(new HttpClient { Timeout = TimeSpan.FromSeconds(15) });
    var kugouResult = kugou.SearchAsync(new TrackQuery("晴天", "周杰伦", null, 218000, KuGouProvider.PackagePrefix), CancellationToken.None).GetAwaiter().GetResult();
    ReportOnlineResult(kugou.Name, kugouResult);
}

static void ReportOnlineResult(string provider, LyricsResult result)
{
    Console.WriteLine($"online {provider}: success={result.IsSuccess} failure={result.Failure}");
    if (result.IsSuccess)
    {
        Console.WriteLine($"PASS online {provider}");
    }
    else
    {
        Console.WriteLine($"SKIP online {provider}: external network or provider response was unavailable");
    }
}

static TrackState State(string trackId, TrackPlaybackState state, long position, double speed, long version, long? duration = 300000) =>
    new(trackId, "title", "artist", "album", duration, "player", state, position, speed, version);

sealed class StubHandler : HttpMessageHandler
{
    private readonly string _body;

    public StubHandler(string body) => _body = body;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json")
        });
}

sealed class QueueHandler : HttpMessageHandler
{
    private readonly Queue<string> _responses;

    public QueueHandler(params string[] responses) => _responses = new Queue<string>(responses);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(_responses.Dequeue(), System.Text.Encoding.UTF8, "application/json")
        });
}

sealed class FakeProvider : ILyricsProvider
{
    private readonly LyricsResult _result;

    public FakeProvider(string name, LyricsResult result)
    {
        Name = name;
        _result = result;
    }

    public string Name { get; }
    public int Calls { get; private set; }
    public bool CanHandle(TrackQuery query) => true;

    public Task<LyricsResult> SearchAsync(TrackQuery query, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(_result);
    }
}

static class Assert
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {expected}, got {actual}");
        }
    }
}
