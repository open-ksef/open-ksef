using System.Collections.Concurrent;

namespace OpenKSeF.Sync;

/// <summary>
/// Per-NIP KSeF request pacing (all requests, plus a slower metadata key) and XML 429 blocks until Retry-After. In memory, per process.
/// </summary>
public sealed class KSeFRequestLimits(TimeProvider time)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _blockedUntil = new();
    private readonly Dictionary<string, DateTimeOffset> _nextTurn = new();

    public bool IsBlocked(string nip) =>
        _blockedUntil.TryGetValue(nip, out var until) && until > time.GetUtcNow();

    public void Block(string nip, TimeSpan retryAfter) =>
        _blockedUntil[nip] = time.GetUtcNow() + retryAfter;

    public async Task WaitTurnAsync(string key, TimeSpan spacing, CancellationToken cancellationToken)
    {
        TimeSpan wait;
        lock (_nextTurn)
        {
            var now = time.GetUtcNow();
            var turn = _nextTurn.TryGetValue(key, out var next) && next > now ? next : now;
            _nextTurn[key] = turn + spacing;
            wait = turn - now;
        }

        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, time, cancellationToken);
    }
}
