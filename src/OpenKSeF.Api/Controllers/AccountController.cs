using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AccountController : ControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly ISetupTokenService _setupTokenService;
    private readonly IKeycloakTokenExchangeService _tokenExchangeService;
    private readonly IKeycloakUserService _keycloakUserService;
    private readonly ApplicationDbContext _db;

    public AccountController(
        ICurrentUserService currentUser,
        ISetupTokenService setupTokenService,
        IKeycloakTokenExchangeService tokenExchangeService,
        IKeycloakUserService keycloakUserService,
        ApplicationDbContext db)
    {
        _currentUser = currentUser;
        _setupTokenService = setupTokenService;
        _tokenExchangeService = tokenExchangeService;
        _keycloakUserService = keycloakUserService;
        _db = db;
    }

    [HttpGet("onboarding-status")]
    [ProducesResponseType(typeof(OnboardingStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetOnboardingStatus()
    {
        var userId = _currentUser.UserId;

        var firstTenant = await _db.Tenants
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.CreatedAt)
            .Select(t => new { t.Id, HasCredential = t.KSeFCredentials.Any() })
            .FirstOrDefaultAsync();

        var hasTenant = firstTenant is not null;
        var hasCredential = firstTenant?.HasCredential ?? false;

        return Ok(new OnboardingStatusResponse(
            IsComplete: hasTenant,
            HasTenant: hasTenant,
            HasCredential: hasCredential,
            FirstTenantId: firstTenant?.Id.ToString()));
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(UserInfoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetMe()
    {
        var response = new UserInfoResponse(
            UserId: _currentUser.UserId,
            Email: User.FindFirst("email")?.Value,
            Name: User.FindFirst("name")?.Value);

        return Ok(response);
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _keycloakUserService.CreateUserAsync(
            request.Email, request.Password, request.FirstName, request.LastName);

        if (!result.Success)
        {
            return StatusCode((int)result.StatusCode, new { error = result.ErrorMessage });
        }

        return Ok(new RegisterResponse("Account created successfully."));
    }

    [HttpPost("setup-token")]
    [ProducesResponseType(typeof(SetupTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GenerateSetupToken()
    {
        var userId = _currentUser.UserId;
        var email = User.FindFirst("email")?.Value;
        var name = User.FindFirst("name")?.Value;

        var token = _setupTokenService.GenerateSetupToken(userId, email, name);

        return Ok(new SetupTokenResponse(token, ExpiresInSeconds: 300));
    }

    [HttpPost("redeem-setup-token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(RedeemTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RedeemSetupToken([FromBody] RedeemSetupTokenRequest request)
    {
        var principal = _setupTokenService.ValidateSetupToken(request.SetupToken);
        if (principal is null)
            return BadRequest(new { error = "Invalid or expired setup token." });

        var userId = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                     ?? principal.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
            return BadRequest(new { error = "Setup token missing subject." });

        var result = await _tokenExchangeService.ExchangeSetupTokenAsync(userId);
        if (result is null)
            return BadRequest(new { error = "Token exchange failed. Try logging in manually." });

        return Ok(new RedeemTokenResponse(
            result.AccessToken,
            result.RefreshToken,
            result.ExpiresIn));
    }
}
