using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Api.Authorization;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Controllers;

[ApiController]
[Route("api/tenants/{tenantId:guid}/issued-invoices")]
[Authorize]
public class IssuedInvoicesController(
    ApplicationDbContext db,
    IIssuedInvoiceService issuedInvoices) : ControllerBase
{
    private const int MaxFeedRows = 10_000;

    [HttpGet]
    [TenantAccess(TenantPermissions.InvoicesRead)]
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
        [FromQuery] string? invoiceType = null,
        CancellationToken ct = default) =>
        GuardedAsync(StatusCodes.Status400BadRequest, async () =>
        {
            page = Math.Max(page, 1);
            pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 100);

            var query = issuedInvoices.Query(tenantId, new IssuedInvoiceListFilter(status, month, q, invoiceType));
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
                    i.BuyerForeignId,
                    i.InvoiceType.Code()))
                .ToListAsync(ct);

            return Ok(new PagedResult<IssuedInvoiceListItemResponse> { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount });
        });

    [HttpGet("feed")]
    [TenantAccess(TenantPermissions.InvoicesRead)]
    [ProducesResponseType(typeof(IssuedFeedPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Feed(
        Guid tenantId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? month = null,
        [FromQuery] string? q = null,
        [FromQuery] string? invoiceType = null,
        CancellationToken ct = default) =>
        GuardedAsync(StatusCodes.Status400BadRequest, async () =>
        {
            page = Math.Max(page, 1);
            pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 100);
            page = Math.Min(page, MaxFeedRows / pageSize);

            var (rows, totalCount) = await issuedInvoices.FeedAsync(tenantId, new IssuedInvoiceListFilter(status, month, q, invoiceType), page, pageSize, ct);
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
                r.NeedsAttention,
                r.InvoiceType)).ToList();

            var syncState = await db.SyncStates.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

            return Ok(new IssuedFeedPageResponse
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                IssuedFetchIncomplete = syncState?.IssuedFetchIncomplete() ?? false
            });
        });

    [HttpGet("exchange-rate")]
    [TenantAccess(TenantPermissions.InvoicesRead)]
    [ProducesResponseType(typeof(ExchangeRateProposalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> ProposeExchangeRate(
        Guid tenantId,
        [FromQuery] string? currency,
        [FromQuery] DateOnly? issueDate = null,
        [FromQuery] DateOnly? saleDate = null,
        CancellationToken ct = default) =>
        GuardedAsync(StatusCodes.Status400BadRequest, async () =>
        {
            var quote = await issuedInvoices.ProposeExchangeRateAsync(currency, issueDate, saleDate, ct);
            return Ok(new ExchangeRateProposalResponse(currency!.Trim().ToUpperInvariant(), quote?.Rate, quote?.TableDate));
        });

    [HttpGet("recent-buyers")]
    [TenantAccess(TenantPermissions.IssuingDraft)]
    [ProducesResponseType(typeof(IReadOnlyList<RecentBuyerResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> RecentBuyers(Guid tenantId, [FromQuery] string? q = null, CancellationToken ct = default) =>
        GuardedAsync(StatusCodes.Status400BadRequest, async () =>
            Ok((await issuedInvoices.RecentBuyersAsync(tenantId, q, ct))
                .Select(b => new RecentBuyerResponse(
                    new IssuedInvoiceBuyerModel(b.Kind.ToString(), b.Nip, b.Name, b.AddressLine1, b.AddressLine2, b.CountryCode, b.IdType, b.ForeignId),
                    b.LastIssueDate))
                .ToList()));

    [HttpGet("line-suggestions")]
    [TenantAccess(TenantPermissions.IssuingDraft)]
    [ProducesResponseType(typeof(IReadOnlyList<LineSuggestionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> LineSuggestions(Guid tenantId, [FromQuery] string? q = null, [FromQuery] string? buyerKind = null, CancellationToken ct = default) =>
        GuardedAsync(StatusCodes.Status400BadRequest, async () =>
            Ok((await issuedInvoices.LineSuggestionsAsync(tenantId, q, buyerKind, ct))
                .Select(l => new LineSuggestionResponse(l.Name, l.Unit, l.UnitNetPrice, l.VatRate.Code()))
                .ToList()));

    [HttpGet("templates")]
    [TenantAccess(TenantPermissions.IssuingDraft)]
    [ProducesResponseType(typeof(IReadOnlyList<InvoiceTemplateResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> ListTemplates(Guid tenantId, CancellationToken ct) =>
        GuardedAsync(StatusCodes.Status400BadRequest, async () =>
            Ok((await issuedInvoices.ListTemplatesAsync(tenantId, ct)).Select(TemplateResponse).ToList()));

    [HttpPost("{id:guid}/template")]
    [TenantAccess(TenantPermissions.IssuingDraft)]
    [ProducesResponseType(typeof(InvoiceTemplateResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> CreateTemplate(Guid tenantId, Guid id, InvoiceTemplateRequest request, CancellationToken ct) =>
        GuardedAsync(StatusCodes.Status400BadRequest, async () =>
            await issuedInvoices.CreateTemplateAsync(tenantId, id, request.Name, ct) is { } template
                ? StatusCode(StatusCodes.Status201Created, TemplateResponse(template))
                : NotFound());

    [HttpPut("templates/{id:guid}")]
    [TenantAccess(TenantPermissions.IssuingDraft)]
    [ProducesResponseType(typeof(InvoiceTemplateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> RenameTemplate(Guid tenantId, Guid id, InvoiceTemplateRequest request, CancellationToken ct) =>
        GuardedAsync(StatusCodes.Status400BadRequest, async () =>
            await issuedInvoices.RenameTemplateAsync(tenantId, id, request.Name, ct) is { } template ? Ok(TemplateResponse(template)) : NotFound());

    [HttpDelete("templates/{id:guid}")]
    [TenantAccess(TenantPermissions.IssuingDraft)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> DeleteTemplate(Guid tenantId, Guid id, CancellationToken ct) =>
        GuardedAsync(StatusCodes.Status400BadRequest, async () =>
            await issuedInvoices.DeleteTemplateAsync(tenantId, id, ct) ? NoContent() : NotFound());

    [HttpPost("templates/{id:guid}/draft")]
    [TenantAccess(TenantPermissions.IssuingDraft)]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> DraftFromTemplate(Guid tenantId, Guid id, CancellationToken ct) =>
        GuardedAsync(StatusCodes.Status400BadRequest, async () =>
            await issuedInvoices.DraftFromTemplateAsync(tenantId, id, ct) is { } view ? CreatedInvoice(tenantId, view) : NotFound());

    [HttpGet("{id:guid}")]
    [TenantAccess(TenantPermissions.InvoicesRead)]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Get(Guid tenantId, Guid id, CancellationToken ct) =>
        GuardedAsync(StatusCodes.Status400BadRequest, async () =>
            await issuedInvoices.GetAsync(tenantId, id, ct) is { } view ? Ok(ToResponse(view)) : NotFound());

    [HttpPost]
    [TenantAccess(TenantPermissions.IssuingDraft)]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Create(Guid tenantId, IssuedInvoiceDraftRequest request, CancellationToken ct, [FromQuery] string? invoiceType = null) =>
        GuardedAsync(StatusCodes.Status400BadRequest, async () =>
            CreatedInvoice(tenantId, await issuedInvoices.CreateAsync(tenantId, ToInput(request), invoiceType, ct)));

    [HttpPut("{id:guid}")]
    [TenantAccess(TenantPermissions.IssuingDraft)]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Update(Guid tenantId, Guid id, IssuedInvoiceDraftRequest request, [FromQuery] uint? version, CancellationToken ct, [FromQuery] string? invoiceType = null) =>
        GuardedAsync(StatusCodes.Status400BadRequest, async () =>
            await issuedInvoices.UpdateAsync(tenantId, id, ToInput(request), version, invoiceType, ct) is { } view ? Ok(ToResponse(view)) : NotFound());

    [HttpDelete("{id:guid}")]
    [TenantAccess(TenantPermissions.IssuingDraft)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Delete(Guid tenantId, Guid id, [FromQuery] uint? version, CancellationToken ct) =>
        GuardedAsync(StatusCodes.Status400BadRequest, async () =>
            await issuedInvoices.DeleteAsync(tenantId, id, version, ct) ? NoContent() : NotFound());

    [HttpPost("{id:guid}/issue")]
    [TenantAccess(TenantPermissions.IssuingSubmit)]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Issue(Guid tenantId, Guid id, [FromQuery] uint? version, CancellationToken ct, [FromQuery] bool confirmPastIssueDate = false, [FromQuery] uint? sellerVersion = null, [FromQuery] string? invoiceType = null) =>
        GuardedAsync(StatusCodes.Status422UnprocessableEntity, async () =>
        {
            if (await issuedInvoices.IssueAsync(tenantId, id, version, confirmPastIssueDate, sellerVersion, invoiceType, ct) is not { } result)
                return NotFound();

            var response = ToResponse(result.View);
            return result.Issued ? StatusCode(StatusCodes.Status202Accepted, response) : Ok(response);
        });

    [HttpPost("{id:guid}/copy")]
    [TenantAccess(TenantPermissions.IssuingDraft)]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Copy(Guid tenantId, Guid id, CancellationToken ct, [FromQuery] string? invoiceType = null) =>
        GuardedAsync(StatusCodes.Status400BadRequest, async () =>
            await issuedInvoices.CopyAsync(tenantId, id, invoiceType, ct) is { } view ? CreatedInvoice(tenantId, view) : NotFound());

    [HttpPost("{id:guid}/convert")]
    [TenantAccess(TenantPermissions.IssuingDraft)]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> ConvertProforma(Guid tenantId, Guid id, CancellationToken ct) =>
        GuardedAsync(StatusCodes.Status400BadRequest, async () =>
        {
            if (await issuedInvoices.ConvertProformaAsync(tenantId, id, ct) is not { } result)
                return NotFound();
            return result.Created ? CreatedInvoice(tenantId, result.View) : Ok(ToResponse(result.View));
        });

    [HttpPost("{id:guid}/corrections")]
    [TenantAccess(TenantPermissions.IssuingDraft)]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> CreateCorrection(Guid tenantId, Guid id, CorrectionCreateRequest? request, CancellationToken ct) =>
        GuardedAsync(StatusCodes.Status400BadRequest, async () =>
            await issuedInvoices.CreateCorrectionAsync(tenantId, id, request?.Mode, ct) is { } view ? CreatedInvoice(tenantId, view) : NotFound());

    [HttpPut("{id:guid}/correction")]
    [TenantAccess(TenantPermissions.IssuingDraft)]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> UpdateCorrection(Guid tenantId, Guid id, CorrectionDraftRequest request, [FromQuery] uint? version, CancellationToken ct) =>
        GuardedAsync(StatusCodes.Status422UnprocessableEntity, async () =>
            await issuedInvoices.UpdateCorrectionAsync(tenantId, id, new IssuedInvoiceCorrectionInput(
                request.IssueDate,
                request.DueDate,
                request.PaymentMethod,
                request.CorrectionReason,
                request.CorrectionType,
                request.Buyer is { } buyer ? new IssuedInvoiceBuyerInput(buyer.Kind, buyer.Nip, buyer.Name, buyer.AddressLine1, buyer.AddressLine2, buyer.CountryCode, buyer.ForeignId) : null,
                request.Buyer?.IdType,
                request.Lines?.Select(l => new IssuedInvoiceLineInput(l.Name, l.Unit, l.Quantity, l.UnitNetPrice, l.VatRate, l.BaseLineNo)).ToList(),
                request.AdvanceAmount), version, ct) is { } view
                ? Ok(ToResponse(view))
                : NotFound());

    [HttpPost("advances")]
    [TenantAccess(TenantPermissions.IssuingDraft)]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> CreateAdvance(Guid tenantId, AdvanceDraftRequest? request, CancellationToken ct) =>
        GuardedAsync(StatusCodes.Status422UnprocessableEntity, async () =>
            await issuedInvoices.CreateAdvanceAsync(tenantId, request is null ? null : ToAdvanceInput(request), ct) is { } view ? CreatedInvoice(tenantId, view) : NotFound());

    [HttpPost("{id:guid}/advances")]
    [TenantAccess(TenantPermissions.IssuingDraft)]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> NextAdvance(Guid tenantId, Guid id, CancellationToken ct) =>
        GuardedAsync(StatusCodes.Status422UnprocessableEntity, async () =>
            await issuedInvoices.NextAdvanceAsync(tenantId, id, ct) is { } view ? CreatedInvoice(tenantId, view) : NotFound());

    [HttpPut("{id:guid}/advance")]
    [TenantAccess(TenantPermissions.IssuingDraft)]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> UpdateAdvance(Guid tenantId, Guid id, AdvanceDraftRequest request, [FromQuery] uint? version, CancellationToken ct) =>
        GuardedAsync(StatusCodes.Status422UnprocessableEntity, async () =>
            await issuedInvoices.UpdateAdvanceAsync(tenantId, id, ToAdvanceInput(request), version, ct) is { } view ? Ok(ToResponse(view)) : NotFound());

    [HttpPost("{id:guid}/final")]
    [TenantAccess(TenantPermissions.IssuingDraft)]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> CreateFinal(Guid tenantId, Guid id, CancellationToken ct) =>
        GuardedAsync(StatusCodes.Status422UnprocessableEntity, async () =>
            await issuedInvoices.CreateFinalAsync(tenantId, id, ct) is { } view ? CreatedInvoice(tenantId, view) : NotFound());

    [HttpPut("{id:guid}/final")]
    [TenantAccess(TenantPermissions.IssuingDraft)]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> UpdateFinal(Guid tenantId, Guid id, FinalDraftRequest request, [FromQuery] uint? version, CancellationToken ct) =>
        GuardedAsync(StatusCodes.Status422UnprocessableEntity, async () =>
            await issuedInvoices.UpdateFinalAsync(tenantId, id, new IssuedInvoiceFinalInput(
                request.IssueDate,
                request.SaleDate,
                request.DueDate,
                request.PaymentMethod,
                request.SplitPayment,
                request.ExchangeRate,
                request.ExchangeRateDate,
                request.ExemptionBasis?.Kind,
                request.ExemptionBasis?.Text,
                ToLines(request.Lines),
                ToBuyer(request.Buyer),
                request.Currency), version, ct) is { } view
                ? Ok(ToResponse(view))
                : NotFound());

    [HttpPost("{id:guid}/reissue")]
    [TenantAccess(TenantPermissions.IssuingDraft)]
    [ProducesResponseType(typeof(IssuedInvoiceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Reissue(Guid tenantId, Guid id, CancellationToken ct) =>
        GuardedAsync(StatusCodes.Status400BadRequest, async () =>
            await issuedInvoices.ReissueAsync(tenantId, id, ct) is { } view ? CreatedInvoice(tenantId, view) : NotFound());

    [HttpGet("{id:guid}/xml")]
    [TenantAccess(TenantPermissions.InvoicesRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> GetXml(Guid tenantId, Guid id, CancellationToken ct) =>
        GuardedAsync(StatusCodes.Status400BadRequest, async () =>
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
    [TenantAccess(TenantPermissions.InvoicesRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> GetUpo(Guid tenantId, Guid id, CancellationToken ct) =>
        GuardedAsync(StatusCodes.Status400BadRequest, async () =>
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

    private async Task<IActionResult> GuardedAsync(int validationStatus, Func<Task<IActionResult>> action)
    {
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
            return e.Code is null ? Conflict(new { message = e.Message }) : Conflict(new { message = e.Message, code = e.Code });
        }
    }

    private CreatedAtActionResult CreatedInvoice(Guid tenantId, IssuedInvoiceView view) =>
        CreatedAtAction(nameof(Get), new { tenantId, id = view.Invoice.Id }, ToResponse(view));

    private static string WireStatus(IssuedInvoiceStatus status) =>
        status is IssuedInvoiceStatus.Queued or IssuedInvoiceStatus.Sending ? "Sending" : status.ToString();

    private static IssuedInvoiceBuyerInput? ToBuyer(IssuedInvoiceBuyerModel? buyer) =>
        buyer is null ? null : new IssuedInvoiceBuyerInput(buyer.Kind, buyer.Nip, buyer.Name, buyer.AddressLine1, buyer.AddressLine2, buyer.CountryCode, buyer.ForeignId);

    private static List<IssuedInvoiceLineInput>? ToLines(IReadOnlyList<IssuedInvoiceLineRequest>? lines) =>
        lines?.Select(l => new IssuedInvoiceLineInput(l.Name, l.Unit, l.Quantity, l.UnitNetPrice, l.VatRate)).ToList();

    private static IssuedInvoiceAdvanceInput ToAdvanceInput(AdvanceDraftRequest request) => new(
        ToBuyer(request.Buyer),
        request.IssueDate,
        request.PaymentDate,
        request.AdvanceAmount,
        request.PaymentMethod,
        request.Currency,
        request.ExchangeRate,
        request.ExchangeRateDate,
        request.ExemptionBasis?.Kind,
        request.ExemptionBasis?.Text,
        ToLines(request.Lines));

    private static IssuedInvoiceDraftInput ToInput(IssuedInvoiceDraftRequest request) => new(
        ToBuyer(request.Buyer),
        request.IssueDate,
        request.SaleDate,
        request.DueDate,
        request.PaymentMethod,
        request.SplitPayment,
        request.ExemptionBasis?.Kind,
        request.ExemptionBasis?.Text,
        ToLines(request.Lines),
        request.Currency,
        request.ExchangeRate,
        request.ExchangeRateDate);

    private static IssuedInvoiceLineResponse LineResponse(IssuedInvoiceLine l) =>
        new(l.LineNo, l.Name, l.Unit, l.Quantity, l.UnitNetPrice, l.VatRate.Code(), l.NetAmount, l.VatAmount, l.BaseLineNo);

    private static IssuedInvoiceBuyerModel BuyerModel(IssuedInvoice invoice) =>
        new(invoice.BuyerKind.ToString(), invoice.BuyerNip, invoice.BuyerName, invoice.BuyerAddressLine1, invoice.BuyerAddressLine2,
            invoice.BuyerCountryCode, invoice.BuyerIdType, invoice.BuyerForeignId);

    private static InvoiceTemplateResponse TemplateResponse(InvoiceTemplate t) =>
        new(t.Id, t.Name,
            new IssuedInvoiceBuyerModel(t.BuyerKind.ToString(), t.BuyerNip, t.BuyerName, t.BuyerAddressLine1, t.BuyerAddressLine2, t.BuyerCountryCode, t.BuyerIdType, t.BuyerForeignId),
            t.Currency, t.PaymentDays, t.Lines.Count, t.CreatedAt);

    private static IReadOnlyList<VatSummaryResponse> SummaryResponse(IEnumerable<VatSummary> summary) =>
        summary.Select(s => new VatSummaryResponse(s.VatRate.Code(), s.Net, s.Vat)).ToList();

    private static IssuedInvoiceResponse ToResponse(IssuedInvoiceView view)
    {
        var invoice = view.Invoice;
        var state = view.CorrectedState;
        var original = view.Original;
        var current = view.CurrentState;
        return new IssuedInvoiceResponse(
            invoice.Id,
            WireStatus(invoice.Status),
            invoice.Number,
            new IssuedInvoiceSellerResponse(view.Seller.Nip, view.Seller.Name, view.Seller.AddressLine1, view.Seller.AddressLine2, view.Seller.BankAccount, view.Seller.CashAccounting,
                view.Seller.Iban, view.Seller.Swift, view.Seller.BankName),
            BuyerModel(invoice),
            invoice.IssueDate,
            invoice.SaleDate,
            invoice.DueDate,
            invoice.PaymentMethod.ToString(),
            invoice.SplitPayment,
            invoice.ExemptionBasisKind is { } kind ? new ExemptionBasisModel(kind.ToString(), invoice.ExemptionBasisText) : null,
            invoice.Lines.OrderBy(l => l.LineNo).Select(LineResponse).ToList(),
            SummaryResponse(invoice.Buckets(state, view.Deducted)),
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
                invoice.DuplicateKsefNumber,
                invoice.Status == IssuedInvoiceStatus.Accepted && invoice.AcceptedAt is null ? invoice.KsefNumberDate : null,
                invoice.UpoUnavailable),
            view.Warnings.Select(w => new ValidationIssue(w.Code, w.Field, w.Message)).ToList(),
            invoice.CreatedAt,
            invoice.UpdatedAt,
            invoice.IssuedAt,
            invoice.ExchangeRate,
            invoice.ExchangeRateDate,
            view.Version,
            view.SellerVersion,
            invoice.InvoiceType.Code(),
            original is null ? null : new CorrectedInvoiceRefResponse(original.Id, original.Number, original.KsefNumber, original.IssueDate, original.InvoiceType.Code()),
            invoice.CorrectionReason,
            invoice.CorrectionType,
            state is null
                ? null
                : (invoice.InvoiceType == IssuedInvoiceType.KorZal ? Advances.OrderRows(state, invoice) : Corrections.Rows(state.Lines, invoice.Lines))
                    .Select(r => new CorrectionRowResponse(r.RowNo, r.IsBefore, r.BaseLineNo, r.Name, r.Unit, r.Quantity, r.UnitNetPrice, r.VatRate.Code(), r.NetAmount))
                    .ToList(),
            state is not null && Corrections.BuyerDataDiffers(invoice, state) ? BuyerModel(state) : null,
            current is null
                ? null
                : new CurrentStateResponse(
                    current.Lines.OrderBy(l => l.LineNo).Select(LineResponse).ToList(),
                    SummaryResponse(current.VatSummary()),
                    current.VatSummary().Sum(s => s.Net),
                    current.VatSummary().Sum(s => s.Vat),
                    current.VatSummary().Sum(s => s.Net + s.Vat),
                    BuyerModel(current),
                    current.Lines.Count == 0,
                    invoice.InvoiceType == IssuedInvoiceType.Zal ? current.AdvanceAmount : null),
            view.Corrections.Count == 0
                ? null
                : view.Corrections.OrderBy(c => c.CreatedAt)
                    .Select(ChainItem)
                    .ToList(),
            view.Corrections.FirstOrDefault(c => c.IsOpen)?.Id,
            invoice.OrderChainId,
            invoice.AdvanceAmount,
            view.AmountBefore,
            view.AdvanceInvoices.Count == 0 ? null : view.AdvanceInvoices.Select(a => new AdvanceInvoiceRefResponse(a.Id, a.Number, a.KsefNumber)).ToList(),
            view.Chain is not { } chain
                ? null
                : new OrderChainResponse(
                    chain.OrderValue,
                    chain.Advances.Select(ChainItem).ToList(),
                    chain.Final is null ? null : ChainItem(chain.Final),
                    chain.Corrections.Select(ChainItem).ToList(),
                    chain.Remaining,
                    chain.OpenDocumentId),
            view.Proforma is null ? null : ChainItem(view.Proforma),
            view.ConvertedTo is null ? null : ChainItem(view.ConvertedTo));
    }

    private static CorrectionListItemResponse ChainItem(IssuedInvoice c) =>
        new(c.Id, c.Number, c.InvoiceType.Code(), WireStatus(c.Status), c.IssueDate, c.TotalGross);
}
