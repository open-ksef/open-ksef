namespace OpenKSeF.Portal.Services;

public sealed class InvoiceDetailModel
{
    public required Guid Id { get; init; }
    public required Guid TenantId { get; init; }
    public required string KSeFInvoiceNumber { get; init; }
    public required string KSeFReferenceNumber { get; init; }
    public required string VendorName { get; init; }
    public required string VendorNip { get; init; }
    public required DateTime IssueDate { get; init; }
    public required decimal AmountGross { get; init; }
    public required string Currency { get; init; }
    public required DateTime FirstSeenAt { get; init; }
    public required DateTime LastUpdatedAt { get; init; }
}
