namespace OpenKSeF.Domain.Events;

public record NewInvoiceDetectedEvent(
    Guid TenantId,
    Guid InvoiceId,
    string VendorName,
    string? InvoiceNumber,
    decimal Amount,
    string Currency);
