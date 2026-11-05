using System.Collections.Concurrent;

namespace OpenKSeF.Sync;

/// <summary>
/// NIPs that got a KSeF 429 on invoice XML and wait for Retry-After. In memory, per process.
/// </summary>
public sealed class XmlDownloadBlocks
{
    private readonly ConcurrentDictionary<string, DateTime> _blockedUntil = new();

    public bool IsBlocked(string nip) =>
        _blockedUntil.TryGetValue(nip, out var until) && until > DateTime.UtcNow;

    public void Block(string nip, TimeSpan retryAfter) =>
        _blockedUntil[nip] = DateTime.UtcNow + retryAfter;
}
