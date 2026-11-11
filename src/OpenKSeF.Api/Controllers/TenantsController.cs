using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TenantsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IIssuedInvoiceService _issuedInvoices;

    public TenantsController(ApplicationDbContext db, ICurrentUserService currentUser, IIssuedInvoiceService issuedInvoices)
    {
        _db = db;
        _currentUser = currentUser;
        _issuedInvoices = issuedInvoices;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<TenantResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List()
    {
        var tenants = await _db.Tenants
            .Where(t => t.UserId == _currentUser.UserId)
            .OrderBy(t => t.CreatedAt)
            .Select(t => new TenantResponse(t.Id, t.Nip, t.DisplayName, t.NotificationEmail, t.CreatedAt))
            .ToListAsync();

        return Ok(tenants);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TenantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id)
    {
        var tenant = await _db.Tenants
            .Where(t => t.Id == id && t.UserId == _currentUser.UserId)
            .Select(t => new TenantResponse(t.Id, t.Nip, t.DisplayName, t.NotificationEmail, t.CreatedAt))
            .FirstOrDefaultAsync();

        return tenant is null ? NotFound() : Ok(tenant);
    }

    [HttpPost]
    [ProducesResponseType(typeof(TenantResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateTenantRequest request)
    {
        if (!NipValidator.HasValidChecksum(request.Nip))
            return BadRequest(new { error = "Niepoprawny NIP. Wpisz 10 cyfr z poprawną sumą kontrolną." });

        var normalizedNip = NipValidator.Normalize(request.Nip);
        var now = DateTime.UtcNow;

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            UserId = _currentUser.UserId,
            Nip = normalizedNip,
            DisplayName = request.DisplayName,
            NotificationEmail = request.NotificationEmail,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Tenants.Add(tenant);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return Conflict(new { error = "Ta firma (NIP) jest już dodana do Twojego konta." });
        }

        var response = new TenantResponse(tenant.Id, tenant.Nip, tenant.DisplayName, tenant.NotificationEmail, tenant.CreatedAt);
        return CreatedAtAction(nameof(Get), new { id = tenant.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(TenantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTenantRequest request)
    {
        var tenant = await _db.Tenants
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == _currentUser.UserId);

        if (tenant is null)
            return NotFound();

        tenant.DisplayName = request.DisplayName;
        if (request.NotificationEmailSet)
            tenant.NotificationEmail = request.NotificationEmail;
        tenant.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var response = new TenantResponse(tenant.Id, tenant.Nip, tenant.DisplayName, tenant.NotificationEmail, tenant.CreatedAt);
        return Ok(response);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!await _db.OwnsTenantAsync(id, _currentUser.UserId))
            return NotFound();

        try
        {
            return await _issuedInvoices.DeleteTenantAsync(id) ? NoContent() : NotFound();
        }
        catch (InvoiceStateException e)
        {
            return Conflict(new { message = e.Message });
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase) == true;
    }
}
