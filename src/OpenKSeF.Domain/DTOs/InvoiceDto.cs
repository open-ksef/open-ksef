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
    string? VendorBankAccount = null);
