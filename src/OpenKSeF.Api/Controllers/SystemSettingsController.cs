using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Controllers;

[ApiController]
[Route("api/system/settings")]
[Authorize]
public class SystemSettingsController : ControllerBase
{
    private readonly ISystemConfigService _systemConfig;
    private readonly ISystemSetupService _setupService;
    private readonly ISystemSettingsService _settingsService;
    private readonly ILogger<SystemSettingsController> _logger;

    public SystemSettingsController(
        ISystemConfigService systemConfig,
        ISystemSetupService setupService,
        ISystemSettingsService settingsService,
        ILogger<SystemSettingsController> logger)
    {
        _systemConfig = systemConfig;
        _setupService = setupService;
        _settingsService = settingsService;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(SettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSettings(
        [FromBody] SettingsAuthRequest request,
        CancellationToken ct)
    {
        if (!_systemConfig.IsInitialized)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "System is not initialized." });

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var kcAdminToken = await _setupService.AuthenticateAdminAsync(request.KcAdminUsername, request.KcAdminPassword);
        if (kcAdminToken is null)
            return Unauthorized(new { error = "Invalid Keycloak admin credentials." });

        var settings = await _settingsService.GetSettingsAsync(kcAdminToken, ct);
        return Ok(settings);
    }

    [HttpPut]
    [ProducesResponseType(typeof(SettingsUpdateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] SettingsUpdateRequest request,
        CancellationToken ct)
    {
        if (!_systemConfig.IsInitialized)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "System is not initialized." });

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var kcAdminToken = await _setupService.AuthenticateAdminAsync(request.KcAdminUsername, request.KcAdminPassword);
        if (kcAdminToken is null)
            return Unauthorized(new { error = "Invalid Keycloak admin credentials." });

        var result = await _settingsService.UpdateSettingsAsync(request, kcAdminToken, ct);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }
}
