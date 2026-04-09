using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.DTOs;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;
using System.Linq;

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
    private readonly ISyncedInvoiceMapper _invoiceMapper;

    public InvoicesController(
        ApplicationDbContext db,
        ICurrentUserService currentUser,
        ITransferDetailsService transferDetails,
        IQrCodeService qrCode,
        ISyncedInvoiceMapper invoiceMapper)
    {
        _db = db;
        _currentUser = currentUser;
        _transferDetails = transferDetails;
        _qrCode = qrCode;
        _invoiceMapper = invoiceMapper;
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
                i.PaidAt,
                null))
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
            .Include(i => i.Lines)
            .Where(i => i.Id == id && i.TenantId == tenantId)
            .FirstOrDefaultAsync();

        return invoice is null ? NotFound() : Ok(ToResponse(invoice.Id, invoice.FirstSeenAt, invoice.IsPaid, invoice.PaidAt, _invoiceMapper.ToDto(invoice)));
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
            .Include(i => i.Lines)
            .Where(i => i.KSeFInvoiceNumber == ksefInvoiceNumber && i.TenantId == tenantId)
            .FirstOrDefaultAsync();

        return invoice is null ? NotFound() : Ok(ToResponse(invoice.Id, invoice.FirstSeenAt, invoice.IsPaid, invoice.PaidAt, _invoiceMapper.ToDto(invoice)));
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

        await _db.Entry(invoice).Collection(i => i.Lines).LoadAsync();

        return Ok(ToResponse(invoice.Id, invoice.FirstSeenAt, invoice.IsPaid, invoice.PaidAt, _invoiceMapper.ToDto(invoice)));
    }

    private async Task<bool> VerifyTenantOwnership(Guid tenantId)
    {
        return await _db.Tenants
            .AnyAsync(t => t.Id == tenantId && t.UserId == _currentUser.UserId);
    }

    private static InvoiceResponse ToResponse(Guid id, DateTime firstSeenAt, bool isPaid, DateTime? paidAt, InvoiceDto dto) => new(
        id,
        dto.Number,
        dto.ReferenceNumber,
        dto.InvoiceNumber,
        dto.VendorName,
        dto.VendorNip,
        dto.BuyerName,
        dto.BuyerNip,
        dto.AmountNet,
        dto.AmountVat,
        dto.AmountGross,
        dto.Currency ?? "PLN",
        dto.IssueDate,
        dto.AcquisitionDate,
        dto.InvoiceType,
        firstSeenAt,
        isPaid,
        paidAt,
        Lines: dto.Lines?
            .Select(l => new InvoiceLineResponse(
                l.LineNumber, l.Name, l.Unit, l.Quantity,
                l.UnitPriceNet, l.UnitPriceGross,
                l.AmountNet, l.AmountGross, l.AmountVat, l.VatRate))
            .ToList());
}
