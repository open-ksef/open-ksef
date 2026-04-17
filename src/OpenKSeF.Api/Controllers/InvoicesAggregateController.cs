using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;
using OpenKSeF.Invoices.Application.Commands.ApproveInvoice;
using OpenKSeF.Invoices.Application.Commands.CreateCorrectionFromOriginal;
using OpenKSeF.Invoices.Application.Commands.CreateFinalInvoiceFromAdvances;
using OpenKSeF.Invoices.Application.Commands.CreateInvoice;
using OpenKSeF.Invoices.Application.Commands.RecordKsefAcceptance;
using OpenKSeF.Invoices.Application.Commands.RecordKsefRejection;
using OpenKSeF.Invoices.Application.Commands.ReopenInvoice;
using OpenKSeF.Invoices.Application.Commands.UpdateInvoiceDraft;
using OpenKSeF.Invoices.Application.Projection;
using OpenKSeF.Invoices.Contracts.Dtos;
using OpenKSeF.Invoices.Contracts.Dtos.Requests;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.Integration;
using OpenKSeF.Invoices.Domain.Persistence;
using OpenKSeF.Invoices.Domain.Policies;
using OpenKSeF.Invoices.Domain.Projection;
using OpenKSeF.Invoices.Domain.Validation;
using OpenKSeF.Invoices.Domain.Validation.Orchestrators;

namespace OpenKSeF.Api.Controllers;

[ApiController]
[Route("api/tenants/{tenantId:guid}/invoices/aggregate")]
[Authorize]
public sealed class InvoicesAggregateController : ControllerBase
{
    private const string InvalidFilterCode = "INV-VAL-003";
    private const string DuplicatePreconditionCode = "IMM-003";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ICreateInvoiceHandler _createInvoiceHandler;
    private readonly IUpdateInvoiceDraftHandler _updateInvoiceDraftHandler;
    private readonly IApproveInvoiceHandler _approveInvoiceHandler;
    private readonly IReopenInvoiceHandler _reopenInvoiceHandler;
    private readonly ICreateCorrectionFromOriginalHandler _createCorrectionFromOriginalHandler;
    private readonly ICreateFinalInvoiceFromAdvancesHandler _createFinalInvoiceFromAdvancesHandler;
    private readonly IRecordKsefAcceptanceHandler _recordKsefAcceptanceHandler;
    private readonly IRecordKsefRejectionHandler _recordKsefRejectionHandler;
    private readonly IInvoiceReadModelProjector<InvoiceReadDto> _invoiceReadDtoProjector;
    private readonly Func<PrintVariant, InvoicePrintModelProjector> _invoicePrintModelProjectorFactory;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly DraftValidationService _draftValidationService;
    private readonly KsefSubmissionValidationService _ksefSubmissionValidationService;
    private readonly InvoiceAggregateValidationContextFactory _validationContextFactory;
    private readonly InvoiceAggregateMutationService _mutationService;
    private readonly IInvoiceToKsefPayloadMapper _invoiceToKsefPayloadMapper;
    private readonly IClock _clock;

