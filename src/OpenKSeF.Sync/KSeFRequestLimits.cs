using System.Collections.Concurrent;

namespace OpenKSeF.Sync;

/// <summary>
/// Per-NIP KSeF request pacing (metadata and XML share it) and XML 429 blocks until Retry-After. In memory, per process.
/// </summary>
public sealed class KSeFRequestLimits
{
    private readonly ConcurrentDictionary<string, DateTime> _blockedUntil = new();
    private readonly Dictionary<string, DateTime> _nextTurn = new();

    public bool IsBlocked(string nip) =>
        _blockedUntil.TryGetValue(nip, out var until) && until > DateTime.UtcNow;

    public void Block(string nip, TimeSpan retryAfter) =>
        _blockedUntil[nip] = DateTime.UtcNow + retryAfter;

    public async Task WaitTurnAsync(string nip, TimeSpan spacing, CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan wait;
            lock (_nextTurn)
            {
                var now = DateTime.UtcNow;
                if (!_nextTurn.TryGetValue(nip, out var next) || next <= now)
                {
                    _nextTurn[nip] = now + spacing;
                    return;
                }

                wait = next - now;
            }

            await Task.Delay(wait, cancellationToken);
        }
    }
}
