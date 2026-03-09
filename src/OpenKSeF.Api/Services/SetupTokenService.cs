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

    public SetupTokenService(IConfiguration configuration)
    {
        var keyBase64 = configuration["ENCRYPTION_KEY"];
        if (string.IsNullOrEmpty(keyBase64))
            _signingKey = Encoding.UTF8.GetBytes("dev-setup-token-key-must-be-32b!");
        else
            _signingKey = Convert.FromBase64String(keyBase64);

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
