using OpenKSeF.Invoices.Domain.Enums;

namespace OpenKSeF.Invoices.Contracts.Commands;

public sealed record CreateInvoiceCommand(
    Guid TenantId,
    DocumentKind Kind,
    string SellerName,
    string SellerNip,
    string BuyerName,
    BuyerKind BuyerKind,
    string? BuyerNip,
    string Currency,
    DateTime IssueDate,
    KsefSubmissionRequirement KsefSubmissionRequirement,
    string? DocumentNumber = null,
    string? ExternalReference = null);
