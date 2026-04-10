using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace OpenKSeF.Api.Services;

public interface ISetupTokenService
{
    string GenerateSetupToken(string userId, string? email, string? name);
    ClaimsPrincipal? ValidateSetupToken(string token);
}

public class SetupTokenService : ISetupTokenService
{
    private readonly byte[] _signingKey;
    private readonly string _issuer;
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);

    private const string DevFallbackKey = "dev-setup-token-key-must-be-32b!";

    public SetupTokenService(IConfiguration configuration, IHostEnvironment environment, ILogger<SetupTokenService> logger)
    {
        var keyBase64 = configuration["ENCRYPTION_KEY"];
        if (string.IsNullOrEmpty(keyBase64))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException(
                    "ENCRYPTION_KEY is not configured. Run the admin setup wizard or set the ENCRYPTION_KEY environment variable. " +
                    "Generate one with: openssl rand -base64 32");

            _signingKey = Encoding.UTF8.GetBytes(DevFallbackKey);
            logger.LogWarning(
                "ENCRYPTION_KEY not set — using dev fallback key for setup tokens. Set ENCRYPTION_KEY before exposing this instance.");
        }
        else
        {
            _signingKey = Convert.FromBase64String(keyBase64);
        }

        _issuer = "openksef-setup";
    }

    public string GenerateSetupToken(string userId, string? email, string? name)
    {
        var securityKey = new SymmetricSecurityKey(_signingKey);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("purpose", "mobile-setup"),
        };

        if (email is not null)
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, email));
        if (name is not null)
            claims.Add(new Claim("name", name));

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: "openksef-mobile",
            claims: claims,
            expires: DateTime.UtcNow.Add(TokenLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ClaimsPrincipal? ValidateSetupToken(string token)
    {
        var securityKey = new SymmetricSecurityKey(_signingKey);
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _issuer,
            ValidateAudience = true,
            ValidAudience = "openksef-mobile",
            ValidateLifetime = true,
            IssuerSigningKey = securityKey,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            return handler.ValidateToken(token, validationParameters, out _);
        }
        catch
        {
            return null;
        }
    }
}
