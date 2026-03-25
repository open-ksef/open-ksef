using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    private readonly ISetupSessionService _sessionService;
    private readonly ILogger<SystemSetupController> _logger;

    public SystemSetupController(
        ISystemConfigService systemConfig,
        ISystemSetupService setupService,
        ISetupSessionService sessionService,
        ILogger<SystemSetupController> logger)
    {
        _systemConfig = systemConfig;
        _setupService = setupService;
        _sessionService = sessionService;
        _logger = logger;
    }

    [HttpGet("setup-status")]
    [ProducesResponseType(typeof(SetupStatusResponse), StatusCodes.Status200OK)]
    public IActionResult GetSetupStatus()
    {
        return Ok(new SetupStatusResponse(_systemConfig.IsInitialized));
    }

    [HttpPost("setup/authenticate")]
    [EnableRateLimiting("setup-auth")]
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

        var setupToken = _sessionService.CreateSession(request.Username, request.Password);
        return Ok(new SetupAuthenticateResponse(setupToken, 600));
    }

    [HttpPost("setup/apply")]
    [EnableRateLimiting("setup-apply")]
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

        var credentials = _sessionService.RedeemSession(setupToken);
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
}
