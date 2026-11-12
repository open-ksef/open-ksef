using System.Globalization;
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
    private readonly SetupAuthAttemptLimiter _authLimiter;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SystemSetupController> _logger;

    public SystemSetupController(
        ISystemConfigService systemConfig,
        ISystemSetupService setupService,
        ISetupSessionService sessionService,
        SetupAuthAttemptLimiter authLimiter,
        IConfiguration configuration,
        ILogger<SystemSetupController> logger)
    {
        _systemConfig = systemConfig;
        _setupService = setupService;
        _sessionService = sessionService;
        _authLimiter = authLimiter;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("setup-status")]
    [ProducesResponseType(typeof(SetupStatusResponse), StatusCodes.Status200OK)]
    public IActionResult GetSetupStatus()
    {
        return Ok(new SetupStatusResponse(
            _systemConfig.IsInitialized,
            !string.Equals(_systemConfig.GetValue(SystemConfigKeys.RegistrationAllowed), "false", StringComparison.OrdinalIgnoreCase)));
    }

    [HttpPost("setup/authenticate")]
    [ProducesResponseType(typeof(SetupAuthenticateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Authenticate([FromBody] SetupAuthenticateRequest request)
    {
        if (_systemConfig.IsInitialized)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "System is already initialized." });

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!_authLimiter.TryAcquire(ip, out var windowStart, out var retryAfter))
        {
            Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            return StatusCode(StatusCodes.Status429TooManyRequests, new { code = "rate_limited", error = "Zbyt wiele prób. Spróbuj ponownie później." });
        }

        var (kcAdminToken, unreachable) = await _setupService.AuthenticateAdminAsync(request.Username, request.Password);
        if (kcAdminToken is null)
        {
            if (unreachable)
                _authLimiter.Release(ip, windowStart);
            return BadRequest(new { error = AuthFailureMessage(unreachable) });
        }

        if (!SystemSetupService.HasUsableEncryptionKey(_configuration))
            return BadRequest(new { error = SystemSetupService.EncryptionKeyMissingMessage, code = SystemSetupService.EncryptionKeyMissingCode });

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

        var (kcAdminToken, unreachable) = await _setupService.AuthenticateAdminAsync(credentials.Value.Username, credentials.Value.Password);
        if (kcAdminToken is null)
        {
            if (unreachable)
                _sessionService.RestoreSession(setupToken, credentials.Value);
            return BadRequest(new SetupApplyResponse(false, AuthFailureMessage(unreachable)));
        }

        var result = await _setupService.ApplySetupAsync(request, kcAdminToken, credentials.Value.Username, ct);
        if (!result.Success)
        {
            _sessionService.RestoreSession(setupToken, credentials.Value);
            return BadRequest(result);
        }

        return Ok(result);
    }

    private static string AuthFailureMessage(bool unreachable) => unreachable
        ? SystemSetupService.KeycloakUnreachableMessage
        : SystemSetupService.KeycloakInvalidCredentialsMessage;
}
