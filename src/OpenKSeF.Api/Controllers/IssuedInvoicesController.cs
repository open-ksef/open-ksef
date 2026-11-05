using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Controllers;

[ApiController]
[Route("api/tenants/{tenantId:guid}/issued-invoices")]
[Authorize]
public class IssuedInvoicesController(
    ApplicationDbContext db,
    ICurrentUserService currentUser,
    IIssuedInvoiceService issuedInvoices) : ControllerBase
{
    private const int MaxFeedRows = 10_000;

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<IssuedInvoiceListItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> List(
        Guid tenantId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? month = null,
        [FromQuery] string? q = null,
        CancellationToken ct = default) =>
        GuardedAsync(tenantId, StatusCodes.Status400BadRequest, async () =>
        {
            page = Math.Max(page, 1);
            pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 100);

            var query = issuedInvoices.Query(tenantId, new IssuedInvoiceListFilter(status, month, q));
            var totalCount = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(i => i.IssueDate)
                .ThenByDescending(i => i.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(i => new IssuedInvoiceListItemResponse(
                    i.Id,
                    WireStatus(i.Status),
                    i.Number,
                    i.BuyerName,
                    i.BuyerNip,
                    i.IssueDate,
                    i.DueDate,
                    i.TotalNet,
                    i.TotalGross,
                    i.Currency,
                    i.KsefNumber,
                    i.EscalatedAt != null,
                    i.BuyerCountryCode,
                    i.BuyerIdType,
                    i.BuyerForeignId))
                .ToListAsync(ct);

            return Ok(new PagedResult<IssuedInvoiceListItemResponse> { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount });
        });

    [HttpGet("feed")]
    [ProducesResponseType(typeof(PagedResult<IssuedFeedItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Feed(
        Guid tenantId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? month = null,
        [FromQuery] string? q = null,
        CancellationToken ct = default) =>
        GuardedAsync(tenantId, StatusCodes.Status400BadRequest, async () =>
        {
            page = Math.Max(page, 1);
            pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 100);
            page = Math.Min(page, MaxFeedRows / pageSize);

            var (rows, totalCount) = await issuedInvoices.FeedAsync(tenantId, new IssuedInvoiceListFilter(status, month, q), page, pageSize, ct);
            var items = rows.Select(r => new IssuedFeedItemResponse(
                r.Id,
                r.IsExternal ? "External" : "OpenKSeF",
                WireStatus(r.Status),
                r.Number,
                r.BuyerName,
                r.BuyerNip,
                r.BuyerIdType,
                r.BuyerForeignId,
                r.BuyerCountryCode,
                r.IssueDate,
                r.DueDate,
                r.TotalNet,
                r.TotalGross,
                r.Currency,
                r.KsefNumber,
                r.NeedsAttention)).ToList();

            return Ok(new PagedResult<IssuedFeedItemResponse> { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount });
        });

    [HttpGet("exchange-rate")]
    [ProducesResponseType(typeof(ExchangeRateProposalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> ProposeExchangeRate(
        Guid tenantId,
        [FromQuery] string? currency,
        [FromQuery] DateOnly? issueDate = null,
        [FromQuery] DateOnly? saleDate = null,
        CancellationToken ct = default) =>
        GuardedAsync(tenantId, StatusCodes.Status400BadRequest, async () =>
        {
            var quote = await issuedInvoices.ProposeExchangeRateAsync(currency, issueDate, saleDate, ct);
            return Ok(new ExchangeRateProposalResponse(currency!.Trim().ToUpperInvariant(), quote?.Rate, quote?.TableDate));
        });

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Get(Guid tenantId, Guid id, CancellationToken ct) =>
        GuardedAsync(tenantId, StatusCodes.Status400BadRequest, async () =>
            await issuedInvoices.GetAsync(tenantId, id, ct) is { } view ? Ok(ToResponse(view)) : NotFound());

    [HttpPost]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Create(Guid tenantId, IssuedInvoiceDraftRequest request, CancellationToken ct) =>
        GuardedAsync(tenantId, StatusCodes.Status400BadRequest, async () =>
            CreatedInvoice(tenantId, await issuedInvoices.CreateAsync(tenantId, ToInput(request), ct)));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Update(Guid tenantId, Guid id, IssuedInvoiceDraftRequest request, [FromQuery] uint? version, CancellationToken ct) =>
        GuardedAsync(tenantId, StatusCodes.Status400BadRequest, async () =>
            await issuedInvoices.UpdateAsync(tenantId, id, ToInput(request), version, ct) is { } view ? Ok(ToResponse(view)) : NotFound());

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Delete(Guid tenantId, Guid id, [FromQuery] uint? version, CancellationToken ct) =>
        GuardedAsync(tenantId, StatusCodes.Status400BadRequest, async () =>
            await issuedInvoices.DeleteAsync(tenantId, id, version, ct) ? NoContent() : NotFound());

    [HttpPost("{id:guid}/issue")]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Issue(Guid tenantId, Guid id, [FromQuery] uint? version, CancellationToken ct) =>
        GuardedAsync(tenantId, StatusCodes.Status422UnprocessableEntity, async () =>
        {
            if (await issuedInvoices.IssueAsync(tenantId, id, version, ct) is not { } result)
                return NotFound();

            var response = ToResponse(result.View);
            return result.Issued ? StatusCode(StatusCodes.Status202Accepted, response) : Ok(response);
        });

    [HttpPost("{id:guid}/copy")]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Copy(Guid tenantId, Guid id, CancellationToken ct) =>
        GuardedAsync(tenantId, StatusCodes.Status400BadRequest, async () =>
            await issuedInvoices.CopyAsync(tenantId, id, ct) is { } view ? CreatedInvoice(tenantId, view) : NotFound());

    [HttpGet("{id:guid}/xml")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> GetXml(Guid tenantId, Guid id, CancellationToken ct) =>
        GuardedAsync(tenantId, StatusCodes.Status400BadRequest, async () =>
        {
            var document = await db.IssuedInvoices
                .Where(i => i.TenantId == tenantId && i.Id == id)
                .Select(i => new { i.Status, i.SentXml })
                .SingleOrDefaultAsync(ct);
            if (document is null)
                return NotFound();

            return document.Status == IssuedInvoiceStatus.Accepted && document.SentXml is not null
                ? File(document.SentXml, "application/xml")
                : Conflict(new { message = "XML jest dostępny po przyjęciu faktury w KSeF" });
        });

    [HttpGet("{id:guid}/upo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> GetUpo(Guid tenantId, Guid id, CancellationToken ct) =>
        GuardedAsync(tenantId, StatusCodes.Status400BadRequest, async () =>
        {
            var document = await db.IssuedInvoices
                .Where(i => i.TenantId == tenantId && i.Id == id)
                .Select(i => new { i.Status, i.UpoXml })
                .SingleOrDefaultAsync(ct);
            if (document is null)
                return NotFound();

            return document.Status == IssuedInvoiceStatus.Accepted && document.UpoXml is not null
                ? File(document.UpoXml, "application/xml")
                : Conflict(new { message = "UPO jest dostępne po przyjęciu faktury w KSeF, gdy KSeF je udostępni" });
        });

    private async Task<IActionResult> GuardedAsync(Guid tenantId, int validationStatus, Func<Task<IActionResult>> action)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId && t.UserId == currentUser.UserId, HttpContext.RequestAborted))
            return NotFound();

        try
        {
            return await action();
        }
        catch (InvoiceValidationException e)
        {
            var envelope = new ValidationEnvelope(e.Errors.Select(c => new ValidationIssue(c.Code, c.Field, c.Message)).ToList(), []);
            return StatusCode(e.Errors.Any(c => c.Code == IssueChecks.NumberPeriodCode) ? StatusCodes.Status422UnprocessableEntity : validationStatus, envelope);
        }
        catch (InvoiceStateException e)
        {
            return Conflict(new { message = e.Message });
        }
    }

    private CreatedAtActionResult CreatedInvoice(Guid tenantId, IssuedInvoiceView view) =>
        CreatedAtAction(nameof(Get), new { tenantId, id = view.Invoice.Id }, ToResponse(view));

    private static string WireStatus(IssuedInvoiceStatus status) =>
        status is IssuedInvoiceStatus.Queued or IssuedInvoiceStatus.Sending ? "Sending" : status.ToString();

    private static IssuedInvoiceDraftInput ToInput(IssuedInvoiceDraftRequest request) => new(
        request.Buyer is { } buyer ? new IssuedInvoiceBuyerInput(buyer.Kind, buyer.Nip, buyer.Name, buyer.AddressLine1, buyer.AddressLine2, buyer.CountryCode, buyer.ForeignId) : null,
        request.IssueDate,
        request.SaleDate,
        request.DueDate,
        request.PaymentMethod,
        request.SplitPayment,
        request.ExemptionBasis?.Kind,
        request.ExemptionBasis?.Text,
        request.Lines?.Select(l => new IssuedInvoiceLineInput(l.Name, l.Unit, l.Quantity, l.UnitNetPrice, l.VatRate)).ToList(),
        request.Currency,
        request.ExchangeRate,
        request.ExchangeRateDate);

    private static IssuedInvoiceResponse ToResponse(IssuedInvoiceView view)
    {
        var invoice = view.Invoice;
        return new IssuedInvoiceResponse(
            invoice.Id,
            WireStatus(invoice.Status),
            invoice.Number,
            new IssuedInvoiceSellerResponse(view.Seller.Nip, view.Seller.Name, view.Seller.AddressLine1, view.Seller.AddressLine2, view.Seller.BankAccount, view.Seller.CashAccounting,
                view.Seller.Iban, view.Seller.Swift, view.Seller.BankName),
            new IssuedInvoiceBuyerModel(invoice.BuyerKind.ToString(), invoice.BuyerNip, invoice.BuyerName, invoice.BuyerAddressLine1, invoice.BuyerAddressLine2,
                invoice.BuyerCountryCode, invoice.BuyerIdType, invoice.BuyerForeignId),
            invoice.IssueDate,
            invoice.SaleDate,
            invoice.DueDate,
            invoice.PaymentMethod.ToString(),
            invoice.SplitPayment,
            invoice.ExemptionBasisKind is { } kind ? new ExemptionBasisModel(kind.ToString(), invoice.ExemptionBasisText) : null,
            invoice.Lines.OrderBy(l => l.LineNo)
                .Select(l => new IssuedInvoiceLineResponse(l.LineNo, l.Name, l.Unit, l.Quantity, l.UnitNetPrice, l.VatRate.Code(), l.NetAmount, l.VatAmount))
                .ToList(),
            invoice.VatSummary().Select(s => new VatSummaryResponse(s.VatRate.Code(), s.Net, s.Vat)).ToList(),
            invoice.TotalNet,
            invoice.TotalVat,
            invoice.TotalGross,
            invoice.Currency,
            new IssuedInvoiceKsefResponse(
                view.KsefEnvironment,
                invoice.KsefNumber,
                invoice.AcceptedAt,
                invoice.KsefStatusCode,
                invoice.KsefStatusDescription,
                invoice.KsefStatusDetails,
                invoice.LastError,
                invoice.Status == IssuedInvoiceStatus.Accepted ? invoice.VerificationUrl : null,
                invoice.UpoXml is not null,
                invoice.DuplicateKsefNumber),
            view.Warnings.Select(w => new ValidationIssue(w.Code, w.Field, w.Message)).ToList(),
            invoice.CreatedAt,
            invoice.UpdatedAt,
            invoice.IssuedAt,
            invoice.ExchangeRate,
            invoice.ExchangeRateDate,
            view.Version);
    }
}
