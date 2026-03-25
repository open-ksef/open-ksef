using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace OpenKSeF.Api.Services;

public interface ISetupSessionService
{
    string CreateSession(string username, string password);
    (string Username, string Password)? RedeemSession(string token);
}

public class SetupSessionService : ISetupSessionService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(10);
    private readonly IMemoryCache _cache;

    public SetupSessionService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public string CreateSession(string username, string password)
    {
        var token = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var cacheKey = CacheKey(token);
        _cache.Set(cacheKey, (username, password), new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = SessionLifetime,
            Size = 1
        });
        return token;
    }

    public (string Username, string Password)? RedeemSession(string token)
    {
        var cacheKey = CacheKey(token);
        if (!_cache.TryGetValue(cacheKey, out (string Username, string Password) credentials))
            return null;

        _cache.Remove(cacheKey);
        return credentials;
    }

    private static string CacheKey(string token) => $"setup-session:{token}";

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
