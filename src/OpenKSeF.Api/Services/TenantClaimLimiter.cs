namespace OpenKSeF.Api.Services;

/// <summary>Company claim attempts that would reach KSeF (REQ-0054): 5 per hour per user and 10 per day per NIP; both are checked before either is counted.</summary>
public sealed class TenantClaimLimiter(TimeProvider time)
{
    public const int UserLimit = 5;
    public const int NipLimit = 10;
    public static readonly TimeSpan UserWindow = TimeSpan.FromHours(1);
    public static readonly TimeSpan NipWindow = TimeSpan.FromDays(1);

    // shortcut: in API memory, reset on restart, one replica only (REQ assumption); move to a table if the API scales out.
    private readonly Dictionary<string, Queue<DateTimeOffset>> _users = [];
    private readonly Dictionary<string, Queue<DateTimeOffset>> _nips = [];

    public bool TryAcquire(string userId, string nip)
    {
        var now = time.GetUtcNow();
        lock (_users)
        {
            var user = Recent(_users, userId, now - UserWindow);
            var perNip = Recent(_nips, nip, now - NipWindow);
            if (user.Count >= UserLimit || perNip.Count >= NipLimit)
                return false;

            user.Enqueue(now);
            perNip.Enqueue(now);
            return true;
        }
    }

    private static Queue<DateTimeOffset> Recent(Dictionary<string, Queue<DateTimeOffset>> attempts, string key, DateTimeOffset since)
    {
        if (!attempts.TryGetValue(key, out var queue))
            attempts[key] = queue = new Queue<DateTimeOffset>();
        while (queue.Count > 0 && queue.Peek() <= since)
            queue.Dequeue();
        return queue;
    }
}
