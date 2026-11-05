using System.Text.Json.Serialization;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;

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
    DateTime? PaidAt,
    string? VendorBankAccount = null,
    IReadOnlyList<InvoiceLineResponse>? Lines = null,
    DateOnly? DueDate = null,
    string? Direction = null,
    string? BuyerIdType = null,
    string? BuyerForeignId = null)
{
    public bool IsOverdue =>
        Direction != nameof(InvoiceDirection.Issued) && InvoiceHeader.IsOverdue(IsPaid, DueDate, DateTimeOffset.UtcNow);
}
