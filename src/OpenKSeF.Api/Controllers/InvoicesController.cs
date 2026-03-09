using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Controllers;

[ApiController]
[Route("api/tenants/{tenantId:guid}/invoices")]
[Authorize]
public class InvoicesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ITransferDetailsService _transferDetails;
    private readonly IQrCodeService _qrCode;

    public InvoicesController(
        ApplicationDbContext db,
        ICurrentUserService currentUser,
        ITransferDetailsService transferDetails,
        IQrCodeService qrCode)
    {
        _db = db;
        _currentUser = currentUser;
        _transferDetails = transferDetails;
        _qrCode = qrCode;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<InvoiceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        Guid tenantId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? filterByVendor = null,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null)
    {
        if (!await VerifyTenantOwnership(tenantId))
            return Forbid();

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var query = _db.InvoiceHeaders
            .Where(i => i.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(filterByVendor))
        {
            query = query.Where(i =>
                i.VendorName.Contains(filterByVendor) ||
                i.VendorNip.Contains(filterByVendor));
        }

        if (dateFrom.HasValue)
            query = query.Where(i => i.IssueDate >= dateFrom.Value);

        if (dateTo.HasValue)
            query = query.Where(i => i.IssueDate <= dateTo.Value);

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(i => i.IssueDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new InvoiceResponse(
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
                i.PaidAt))
            .ToListAsync();

        return Ok(new PagedResult<InvoiceResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(InvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid tenantId, Guid id)
    {
        if (!await VerifyTenantOwnership(tenantId))
            return Forbid();

        var invoice = await _db.InvoiceHeaders
            .Where(i => i.Id == id && i.TenantId == tenantId)
            .Select(i => new InvoiceResponse(
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
                i.PaidAt))
            .FirstOrDefaultAsync();

        return invoice is null ? NotFound() : Ok(invoice);
    }

    [HttpGet("by-number/{ksefInvoiceNumber}")]
    [ProducesResponseType(typeof(InvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByKSeFNumber(Guid tenantId, string ksefInvoiceNumber)
    {
        if (!await VerifyTenantOwnership(tenantId))
            return Forbid();

        var invoice = await _db.InvoiceHeaders
            .Where(i => i.KSeFInvoiceNumber == ksefInvoiceNumber && i.TenantId == tenantId)
            .Select(i => new InvoiceResponse(
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
                i.PaidAt))
            .FirstOrDefaultAsync();

        return invoice is null ? NotFound() : Ok(invoice);
    }

    [HttpGet("{id:guid}/transfer")]
    [ProducesResponseType(typeof(TransferDetailsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTransferDetails(Guid tenantId, Guid id)
    {
        if (!await VerifyTenantOwnership(tenantId))
            return Forbid();

        var invoice = await _db.InvoiceHeaders
            .FirstOrDefaultAsync(i => i.Id == id && i.TenantId == tenantId);

        if (invoice is null)
            return NotFound();

        var transferData = _transferDetails.BuildTransferData(invoice);
        var transferText = _transferDetails.BuildTransferText(invoice);
        var qrBytes = _qrCode.GenerateTransferQr(transferData);
        var qrBase64 = $"data:image/png;base64,{Convert.ToBase64String(qrBytes)}";

        return Ok(new TransferDetailsResponse(
            transferData.RecipientName,
            transferData.RecipientAccount,
            transferData.RecipientNip,
            transferData.Amount,
            transferData.Currency,
            transferData.Title,
            transferText,
            qrBase64));
    }

    [HttpPatch("{id:guid}/paid")]
    [ProducesResponseType(typeof(InvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetPaid(Guid tenantId, Guid id, [FromBody] SetInvoicePaidRequest request)
    {
        if (!await VerifyTenantOwnership(tenantId))
            return Forbid();

        var invoice = await _db.InvoiceHeaders
            .FirstOrDefaultAsync(i => i.Id == id && i.TenantId == tenantId);

        if (invoice is null)
            return NotFound();

        invoice.IsPaid = request.IsPaid;
        invoice.PaidAt = request.IsPaid ? DateTime.UtcNow : null;
        invoice.LastUpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return Ok(new InvoiceResponse(
            invoice.Id,
            invoice.KSeFInvoiceNumber,
            invoice.KSeFReferenceNumber,
            invoice.InvoiceNumber,
            invoice.VendorName,
            invoice.VendorNip,
            invoice.BuyerName,
            invoice.BuyerNip,
            invoice.AmountNet,
            invoice.AmountVat,
            invoice.AmountGross,
            invoice.Currency,
            invoice.IssueDate,
            invoice.AcquisitionDate,
            invoice.InvoiceType,
            invoice.FirstSeenAt,
            invoice.IsPaid,
            invoice.PaidAt));
    }

    private async Task<bool> VerifyTenantOwnership(Guid tenantId)
    {
        return await _db.Tenants
            .AnyAsync(t => t.Id == tenantId && t.UserId == _currentUser.UserId);
    }
}
