namespace OpenKSeF.Api.Services;

/// <summary>Setup step 1 limit: 5 attempts per 15 minutes per IP; an attempt that never reached Keycloak is given back.</summary>
public sealed class SetupAuthAttemptLimiter(TimeProvider time)
{
    public const int PermitLimit = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    // shortcut: one entry per IP until restart, bounded because the endpoint only works before setup completes.
    private readonly Dictionary<string, (DateTimeOffset Start, int Count)> _attempts = [];

    public bool TryAcquire(string ip, out TimeSpan retryAfter)
    {
        var now = time.GetUtcNow();
        lock (_attempts)
        {
            if (!_attempts.TryGetValue(ip, out var entry) || now - entry.Start >= Window)
                entry = (now, 0);

            retryAfter = entry.Start + Window - now;
            if (entry.Count >= PermitLimit)
                return false;

            _attempts[ip] = (entry.Start, entry.Count + 1);
            return true;
        }
    }

    public void Release(string ip)
    {
        lock (_attempts)
        {
            if (_attempts.TryGetValue(ip, out var entry) && entry.Count > 0)
                _attempts[ip] = (entry.Start, entry.Count - 1);
        }
    }
}
