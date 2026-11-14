using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Controllers;

[ApiController]
[Route("api/system/users")]
[Authorize(Policy = SystemSettingsController.AdminPolicy)]
[EnableRateLimiting(SystemSettingsController.RateLimitPolicy)]
public class SystemUsersController : ControllerBase
{
    private readonly IKeycloakUserService _keycloakUserService;
    private readonly ICurrentUserService _currentUser;

    public SystemUsersController(IKeycloakUserService keycloakUserService, ICurrentUserService currentUser)
    {
        _keycloakUserService = keycloakUserService;
        _currentUser = currentUser;
    }

    [HttpGet("two-factor")]
    [ProducesResponseType(typeof(TwoFactorUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetTwoFactor([FromQuery, Required] string email)
    {
        var result = await _keycloakUserService.GetTwoFactorAsync(email);
        if (result.KeycloakUnavailable)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "keycloak_unavailable" });
        if (result.UserNotFound)
            return NotFound(new { error = "user_not_found" });
        return Ok(result.User);
    }

    [HttpPost("{userId}/two-factor/reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ResetTwoFactor(string userId)
    {
        var result = await _keycloakUserService.ResetTwoFactorAsync(
            userId, _currentUser.UserId, User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value);
        return result switch
        {
            TwoFactorResetResult.Reset => NoContent(),
            TwoFactorResetResult.UserNotFound => NotFound(new { error = "user_not_found" }),
            TwoFactorResetResult.ResetUnavailable => Conflict(new { error = "reset_unavailable" }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "keycloak_unavailable" }),
        };
    }
}
