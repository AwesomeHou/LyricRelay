using LyricRelay.Protocol;

namespace LyricRelay.Core;

public sealed class LyricsCoordinator : IAsyncDisposable
{
    private readonly IReadOnlyList<ILyricsProvider> _providers;
    private readonly object _gate = new();
    private CancellationTokenSource? _requestCancellation;
    private string? _activeTrackId;

    public LyricsCoordinator(IEnumerable<ILyricsProvider> providers)
    {
        _providers = providers.ToArray();
    }

    public event EventHandler<LyricsResultChangedEventArgs>? ResultChanged;

    public async Task<LyricsResult> LoadAsync(TrackState state, CancellationToken cancellationToken)
    {
        CancellationToken requestToken;
        lock (_gate)
        {
            _requestCancellation?.Cancel();
            _requestCancellation?.Dispose();
            _requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeTrackId = state.TrackId;
            requestToken = _requestCancellation.Token;
        }

        LyricsResult? lastResult = null;
        foreach (var provider in _providers)
        {
            if (!provider.CanHandle(TrackQuery.From(state)))
            {
                continue;
            }

            LyricsResult result;
            try
            {
                result = await provider.SearchAsync(TrackQuery.From(state), requestToken);
            }
            catch (OperationCanceledException) when (requestToken.IsCancellationRequested)
            {
                throw;
            }

            lock (_gate)
            {
                if (_activeTrackId != state.TrackId)
                {
                    return LyricsResult.NotFound();
                }
            }

            lastResult = result;
            if (result.IsSuccess)
            {
                ResultChanged?.Invoke(this, new LyricsResultChangedEventArgs(state.TrackId, result));
                return result;
            }
        }

        var fallback = lastResult ?? LyricsResult.NotFound();
        ResultChanged?.Invoke(this, new LyricsResultChangedEventArgs(state.TrackId, fallback));
        return fallback;
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _requestCancellation?.Cancel();
            _requestCancellation?.Dispose();
            _requestCancellation = null;
        }

        return ValueTask.CompletedTask;
    }
}

public sealed class LyricsResultChangedEventArgs : EventArgs
{
    public LyricsResultChangedEventArgs(string trackId, LyricsResult result)
    {
        TrackId = trackId;
        Result = result;
    }

    public string TrackId { get; }
    public LyricsResult Result { get; }
}
