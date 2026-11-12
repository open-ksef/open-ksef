using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace OpenKSeF.Api.Services;

public interface ISetupSessionService
{
    string CreateSession(string username, string password);
    (string Username, string Password, DateTimeOffset ExpiresAt)? RedeemSession(string token);
    void RestoreSession(string token, (string Username, string Password, DateTimeOffset ExpiresAt) session);
}

public class SetupSessionService : ISetupSessionService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new();

    private record SessionEntry(string Username, string Password, DateTimeOffset ExpiresAt);

    public string CreateSession(string username, string password)
    {
        PurgeExpired();
        var token = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        _sessions[token] = new SessionEntry(username, password, DateTimeOffset.UtcNow.Add(SessionLifetime));
        return token;
    }

    public (string Username, string Password, DateTimeOffset ExpiresAt)? RedeemSession(string token)
    {
        // TryRemove is atomic — only one concurrent caller succeeds, preventing replay races.
        if (!_sessions.TryRemove(token, out var entry))
            return null;

        if (DateTimeOffset.UtcNow > entry.ExpiresAt)
            return null;

        return (entry.Username, entry.Password, entry.ExpiresAt);
    }

    public void RestoreSession(string token, (string Username, string Password, DateTimeOffset ExpiresAt) session) =>
        _sessions[token] = new SessionEntry(session.Username, session.Password, session.ExpiresAt);

    private void PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _sessions)
        {
            if (kvp.Value.ExpiresAt < now)
                _sessions.TryRemove(kvp.Key, out _);
        }
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
