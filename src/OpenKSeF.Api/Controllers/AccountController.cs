using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    private readonly IKeycloakUserService _keycloakUserService;
    private readonly ApplicationDbContext _db;

    public AccountController(
        ICurrentUserService currentUser,
        IKeycloakUserService keycloakUserService,
        ApplicationDbContext db)
    {
        _currentUser = currentUser;
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
            Email: User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value,
            Name: User.FindFirst("name")?.Value);

        return Ok(response);
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("register")]
    [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _keycloakUserService.CreateUserAsync(
            request.Email, request.Password, request.FirstName, request.LastName);

        if (!result.Success)
        {
            return StatusCode((int)result.StatusCode, new { code = result.ErrorCode, error = result.ErrorMessage });
        }

        return Ok(new RegisterResponse("Account created successfully."));
    }
}
