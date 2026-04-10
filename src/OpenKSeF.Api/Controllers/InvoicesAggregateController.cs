using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;
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
using OpenKSeF.Invoices.Domain.Projection;

namespace OpenKSeF.Api.Controllers;

[ApiController]
[Route("api/tenants/{tenantId:guid}/invoices/aggregate")]
[Authorize]
public sealed class InvoicesAggregateController : ControllerBase
{
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
        Func<PrintVariant, InvoicePrintModelProjector> invoicePrintModelProjectorFactory)
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
    }

    private async Task<bool> VerifyTenantOwnership(Guid tenantId)
    {
        return await _db.Tenants
            .AnyAsync(tenant => tenant.Id == tenantId && tenant.UserId == _currentUser.UserId);
    }
}
