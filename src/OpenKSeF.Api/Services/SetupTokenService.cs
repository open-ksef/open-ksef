using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Services;

public interface ISetupTokenService
{
    Task<string> GenerateSetupTokenAsync(string userId, string? email, string? name);
    Task<ClaimsPrincipal?> ValidateAndConsumeAsync(string token);
}

public class SetupTokenService : ISetupTokenService
{
    private const string Issuer = "openksef-setup";
    private const string Audience = "openksef-mobile";
    private const string Purpose = "mobile-setup";
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    private readonly ISystemConfigService _systemConfig;
    private readonly SemaphoreSlim _keyLock = new(1, 1);
    // shortcut: in-memory jti store, single api replica; move to a DB table when the API scales out.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _usedJtis = new();
    private byte[]? _signingKey;

    public SetupTokenService(ISystemConfigService systemConfig)
    {
        _systemConfig = systemConfig;
    }

    public async Task<string> GenerateSetupTokenAsync(string userId, string? email, string? name)
    {
        var securityKey = new SymmetricSecurityKey(await GetSigningKeyAsync());
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("purpose", Purpose),
        };

        if (email is not null)
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, email));
        if (name is not null)
            claims.Add(new Claim("name", name));

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(TokenLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<ClaimsPrincipal?> ValidateAndConsumeAsync(string token)
    {
        try
        {
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                IssuerSigningKey = new SymmetricSecurityKey(await GetSigningKeyAsync()),
                ClockSkew = ClockSkew
            };

            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, validationParameters, out var validated);

            if (principal.FindFirst("purpose")?.Value != Purpose)
                return null;

            var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            if (string.IsNullOrEmpty(jti))
                return null;

            var now = DateTimeOffset.UtcNow;
            foreach (var (usedJti, expiresAt) in _usedJtis)
            {
                if (expiresAt < now)
                    _usedJtis.TryRemove(usedJti, out _);
            }

            return _usedJtis.TryAdd(jti, new DateTimeOffset(validated.ValidTo, TimeSpan.Zero).Add(ClockSkew))
                ? principal
                : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<byte[]> GetSigningKeyAsync()
    {
        if (_signingKey is not null)
            return _signingKey;

        await _keyLock.WaitAsync();
        try
        {
            if (_signingKey is not null)
                return _signingKey;

            await _systemConfig.RefreshCacheAsync();
            var keyBase64 = _systemConfig.GetValue(SystemConfigKeys.SetupTokenSigningKey);
            if (string.IsNullOrEmpty(keyBase64))
            {
                keyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                await _systemConfig.SetValueAsync(SystemConfigKeys.SetupTokenSigningKey, keyBase64);
            }

            _signingKey = Convert.FromBase64String(keyBase64);
            return _signingKey;
        }
        finally
        {
            _keyLock.Release();
        }
    }
}
