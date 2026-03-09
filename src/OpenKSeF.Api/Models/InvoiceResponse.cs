using System.Text.Json.Serialization;

namespace OpenKSeF.Api.Models;

public record InvoiceResponse(
    Guid Id,
    [property: JsonPropertyName("ksefInvoiceNumber")] string KSeFInvoiceNumber,
    [property: JsonPropertyName("ksefReferenceNumber")] string KSeFReferenceNumber,
    string? InvoiceNumber,
    string VendorName,
    string VendorNip,
    string? BuyerName,
    string? BuyerNip,
    decimal AmountNet,
    decimal AmountVat,
    decimal AmountGross,
    string Currency,
    DateTime IssueDate,
    DateTime? AcquisitionDate,
    string? InvoiceType,
    DateTime FirstSeenAt,
    bool IsPaid,
    DateTime? PaidAt);
