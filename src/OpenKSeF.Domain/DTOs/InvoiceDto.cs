using OpenKSeF.Domain.Enums;

namespace OpenKSeF.Domain.DTOs;

public record InvoiceDto(
    string Number,
    string ReferenceNumber,
    string? InvoiceNumber,
    string VendorName,
    string VendorNip,
    string? BuyerName,
    string? BuyerNip,
    decimal AmountNet,
    decimal AmountVat,
    decimal AmountGross,
    string? Currency,
    DateTime IssueDate,
    DateTime? AcquisitionDate,
    string? InvoiceType,
    string? VendorBankAccount = null,
    IReadOnlyList<InvoiceLineDto>? Lines = null,
    DateOnly? DueDate = null,
    InvoiceDirection Direction = InvoiceDirection.Received,
    string? BuyerIdType = null,
    string? BuyerForeignId = null);
