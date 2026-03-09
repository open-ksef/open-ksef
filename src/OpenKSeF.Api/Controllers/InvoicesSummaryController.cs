using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Controllers;

[ApiController]
[Route("api/invoices")]
[Authorize]
public class InvoicesSummaryController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public InvoicesSummaryController(ApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<InvoiceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? tenantId = null,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;
        if (pageSize > 100) pageSize = 100;

        var userTenantIds = UserTenantIds();

        if (tenantId.HasValue && !await IsTenantOwnedByCurrentUser(tenantId.Value))
            return Forbid();

        var query = _db.InvoiceHeaders
            .Where(i => userTenantIds.Contains(i.TenantId));

        if (tenantId.HasValue)
            query = query.Where(i => i.TenantId == tenantId.Value);

        if (dateFrom.HasValue)
            query = query.Where(i => i.IssueDate >= dateFrom.Value);

        if (dateTo.HasValue)
            query = query.Where(i => i.IssueDate <= dateTo.Value);

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(i => i.IssueDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToInvoiceResponse())
            .ToListAsync();

        return Ok(new PagedResult<InvoiceResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    [HttpGet("by-number/{ksefInvoiceNumber}")]
    [ProducesResponseType(typeof(InvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByNumber(
        string ksefInvoiceNumber,
        [FromQuery] Guid? tenantId = null)
    {
        if (tenantId.HasValue && !await IsTenantOwnedByCurrentUser(tenantId.Value))
            return Forbid();

        var userTenantIds = UserTenantIds();

        var query = _db.InvoiceHeaders
            .Where(i => i.KSeFInvoiceNumber == ksefInvoiceNumber);

        if (tenantId.HasValue)
            query = query.Where(i => i.TenantId == tenantId.Value);
        else
            query = query.Where(i => userTenantIds.Contains(i.TenantId));

        var invoice = await query
            .Select(ToInvoiceResponse())
            .FirstOrDefaultAsync();

        return invoice is null ? NotFound() : Ok(invoice);
    }

    private IQueryable<Guid> UserTenantIds() =>
        _db.Tenants
            .Where(t => t.UserId == _currentUser.UserId)
            .Select(t => t.Id);

    private async Task<bool> IsTenantOwnedByCurrentUser(Guid tenantId) =>
        await _db.Tenants.AnyAsync(t => t.Id == tenantId && t.UserId == _currentUser.UserId);

    private static System.Linq.Expressions.Expression<Func<InvoiceHeader, InvoiceResponse>> ToInvoiceResponse() =>
        i => new InvoiceResponse(
            i.Id,
            i.KSeFInvoiceNumber,
            i.KSeFReferenceNumber,
            i.InvoiceNumber,
            i.VendorName,
            i.VendorNip,
            i.BuyerName,
            i.BuyerNip,
            i.AmountNet,
            i.AmountVat,
            i.AmountGross,
            i.Currency,
            i.IssueDate,
            i.AcquisitionDate,
            i.InvoiceType,
            i.FirstSeenAt,
            i.IsPaid,
            i.PaidAt);
}
