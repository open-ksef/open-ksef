using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Enums;
using System.Text.Json.Serialization;

namespace OpenKSeF.Invoices.Contracts.Dtos.Requests;

public sealed record CreateInvoiceRequest(
    [property: JsonConverter(typeof(InvoicePascalCaseEnumJsonConverter))] DocumentKind Kind,
    string SellerName,
    string SellerNip,
    string BuyerName,
    [property: JsonConverter(typeof(InvoicePascalCaseEnumJsonConverter))] BuyerKind BuyerKind,
    string? BuyerNip,
    string Currency,
    DateTime IssueDate,
    [property: JsonConverter(typeof(InvoicePascalCaseEnumJsonConverter))] KsefSubmissionRequirement KsefSubmissionRequirement,
    string? DocumentNumber = null,
    string? ExternalReference = null)
{
    public CreateInvoiceCommand ToCommand(Guid tenantId) =>
        new(
            tenantId,
            Kind,
            SellerName,
            SellerNip,
            BuyerName,
            BuyerKind,
            BuyerNip,
            Currency,
            IssueDate,
            KsefSubmissionRequirement,
            DocumentNumber,
            ExternalReference);
}

public sealed record UpdateInvoiceDraftRequest(
    DateTime? IssueDate = null,
    DateTime? SaleDate = null,
    DateTime? DueDate = null,
    string? DocumentNumber = null,
    string? ExternalReference = null,
    string? PaymentMethod = null,
    string? PublicNotes = null,
    string? InternalNotes = null,
    IReadOnlyList<UpdateInvoiceDraftLineRequest>? Lines = null)
{
    public UpdateInvoiceDraftCommand ToCommand(Guid invoiceId) =>
        new(
            invoiceId,
            IssueDate,
            SaleDate,
            DueDate,
            DocumentNumber,
            ExternalReference,
            PaymentMethod,
            PublicNotes,
            InternalNotes,
            Lines?.Select(line => line.ToContract()).ToList());
}

public sealed record UpdateInvoiceDraftLineRequest(
    int LineNumber,
    string Description,
    decimal Quantity,
    string? UnitOfMeasure,
    [property: JsonConverter(typeof(InvoicePascalCaseEnumJsonConverter))] PricingMode PricingMode,
    decimal UnitPrice,
    decimal? DiscountPercent,
    string VatRate)
{
    public UpdateInvoiceDraftLineCommand ToContract() =>
        new(
            LineNumber,
            Description,
            Quantity,
            UnitOfMeasure,
            PricingMode,
            UnitPrice,
            DiscountPercent,
            VatRate);
}

public sealed record ApproveInvoiceRequest
{
    public ApproveInvoiceCommand ToCommand(Guid invoiceId, DateTime approvedAt) =>
        new(invoiceId, approvedAt);
}

public sealed record ReopenInvoiceRequest
{
    public ReopenInvoiceCommand ToCommand(Guid invoiceId, DateTime reopenedAt) =>
        new(invoiceId, reopenedAt);
}

public sealed record CreateCorrectionFromOriginalRequest(
    DateTime IssueDate,
    [property: JsonConverter(typeof(InvoicePascalCaseEnumJsonConverter))] CorrectionReasonKind ReasonKind,
    string ReasonDescription)
{
    public CreateCorrectionFromOriginalCommand ToCommand(Guid tenantId) =>
        new(tenantId, IssueDate, ReasonKind, ReasonDescription);
}

public sealed record CreateFinalInvoiceFromAdvancesRequest(
    DateTime IssueDate,
    IReadOnlyList<AdvanceSettlementEntryRequest> Advances)
{
    public CreateFinalInvoiceFromAdvancesCommand ToCommand(Guid tenantId) =>
        new(
            tenantId,
            IssueDate,
            Advances
                .Select(advance => advance.ToContract())
                .ToList());
}

public sealed record AdvanceSettlementEntryRequest(
    Guid AdvanceInvoiceId,
    string AdvanceDocumentNumber,
    decimal SettledAmount)
{
    public AdvanceSettlementEntry ToContract() =>
        new(AdvanceInvoiceId, AdvanceDocumentNumber, SettledAmount);
}

public sealed record RecordKsefAcceptanceRequest(
    string KsefDocumentNumber,
    string KsefReferenceNumber,
    DateTime AcceptedAt)
{
    public RecordKsefAcceptanceCommand ToCommand(Guid invoiceId) =>
        new(invoiceId, KsefDocumentNumber, KsefReferenceNumber, AcceptedAt);
}

public sealed record RecordKsefRejectionRequest(
    string RejectionReason,
    DateTime RejectedAt)
{
    public RecordKsefRejectionCommand ToCommand(Guid invoiceId) =>
        new(invoiceId, RejectionReason, RejectedAt);
}
