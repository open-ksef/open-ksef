namespace OpenKSeF.Domain.Entities;

/// <summary>
/// EF Core persistence record for the <c>OpenKSeF.Invoices.Domain.Invoice</c> aggregate.
/// Stored in the <c>IssuedInvoices</c> table (separate from the KSeF-synced read model).
///
/// Coexistence strategy:
/// - <see cref="IssuedInvoiceRecord"/>: write-side / issuing domain — invoices the tenant creates
/// - <see cref="SyncedInvoice"/>: read-side — invoices synced from KSeF
/// Both tables live in the same database; they serve different bounded contexts.
///
/// Rollback path for the SyncedInvoice rename migration:
/// The rename migration stores the old table names in comments and provides a Down() method
/// that renames back to <c>InvoiceHeaders</c> / <c>InvoiceLines</c>.
/// </summary>
public class IssuedInvoiceRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    // Classification
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string BuyerKind { get; set; } = string.Empty;
    public string KsefSubmissionRequirement { get; set; } = string.Empty;
    public string KsefSubmissionState { get; set; } = string.Empty;

    // Parties (snapshotted at issuance)
    public string SellerName { get; set; } = string.Empty;
    public string SellerNip { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public string? BuyerNip { get; set; }

    // Dates
    public DateTime IssueDate { get; set; }
    public DateTime? SaleDate { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? SubmittedToKsefAt { get; set; }
    public DateTime? AcceptedByKsefAt { get; set; }

    // Money
    public string Currency { get; set; } = "PLN";
    public decimal TotalNet { get; set; }
    public decimal TotalVat { get; set; }
    public decimal TotalGross { get; set; }

    // Commercial
    public string? DocumentNumber { get; set; }
    public string? ExternalReference { get; set; }
    public string? PaymentMethod { get; set; }
    public string? PublicNotes { get; set; }
    public string? InternalNotes { get; set; }

    // KSeF
    public string? KsefDocumentNumber { get; set; }
    public string? KsefReferenceNumber { get; set; }
    public string? KsefRejectionReason { get; set; }
    public string? AdvanceDocumentIdsJson { get; set; }
    public string? SettledAdvanceAllocationsJson { get; set; }
    public string? DuplicateIssuancesJson { get; set; }

    // Correction
    public Guid? CorrectionOriginalInvoiceId { get; set; }
    public string? CorrectionOriginalDocumentNumber { get; set; }
    public string? CorrectionReasonKind { get; set; }
    public string? CorrectionReasonDescription { get; set; }

    // Audit
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public Tenant Tenant { get; set; } = null!;
    public ICollection<IssuedInvoiceLineRecord> Lines { get; set; } = new List<IssuedInvoiceLineRecord>();
}
