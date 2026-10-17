namespace OpenKSeF.Domain.Models;

public enum PushResult
{
    /// <summary>Accepted by FCM/APNs (directly or via relay).</summary>
    Sent,
    /// <summary>Provider not configured or not applicable to the token's platform.</summary>
    Skipped,
    /// <summary>Delivery failed for a reason that says nothing about the token (network, outage, auth).</summary>
    Failed,
    /// <summary>FCM/APNs reported the token as unregistered or invalid; safe to delete.</summary>
    InvalidToken
}
