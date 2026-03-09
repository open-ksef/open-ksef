using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DevicesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly INotificationService _notificationService;

    public DevicesController(
        ApplicationDbContext db,
        ICurrentUserService currentUser,
        INotificationService notificationService)
    {
        _db = db;
        _currentUser = currentUser;
        _notificationService = notificationService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<DeviceTokenResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListDevices()
    {
        var userId = _currentUser.UserId;
        var devices = await _db.DeviceTokens
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.UpdatedAt)
            .Select(d => new DeviceTokenResponse(d.Id, d.Token, d.Platform, d.TenantId, d.CreatedAt, d.UpdatedAt))
            .ToListAsync();

        return Ok(devices);
    }

    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceRequest request)
    {
        var userId = _currentUser.UserId;

        if (request.TenantId.HasValue)
        {
            var tenantExists = await _db.Tenants
                .AnyAsync(t => t.Id == request.TenantId.Value && t.UserId == userId);

            if (!tenantExists)
                return BadRequest(new { error = "Invalid tenant ID." });
        }

        var now = DateTime.UtcNow;
        var existing = await _db.DeviceTokens
            .FirstOrDefaultAsync(d => d.Token == request.Token && d.UserId == userId);

        if (existing is not null)
        {
            existing.Platform = request.Platform;
            existing.TenantId = request.TenantId;
            existing.UpdatedAt = now;
        }
        else
        {
            _db.DeviceTokens.Add(new DeviceToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Token = request.Token,
                Platform = request.Platform,
                TenantId = request.TenantId,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await _db.SaveChangesAsync();

        _ = _notificationService.SendConfirmationAsync(request.Token);

        return Ok(new { message = "Device registered." });
    }

    [HttpPost("{token}/test-notification")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TestNotification(string token)
    {
        var userId = _currentUser.UserId;
        var device = await _db.DeviceTokens
            .FirstOrDefaultAsync(d => d.Token == token && d.UserId == userId);

        if (device is null)
            return NotFound();

        var success = await _notificationService.SendTestNotificationAsync(device.Token);

        return success
            ? Ok(new { success = true })
            : Ok(new { success = false, error = "Push delivery failed — check Firebase/APNs configuration." });
    }

    [HttpDelete("{token}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unregister(string token)
    {
        var userId = _currentUser.UserId;
        var existing = await _db.DeviceTokens
            .FirstOrDefaultAsync(d => d.Token == token && d.UserId == userId);

        if (existing is null)
            return NotFound();

        _db.DeviceTokens.Remove(existing);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
