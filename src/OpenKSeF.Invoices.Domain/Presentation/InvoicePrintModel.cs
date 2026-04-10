using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Presentation;

public sealed record InvoicePrintModel(
    DocumentKind Kind,
    string Title,
    string Language,
    string? DocumentNumber,
    string SellerName,
    string BuyerName,
    CurrencyCode Currency,
    DocumentTotals Totals,
    DateTime? DuplicateIssuedAt = null);
