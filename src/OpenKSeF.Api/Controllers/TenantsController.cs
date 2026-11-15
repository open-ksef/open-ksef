using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Api.Authorization;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
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
        var memberships = await _db.TenantMembers
            .Where(m => m.UserId == _currentUser.UserId)
            .Join(_db.Tenants, m => m.TenantId, t => t.Id, (m, t) => new { Tenant = t, m.Role })
            .OrderBy(x => x.Tenant.CreatedAt)
            .ToListAsync();

        return Ok(memberships.Select(x => TenantResponse.From(x.Tenant, x.Role)).ToList());
    }

    [HttpGet("{tenantId:guid}")]
    [TenantAccess(TenantPermissions.InvoicesRead)]
    [ProducesResponseType(typeof(TenantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid tenantId)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId);
        return Ok(TenantResponse.From(tenant, TenantAccessAttribute.Member(HttpContext).Role));
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
        _db.TenantMembers.Add(new TenantMember { TenantId = tenant.Id, UserId = tenant.UserId, Role = TenantRole.Owner, CreatedAt = now });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return Conflict(new { error = "Ta firma (NIP) jest już dodana do Twojego konta." });
        }

        return CreatedAtAction(nameof(Get), new { tenantId = tenant.Id }, TenantResponse.From(tenant, TenantRole.Owner));
    }

    [HttpPut("{tenantId:guid}")]
    [TenantAccess(TenantPermissions.TenantEdit)]
    [ProducesResponseType(typeof(TenantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid tenantId, [FromBody] UpdateTenantRequest request)
    {
        var tenant = await _db.Tenants.FirstAsync(t => t.Id == tenantId);

        tenant.DisplayName = request.DisplayName;
        if (request.NotificationEmailSet)
            tenant.NotificationEmail = request.NotificationEmail;
        tenant.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return Ok(TenantResponse.From(tenant, TenantAccessAttribute.Member(HttpContext).Role));
    }

    [HttpDelete("{tenantId:guid}")]
    [TenantAccess(TenantPermissions.TenantDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid tenantId)
    {
        try
        {
            return await _issuedInvoices.DeleteTenantAsync(tenantId) ? NoContent() : NotFound();
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
