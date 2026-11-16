using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Api.Authorization;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;
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
    private const int CreateLockKeyspace = 1263748423;
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IIssuedInvoiceService _issuedInvoices;
    private readonly TenantMembershipService _members;

    public TenantsController(ApplicationDbContext db, ICurrentUserService currentUser, IIssuedInvoiceService issuedInvoices, TenantMembershipService members)
    {
        _db = db;
        _currentUser = currentUser;
        _issuedInvoices = issuedInvoices;
        _members = members;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<TenantResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List()
    {
        var memberships = await _db.TenantMembers
            .Where(m => m.UserId == _currentUser.UserId)
            .Join(_db.Tenants, m => m.TenantId, t => t.Id, (m, t) => new { Tenant = t, Member = m })
            .OrderBy(x => x.Tenant.CreatedAt)
            .ToListAsync();

        return Ok(memberships.Select(x => TenantResponse.From(x.Tenant, x.Member)).ToList());
    }

    [HttpGet("{tenantId:guid}")]
    [TenantAccess(TenantPermissions.InvoicesRead)]
    [ProducesResponseType(typeof(TenantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid tenantId)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null)
            return NotFound();

        return Ok(TenantResponse.From(tenant, TenantAccessAttribute.Member(HttpContext)));
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
        await using var transaction = await _db.Database.BeginTransactionAsync();
        await _db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({CreateLockKeyspace}, hashtext({_currentUser.UserId}))");
        var existing = await _db.Tenants
            .Where(t => t.Nip == normalizedNip && _db.TenantMembers.Any(m => m.TenantId == t.Id && m.UserId == _currentUser.UserId))
            .OrderBy(t => t.DisplayName).ThenBy(t => t.Id)
            .Select(t => t.DisplayName)
            .ToListAsync();
        if (existing.Count > 0)
            return Conflict(new { error = $"Masz już dostęp do firmy z tym NIP-em: {(string.IsNullOrWhiteSpace(existing[0]) ? $"NIP {normalizedNip}" : existing[0])}." });

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

        var owner = new TenantMember { TenantId = tenant.Id, UserId = tenant.UserId, Role = TenantRole.Owner, CreatedAt = now };
        _db.Tenants.Add(tenant);
        _db.TenantMembers.Add(owner);

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return CreatedAtAction(nameof(Get), new { tenantId = tenant.Id }, TenantResponse.From(tenant, owner));
    }

    [HttpPut("{tenantId:guid}")]
    [TenantAccess(TenantPermissions.TenantEdit)]
    [ProducesResponseType(typeof(TenantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid tenantId, [FromBody] UpdateTenantRequest request)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null)
            return NotFound();

        tenant.DisplayName = request.DisplayName;
        if (request.NotificationEmailSet)
            tenant.NotificationEmail = request.NotificationEmail;
        tenant.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return Ok(TenantResponse.From(tenant, TenantAccessAttribute.Member(HttpContext)));
    }

    [HttpPut("{tenantId:guid}/members/me/notifications")]
    [TenantAccess(TenantPermissions.NotificationsSelf)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetMyNotifications(Guid tenantId, [FromBody] SetMemberNotificationsRequest request)
    {
        await _db.TenantMembers
            .Where(m => m.TenantId == tenantId && m.UserId == _currentUser.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.NotifyNewInvoices, request.Enabled!.Value));

        return NoContent();
    }

    [HttpGet("{tenantId:guid}/members")]
    [TenantAccess(TenantPermissions.MembersManage)]
    [ProducesResponseType(typeof(List<TenantMemberResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListMembers(Guid tenantId) => Ok(await _members.ListAsync(tenantId));

    [HttpPut("{tenantId:guid}/members/{userId}")]
    [TenantAccess(TenantPermissions.MembersManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeMemberRole(Guid tenantId, string userId, [FromBody] ChangeMemberRoleRequest request) =>
        MemberResult(await _members.ChangeRoleAsync(tenantId, _currentUser.UserId, userId, Enum.Parse<TenantRole>(request.Role!)));

    [HttpDelete("{tenantId:guid}/members/{userId}")]
    [TenantAccess(TenantPermissions.InvoicesRead)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveMember(Guid tenantId, string userId) =>
        MemberResult(await _members.RemoveAsync(tenantId, _currentUser.UserId, userId));

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

    private IActionResult MemberResult(TenantMembershipService.Outcome outcome) => outcome switch
    {
        TenantMembershipService.Outcome.Done => NoContent(),
        TenantMembershipService.Outcome.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = TenantAccessAttribute.ForbiddenMessage }),
        TenantMembershipService.Outcome.LastOwner => Conflict(new { error = TenantMembershipService.LastOwnerMessage }),
        _ => NotFound(),
    };
}