    public InvoicesAggregateController(
        ApplicationDbContext db,
        ICurrentUserService currentUser,
        ICreateInvoiceHandler createInvoiceHandler,
        IUpdateInvoiceDraftHandler updateInvoiceDraftHandler,
        IApproveInvoiceHandler approveInvoiceHandler,
        IReopenInvoiceHandler reopenInvoiceHandler,
        ICreateCorrectionFromOriginalHandler createCorrectionFromOriginalHandler,
        ICreateFinalInvoiceFromAdvancesHandler createFinalInvoiceFromAdvancesHandler,
        IRecordKsefAcceptanceHandler recordKsefAcceptanceHandler,
        IRecordKsefRejectionHandler recordKsefRejectionHandler,
        IInvoiceReadModelProjector<InvoiceReadDto> invoiceReadDtoProjector,
        Func<PrintVariant, InvoicePrintModelProjector> invoicePrintModelProjectorFactory,
        IInvoiceRepository invoiceRepository,
        DraftValidationService draftValidationService,
        KsefSubmissionValidationService ksefSubmissionValidationService,
        InvoiceAggregateValidationContextFactory validationContextFactory,
        InvoiceAggregateMutationService mutationService,
        IInvoiceToKsefPayloadMapper invoiceToKsefPayloadMapper,
        IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _createInvoiceHandler = createInvoiceHandler;
        _updateInvoiceDraftHandler = updateInvoiceDraftHandler;
        _approveInvoiceHandler = approveInvoiceHandler;
        _reopenInvoiceHandler = reopenInvoiceHandler;
        _createCorrectionFromOriginalHandler = createCorrectionFromOriginalHandler;
        _createFinalInvoiceFromAdvancesHandler = createFinalInvoiceFromAdvancesHandler;
        _recordKsefAcceptanceHandler = recordKsefAcceptanceHandler;
        _recordKsefRejectionHandler = recordKsefRejectionHandler;
        _invoiceReadDtoProjector = invoiceReadDtoProjector;
        _invoicePrintModelProjectorFactory = invoicePrintModelProjectorFactory;
        _invoiceRepository = invoiceRepository;
        _draftValidationService = draftValidationService;
        _ksefSubmissionValidationService = ksefSubmissionValidationService;
        _validationContextFactory = validationContextFactory;
        _mutationService = mutationService;
        _invoiceToKsefPayloadMapper = invoiceToKsefPayloadMapper;
        _clock = clock;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<InvoiceReadDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RuleCodeErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        Guid tenantId,
        [FromQuery] string[]? status = null,
        [FromQuery] string[]? kind = null,
        [FromQuery] string? buyerKind = null,
        [FromQuery(Name = "from")] DateTime? issueDateFrom = null,
        [FromQuery(Name = "to")] DateTime? issueDateTo = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        if (!await VerifyTenantOwnership(tenantId))
        {
            return Forbid();
        }

        if (!TryParseMany(status, out IReadOnlySet<DocumentStatus> statuses, out var invalidStatus))
        {
            return BadRequest(new RuleCodeErrorResponse(
                InvalidFilterCode,
                $"Nieznany filtr statusu '{invalidStatus}'."));
        }

        if (!TryParseMany(kind, out IReadOnlySet<DocumentKind> kinds, out var invalidKind))
        {
            return BadRequest(new RuleCodeErrorResponse(
                InvalidFilterCode,
                $"Nieznany filtr rodzaju dokumentu '{invalidKind}'."));
        }

        if (!TryParseSingle(buyerKind, out BuyerKind? parsedBuyerKind))
        {
            return BadRequest(new RuleCodeErrorResponse(
                InvalidFilterCode,
                $"Nieznany filtr typu nabywcy '{buyerKind}'."));
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var invoices = await _invoiceRepository.ListByTenantAsync(new(tenantId), ct);
        var filtered = invoices.Where(invoice =>
            (statuses.Count == 0 || statuses.Contains(invoice.Status)) &&
            (kinds.Count == 0 || kinds.Contains(invoice.Kind)) &&
            (!parsedBuyerKind.HasValue || invoice.BuyerKind == parsedBuyerKind.Value) &&
            (!issueDateFrom.HasValue || invoice.IssueDate >= issueDateFrom.Value) &&
            (!issueDateTo.HasValue || invoice.IssueDate <= issueDateTo.Value));

        var totalCount = filtered.Count();
        var items = filtered
            .OrderByDescending(invoice => invoice.IssueDate)
            .ThenByDescending(invoice => invoice.DocumentNumber?.Value)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(_invoiceReadDtoProjector.Project)
            .ToList();

        return Ok(new PagedResult<InvoiceReadDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(InvoiceReadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var invoice = await LoadInvoiceForItemRouteAsync(tenantId, id, ct);
        return invoice is null ? NotFound() : Ok(_invoiceReadDtoProjector.Project(invoice));
    }

    [HttpPost]
    [ProducesResponseType(typeof(InvoiceReadDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        Guid tenantId,
        [FromBody] CreateInvoiceRequest request,
        CancellationToken ct = default)
    {
        if (!await VerifyTenantOwnership(tenantId))
        {
            return Forbid();
        }

        ValidateCreateRequest(request);

        var invoice = _createInvoiceHandler.Handle(request.ToCommand(tenantId));
        var context = await _validationContextFactory.CreateDraftAsync(invoice, ct);
        EnsureDraftValidationPasses(invoice, context);

        await _invoiceRepository.SaveAsync(invoice, ct);

        return CreatedAtAction(
            nameof(Get),
            new { tenantId, id = invoice.Id.Value },
            _invoiceReadDtoProjector.Project(invoice));
    }

    [HttpPatch("{id:guid}/draft")]
    [ProducesResponseType(typeof(InvoiceReadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateDraft(
        Guid tenantId,
        Guid id,
        [FromBody] UpdateInvoiceDraftRequest request,
        CancellationToken ct = default)
    {
        var invoice = await LoadInvoiceForItemRouteAsync(tenantId, id, ct);
        if (invoice is null)
        {
            return NotFound();
        }

        _updateInvoiceDraftHandler.Handle(invoice, request.ToCommand(id));
        var context = await _validationContextFactory.CreateDraftAsync(invoice, ct);
        EnsureDraftValidationPasses(invoice, context);

        await _invoiceRepository.SaveAsync(invoice, ct);
        return Ok(_invoiceReadDtoProjector.Project(invoice));
    }

    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(typeof(InvoiceReadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Approve(
        Guid tenantId,
        Guid id,
        [FromBody] ApproveInvoiceRequest request,
        CancellationToken ct = default)
    {
        var invoice = await LoadInvoiceForItemRouteAsync(tenantId, id, ct);
        if (invoice is null)
        {
            return NotFound();
        }

        var context = await _validationContextFactory.CreateApprovalAsync(invoice, "DraftToApproved", ct);
        _approveInvoiceHandler.Handle(invoice, request.ToCommand(id, _clock.UtcNow), context);

        await _invoiceRepository.SaveAsync(invoice, ct);
        return Ok(_invoiceReadDtoProjector.Project(invoice));
    }

    [HttpPost("{id:guid}/reopen")]
    [ProducesResponseType(typeof(InvoiceReadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reopen(
        Guid tenantId,
        Guid id,
        [FromBody] ReopenInvoiceRequest request,
        CancellationToken ct = default)
    {
        var invoice = await LoadInvoiceForItemRouteAsync(tenantId, id, ct);
        if (invoice is null)
        {
            return NotFound();
        }

        _reopenInvoiceHandler.Handle(invoice, request.ToCommand(id, _clock.UtcNow));
        await _invoiceRepository.SaveAsync(invoice, ct);

        return Ok(_invoiceReadDtoProjector.Project(invoice));
    }

    [HttpPost("{id:guid}/submit-to-ksef")]
    [ProducesResponseType(typeof(InvoiceReadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SubmitToKsef(
        Guid tenantId,
        Guid id,
        CancellationToken ct = default)
    {
        var invoice = await LoadInvoiceForItemRouteAsync(tenantId, id, ct);
        if (invoice is null)
        {
            return NotFound();
        }

        if (invoice.Status != DocumentStatus.Approved)
        {
            _mutationService.Submit(invoice, _clock.UtcNow);
        }

        var payload = _invoiceToKsefPayloadMapper.TryMap(invoice);
        var context = await _validationContextFactory.CreateSendToKsefAsync(invoice, mappingFailed: payload is null, ct);
        var validationResult = _ksefSubmissionValidationService.Validate(invoice, payload, context);
        if (validationResult.HasErrors)
        {
            throw new InvoiceDomainException(
                "Invoice submission blocked by validation.",
                stage: ValidationStage.SendToKsef,
                validationResult: validationResult);
        }

        _mutationService.Submit(invoice, _clock.UtcNow);
        await _invoiceRepository.SaveAsync(invoice, ct);

        return Ok(_invoiceReadDtoProjector.Project(invoice));
    }

    [HttpPost("{id:guid}/ksef-acceptance")]
    [ProducesResponseType(typeof(InvoiceReadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordKsefAcceptance(
        Guid tenantId,
        Guid id,
        [FromBody] RecordKsefAcceptanceRequest request,
        CancellationToken ct = default)
    {
        var invoice = await LoadInvoiceForItemRouteAsync(tenantId, id, ct);
        if (invoice is null)
        {
            return NotFound();
        }

        _recordKsefAcceptanceHandler.Handle(invoice, request.ToCommand(id));
        await _invoiceRepository.SaveAsync(invoice, ct);

        return Ok(_invoiceReadDtoProjector.Project(invoice));
    }

    [HttpPost("{id:guid}/ksef-rejection")]
    [ProducesResponseType(typeof(InvoiceReadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordKsefRejection(
        Guid tenantId,
        Guid id,
        [FromBody] RecordKsefRejectionRequest request,
        CancellationToken ct = default)
    {
        var invoice = await LoadInvoiceForItemRouteAsync(tenantId, id, ct);
        if (invoice is null)
        {
            return NotFound();
        }

        _recordKsefRejectionHandler.Handle(invoice, request.ToCommand(id));
        await _invoiceRepository.SaveAsync(invoice, ct);

        return Ok(_invoiceReadDtoProjector.Project(invoice));
    }

    [HttpPost("{id:guid}/corrections")]
    [ProducesResponseType(typeof(InvoiceReadDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateCorrection(
        Guid tenantId,
        Guid id,
        [FromBody] CreateCorrectionFromOriginalRequest request,
        CancellationToken ct = default)
    {
        var original = await LoadInvoiceForItemRouteAsync(tenantId, id, ct);
        if (original is null)
        {
            return NotFound();
        }

        var correction = _createCorrectionFromOriginalHandler.Handle(original, request.ToCommand(tenantId));
        var context = await _validationContextFactory.CreateDraftAsync(correction, ct);
        EnsureDraftValidationPasses(correction, context);

        await _invoiceRepository.SaveAsync(correction, ct);

        return CreatedAtAction(
            nameof(Get),
            new { tenantId, id = correction.Id.Value },
            _invoiceReadDtoProjector.Project(correction));
    }

    [HttpPost("final-from-advances")]
    [ProducesResponseType(typeof(InvoiceReadDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ValidationEnvelope), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateFinalFromAdvances(
        Guid tenantId,
        [FromBody] CreateFinalInvoiceFromAdvancesRequest request,
        CancellationToken ct = default)
    {
        if (!await VerifyTenantOwnership(tenantId))
        {
            return Forbid();
        }

        var advancesById = (await _invoiceRepository.ListByTenantAsync(new(tenantId), ct))
            .ToDictionary(invoice => invoice.Id.Value);

        var advances = request.Advances
            .Where(advance => advancesById.ContainsKey(advance.AdvanceInvoiceId))
            .Select(advance => advancesById[advance.AdvanceInvoiceId])
            .ToList();

        if (advances.Count != request.Advances.Count)
        {
            return NotFound();
        }

        var finalInvoice = _createFinalInvoiceFromAdvancesHandler.Handle(advances, request.ToCommand(tenantId));
        var context = await _validationContextFactory.CreateDraftAsync(finalInvoice, ct);
        EnsureDraftValidationPasses(finalInvoice, context);

        await _invoiceRepository.SaveAsync(finalInvoice, ct);

        return CreatedAtAction(
            nameof(Get),
            new { tenantId, id = finalInvoice.Id.Value },
            _invoiceReadDtoProjector.Project(finalInvoice));
    }

    [HttpGet("{id:guid}/print")]
    [ProducesResponseType(typeof(InvoicePrintModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RuleCodeErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RuleCodeErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPrint(
        Guid tenantId,
        Guid id,
        [FromQuery] string variant = nameof(PrintVariant.Standard),
        CancellationToken ct = default)
    {
        var invoice = await LoadInvoiceForItemRouteAsync(tenantId, id, ct);
        if (invoice is null)
        {
            return NotFound();
        }

        if (!TryParseSingle(variant, out PrintVariant? parsedVariant))
        {
            return BadRequest(new RuleCodeErrorResponse(
                InvalidFilterCode,
                $"Nieznany wariant wydruku '{variant}'."));
        }

        if (parsedVariant == PrintVariant.Duplicate)
        {
            if (invoice.Status != DocumentStatus.AcceptedByKsef)
            {
                return Conflict(new RuleCodeErrorResponse(
                    DuplicatePreconditionCode,
                    "Duplikat można wygenerować dopiero po akceptacji przez KSeF."));
            }

            _mutationService.RecordDuplicateIssue(invoice, _clock.UtcNow, _currentUser.UserId);
            await _invoiceRepository.SaveAsync(invoice, ct);
        }

        var printVariant = parsedVariant ?? PrintVariant.Standard;
        return Ok(_invoicePrintModelProjectorFactory(printVariant).Project(invoice));
    }

    [HttpGet("{id:guid}/duplicates")]
    [ProducesResponseType(typeof(IReadOnlyList<DuplicatePrintInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListDuplicates(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var invoice = await LoadInvoiceForItemRouteAsync(tenantId, id, ct);
        if (invoice is null)
        {
            return NotFound();
        }

        var duplicates = invoice.DuplicateIssuances
            .Select(duplicate => new DuplicatePrintInfo(
                duplicate.IssuedAt,
                duplicate.IssuedBy,
                invoice.Id.Value,
                invoice.DocumentNumber?.Value ?? string.Empty))
            .ToList();

        return Ok(duplicates);
    }

    private void EnsureDraftValidationPasses(
        OpenKSeF.Invoices.Domain.Aggregates.Invoice invoice,
        ValidationContext context)
    {
        var validationResult = _draftValidationService.Validate(invoice, context);
        if (validationResult.HasErrors)
        {
            throw new InvoiceDomainException(
                "Invoice draft validation failed.",
                stage: ValidationStage.Draft,
                validationResult: validationResult);
        }
    }

    private static bool TryParseMany<T>(
        IEnumerable<string>? rawValues,
        out IReadOnlySet<T> parsedValues,
        out string? invalidValue)
        where T : struct, Enum
    {
        var values = new HashSet<T>();
        invalidValue = null;

        foreach (var rawValue in rawValues ?? [])
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                continue;
            }

            if (!Enum.TryParse<T>(rawValue, ignoreCase: true, out var parsed))
            {
                parsedValues = values;
                invalidValue = rawValue;
                return false;
            }

            values.Add(parsed);
        }

        parsedValues = values;
        return true;
    }

    private static bool TryParseSingle<T>(string? rawValue, out T? parsedValue)
        where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            parsedValue = null;
            return true;
        }

        if (Enum.TryParse<T>(rawValue, ignoreCase: true, out var parsed))
        {
            parsedValue = parsed;
            return true;
        }

        parsedValue = null;
        return false;
    }

    private async Task<OpenKSeF.Invoices.Domain.Aggregates.Invoice?> LoadInvoiceForItemRouteAsync(
        Guid tenantId,
        Guid id,
        CancellationToken ct)
    {
        if (!await VerifyTenantOwnership(tenantId))
        {
            return null;
        }

        var invoice = await _invoiceRepository.FindByIdAsync(new(id), ct);
        return invoice?.TenantId.Value == tenantId ? invoice : null;
    }

    private void ValidateCreateRequest(CreateInvoiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var messages = new List<ValidationMessage>();

        if (string.IsNullOrWhiteSpace(request.SellerName))
        {
            messages.Add(CreateMessage("INV-VAL-010", "Seller.Name"));
        }

        if (string.IsNullOrWhiteSpace(request.SellerNip))
        {
            messages.Add(CreateMessage("INV-VAL-011", "Seller.Nip"));
        }

        if (request.BuyerKind == BuyerKind.Business && string.IsNullOrWhiteSpace(request.BuyerNip))
        {
            messages.Add(CreateMessage("INV-VAL-013", "Buyer.Nip"));
        }

        if (messages.Count == 0)
        {
            return;
        }

        throw new InvoiceDomainException(
            "Create invoice request validation failed.",
            stage: ValidationStage.Draft,
            validationResult: new ValidationResult(messages));
    }

    private static ValidationMessage CreateMessage(string code, string path) =>
        new(
            code,
            ValidationSeverity.Error,
            ValidationStage.Draft,
            code,
            code,
            path);

    private async Task<bool> VerifyTenantOwnership(Guid tenantId)
    {
        return await _db.Tenants
            .AnyAsync(tenant => tenant.Id == tenantId && tenant.UserId == _currentUser.UserId);
    }
}
