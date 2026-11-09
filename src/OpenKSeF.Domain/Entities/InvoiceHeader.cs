using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Invoicing;

namespace OpenKSeF.Domain.Entities;

public class InvoiceHeader
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string KSeFInvoiceNumber { get; set; }
    public required string KSeFReferenceNumber { get; set; }
    public string? InvoiceNumber { get; set; }
    public required string VendorName { get; set; }
    public required string VendorNip { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerNip { get; set; }
    public string? BuyerIdType { get; set; }
    public string? BuyerForeignId { get; set; }
    public InvoiceDirection Direction { get; set; }
    public decimal AmountNet { get; set; }
    public decimal AmountVat { get; set; }
    public decimal AmountGross { get; set; }
    public string Currency { get; set; } = "PLN";
    public DateTime IssueDate { get; set; }
    public DateTime? AcquisitionDate { get; set; }
    public string? InvoiceType { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastUpdatedAt { get; set; }
    public string? VendorBankAccount { get; set; }
    public DateOnly? DueDate { get; set; }
    public bool IsPaid { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? XmlFetchedAt { get; set; }
    public DateTime? XmlAttemptedAt { get; set; }
    public SettlementState? Settlement { get; set; }
    public decimal? SettlementAmount { get; set; }
    public bool? SplitPayment { get; set; }
    /// <summary><c>DateOnly.MinValue</c>: <c>Zaplacono = 1</c> without a readable <c>DataZaplaty</c>, still paid.</summary>
    public DateOnly? SellerPaidOn { get; set; }
    public short? XmlFailedReads { get; set; }
    public short? PartialPayment { get; set; }
    public string? FactorBankAccount { get; set; }
    public string? FactorNip { get; set; }
    public string? FactorName { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();

    public static bool IsOverdue(bool isPaid, DateOnly? dueDate, DateTimeOffset utcNow) =>
        !isPaid && dueDate < IssuedInvoice.PolandDate(utcNow);
}
