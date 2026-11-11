using System.Linq.Expressions;
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
    public static readonly Expression<Func<InvoiceHeader, InvoiceResponse>> Projection = i => new InvoiceResponse(
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
        i.VendorBankAccount,
        null,
        i.DueDate,
        i.Direction.ToString(),
        i.BuyerIdType,
        i.BuyerForeignId);

    private static readonly Func<InvoiceHeader, InvoiceResponse> CompiledProjection = Projection.Compile();

    public static InvoiceResponse FromEntity(InvoiceHeader i) => CompiledProjection(i) with
    {
        Lines = i.Lines
            .OrderBy(l => l.LineNumber)
            .Select(l => new InvoiceLineResponse(
                l.LineNumber, l.Name, l.Unit, l.Quantity,
                l.UnitPriceNet, l.UnitPriceGross,
                l.AmountNet, l.AmountGross, l.AmountVat, l.VatRate))
            .ToList()
    };

    public bool IsOverdue =>
        Direction != nameof(InvoiceDirection.Issued) && InvoiceHeader.IsOverdue(IsPaid, DueDate, DateTimeOffset.UtcNow);
}
