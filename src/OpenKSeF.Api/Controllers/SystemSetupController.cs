using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Controllers;

[ApiController]
[Route("api/system")]
[AllowAnonymous]
public class SystemSetupController : ControllerBase
{
    private readonly ISystemConfigService _systemConfig;
    private readonly ISystemSetupService _setupService;
    private readonly ILogger<SystemSetupController> _logger;
    private readonly byte[] _setupTokenSigningKey;

    private static readonly ConcurrentDictionary<string, DateTime> UsedTokens = new();

    public SystemSetupController(
        ISystemConfigService systemConfig,
        ISystemSetupService setupService,
        IConfiguration configuration,
        ILogger<SystemSetupController> logger)
    {
        _systemConfig = systemConfig;
        _setupService = setupService;
        _logger = logger;

        var keyBase64 = configuration["ENCRYPTION_KEY"];
        _setupTokenSigningKey = string.IsNullOrEmpty(keyBase64)
            ? Encoding.UTF8.GetBytes("dev-setup-token-key-must-be-32b!")
            : Convert.FromBase64String(keyBase64);
    }

    [HttpGet("setup-status")]
    [ProducesResponseType(typeof(SetupStatusResponse), StatusCodes.Status200OK)]
    public IActionResult GetSetupStatus()
    {
        return Ok(new SetupStatusResponse(_systemConfig.IsInitialized));
    }

    [HttpPost("setup/authenticate")]
    [ProducesResponseType(typeof(SetupAuthenticateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Authenticate([FromBody] SetupAuthenticateRequest request)
    {
        if (_systemConfig.IsInitialized)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "System is already initialized." });

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var kcAdminToken = await _setupService.AuthenticateAdminAsync(request.Username, request.Password);
        if (kcAdminToken is null)
            return BadRequest(new { error = "Invalid Keycloak admin credentials." });

        var setupToken = GenerateSetupToken(request.Username, request.Password);
        return Ok(new SetupAuthenticateResponse(setupToken, 600));
    }

    [HttpPost("setup/apply")]
    [ProducesResponseType(typeof(SetupApplyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Apply(
        [FromHeader(Name = "X-Setup-Token")] string? setupToken,
        [FromBody] SetupApplyRequest request,
        CancellationToken ct)
    {
        if (_systemConfig.IsInitialized)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "System is already initialized." });

        if (string.IsNullOrEmpty(setupToken))
            return BadRequest(new { error = "X-Setup-Token header is required." });

        var credentials = ValidateSetupToken(setupToken);
        if (credentials is null)
            return BadRequest(new { error = "Invalid or expired setup token." });

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var kcAdminToken = await _setupService.AuthenticateAdminAsync(credentials.Value.Username, credentials.Value.Password);
        if (kcAdminToken is null)
            return BadRequest(new { error = "Failed to authenticate with Keycloak. Please restart the setup." });

        var result = await _setupService.ApplySetupAsync(request, kcAdminToken, credentials.Value.Username, ct);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    private string GenerateSetupToken(string username, string password)
    {
        var key = new SymmetricSecurityKey(_setupTokenSigningKey);
        var signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("purpose", "admin-setup"),
            new Claim("kc_user", username),
            new Claim("kc_pass", password),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: "openksef-setup",
            audience: "openksef-setup",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private (string Username, string Password)? ValidateSetupToken(string token)
    {
        var key = new SymmetricSecurityKey(_setupTokenSigningKey);
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "openksef-setup",
            ValidateAudience = true,
            ValidAudience = "openksef-setup",
            ValidateLifetime = true,
            IssuerSigningKey = key,
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, validationParameters, out _);

            var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            if (jti != null && !UsedTokens.TryAdd(jti, DateTime.UtcNow))
                return null;

            CleanupExpiredTokens();

            var username = principal.FindFirst("kc_user")?.Value;
            var password = principal.FindFirst("kc_pass")?.Value;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return null;

            return (username, password);
        }
        catch
        {
            return null;
        }
    }

    private static void CleanupExpiredTokens()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-15);
        foreach (var kvp in UsedTokens)
        {
            if (kvp.Value < cutoff)
                UsedTokens.TryRemove(kvp.Key, out _);
        }
    }
}
