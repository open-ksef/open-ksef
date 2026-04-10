using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Events;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Aggregates;

/// <summary>
/// Aggregate root for the invoice-issuing domain.
/// Encapsulates the full lifecycle: Draft → Approved → SubmittedToKsef → AcceptedByKsef/RejectedByKsef.
/// No EF or infrastructure dependencies.
/// </summary>
public sealed class Invoice
{
    private readonly List<InvoiceLine> _lines = new();
    private readonly List<InvoiceId> _advanceDocumentIds = new();
    private readonly List<AdvanceAllocation> _settledAllocations = new();
    private readonly List<DuplicateMetadata> _duplicateIssuances = new();
    private readonly List<IDomainEvent> _domainEvents = new();

    // ── Identity ──────────────────────────────────────────────────────────────
    public InvoiceId Id { get; private init; } = null!;
    public TenantId TenantId { get; private init; } = null!;
    public DocumentNumber? DocumentNumber { get; private set; }
    public string? ExternalReference { get; private set; }

    // ── Classification ────────────────────────────────────────────────────────
    public DocumentKind Kind { get; private init; }
    public DocumentStatus Status { get; private set; }
    public BuyerKind BuyerKind { get; private set; }
    public KsefSubmissionRequirement KsefSubmissionRequirement { get; private set; }
    public KsefSubmissionState KsefSubmissionState { get; private set; }

    // ── Parties ───────────────────────────────────────────────────────────────
    public SellerSnapshot Seller { get; private init; } = null!;
    public BuyerSnapshot Buyer { get; private set; } = null!;

    // ── Dates ─────────────────────────────────────────────────────────────────
    public DateTime IssueDate { get; private set; }
    public DateTime? SaleDate { get; private set; }
    public DateTime? DueDate { get; private set; }
    public DateTime? ApprovedAt { get; private set; }
    public DateTime? SubmittedToKsefAt { get; private set; }
    public DateTime? AcceptedByKsefAt { get; private set; }

    // ── Money ─────────────────────────────────────────────────────────────────
    public CurrencyCode Currency { get; private init; } = null!;
    public DocumentTotals Totals { get; private set; } = null!;
    public IReadOnlyList<VatSummary> VatBreakdown { get; private set; } = Array.Empty<VatSummary>();

    // ── Commercial ────────────────────────────────────────────────────────────
    public string? PaymentMethod { get; private set; }
    public BankAccountNumber? BankAccount { get; private set; }
    public string? PublicNotes { get; private set; }
    public string? InternalNotes { get; private set; }
    public IReadOnlyList<InvoiceLine> LineItems => _lines.AsReadOnly();

    // ── KSeF ──────────────────────────────────────────────────────────────────
    public KsefIdentifiers? KsefIdentifiers { get; private set; }
    public string? KsefRejectionReason { get; private set; }

    // ── Relations ─────────────────────────────────────────────────────────────
    public CorrectionReference? CorrectionReference { get; private init; }
    public IReadOnlyList<InvoiceId> AdvanceDocumentIds => _advanceDocumentIds.AsReadOnly();
    public IReadOnlyList<AdvanceAllocation> SettledAdvanceAllocations => _settledAllocations.AsReadOnly();
    public IReadOnlyList<DuplicateMetadata> DuplicateIssuances => _duplicateIssuances.AsReadOnly();

    // ── Domain events ─────────────────────────────────────────────────────────
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    // ── Constructor / factory ─────────────────────────────────────────────────
    private Invoice() { }

    /// <summary>Creates a new invoice in <see cref="DocumentStatus.Draft"/> state.</summary>
    public static Invoice Draft(
        InvoiceId id,
        TenantId tenantId,
        DocumentKind kind,
        SellerSnapshot seller,
        BuyerSnapshot buyer,
        CurrencyCode currency,
        DateTime issueDate,
        KsefSubmissionRequirement ksefRequirement,
        DocumentNumber? documentNumber = null,
        CorrectionReference? correctionReference = null,
        string? externalReference = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(seller);
        ArgumentNullException.ThrowIfNull(buyer);
        ArgumentNullException.ThrowIfNull(currency);

        var invoice = new Invoice
        {
            Id = id,
            TenantId = tenantId,
            Kind = kind,
            Status = DocumentStatus.Draft,
            BuyerKind = buyer.Kind,
            KsefSubmissionRequirement = kind == DocumentKind.Proforma
                ? KsefSubmissionRequirement.Forbidden
                : ksefRequirement,
            KsefSubmissionState = KsefSubmissionState.NotPlanned,
            Seller = seller,
            Buyer = buyer,
            Currency = currency,
            IssueDate = issueDate,
            DocumentNumber = documentNumber,
            CorrectionReference = correctionReference,
            ExternalReference = externalReference,
            Totals = DocumentTotals.Zero(currency)
        };

        invoice.Raise(new InvoiceDrafted(id, DateTime.UtcNow));
        return invoice;
    }

    // ── Mutation ──────────────────────────────────────────────────────────────

    public void AddLine(InvoiceLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsureNotImmutable();
        _lines.Add(line);
    }

    public void AddAdvanceDocumentId(InvoiceId advanceId)
    {
        ArgumentNullException.ThrowIfNull(advanceId);
        EnsureNotImmutable();
        if (!_advanceDocumentIds.Contains(advanceId))
            _advanceDocumentIds.Add(advanceId);
    }

    public void AddAdvanceAllocation(AdvanceAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        EnsureNotImmutable();
        _settledAllocations.Add(allocation);
    }

    public void SetDocumentNumber(DocumentNumber number)
    {
        ArgumentNullException.ThrowIfNull(number);
        EnsureNotImmutable();
        DocumentNumber = number;
    }

    public void SetIssueDates(DateTime issueDate, DateTime? saleDate = null, DateTime? dueDate = null)
    {
        EnsureNotImmutable();
        IssueDate = issueDate;
        SaleDate = saleDate;
        DueDate = dueDate;
    }

    public void SetCommercialData(string? paymentMethod = null, string? publicNotes = null, string? internalNotes = null)
    {
        EnsureNotImmutable();
        PaymentMethod = paymentMethod;
        PublicNotes = publicNotes;
        InternalNotes = internalNotes;
    }

    public void RecordDuplicateIssue(DateTime issuedAt, string? issuedBy = null)
    {
        if (Status != DocumentStatus.AcceptedByKsef)
            throw new InvoiceDomainException(
                "Duplicate issuance is available only for invoices accepted by KSeF.");

        _duplicateIssuances.Add(new DuplicateMetadata(issuedAt, issuedBy));
        Raise(new InvoiceDuplicateIssued(Id, issuedAt));
    }

    /// <summary>
    /// Recalculates document totals and VAT breakdown from line items.
    /// Must be called after adding or modifying lines.
    /// </summary>
    public void RecalculateTotals()
    {
        var net = 0m;
        var vat = 0m;
        var gross = 0m;

        var vatGroups = new Dictionary<VatRate, (decimal net, decimal vat, decimal gross)>(ReferenceEqualityComparer.Instance);

        foreach (var line in _lines)
        {
            net += line.NetAmount.Amount;
            vat += line.VatAmount.Amount;
            gross += line.GrossAmount.Amount;

            // Group by VAT rate instance (or structural equality key)
            var key = FindOrCreateRateKey(vatGroups, line.VatRate);
            var (gn, gv, gg) = vatGroups[key];
            vatGroups[key] = (gn + line.NetAmount.Amount, gv + line.VatAmount.Amount, gg + line.GrossAmount.Amount);
        }

        Totals = new DocumentTotals(
            new Money(Math.Round(net, 2, MidpointRounding.AwayFromZero), Currency),
            new Money(Math.Round(vat, 2, MidpointRounding.AwayFromZero), Currency),
            new Money(Math.Round(gross, 2, MidpointRounding.AwayFromZero), Currency));

        VatBreakdown = vatGroups
            .Select(kv => new VatSummary(
                kv.Key,
                new Money(Math.Round(kv.Value.net, 2, MidpointRounding.AwayFromZero), Currency),
                new Money(Math.Round(kv.Value.vat, 2, MidpointRounding.AwayFromZero), Currency),
                new Money(Math.Round(kv.Value.gross, 2, MidpointRounding.AwayFromZero), Currency)))
            .ToList();
    }

    // ── State machine ─────────────────────────────────────────────────────────

    /// <summary>Transitions from Draft (or RejectedByKsef) to Approved.</summary>
    public void Approve(DateTime now)
    {
        if (Status is not (DocumentStatus.Draft or DocumentStatus.RejectedByKsef))
            throw new InvoiceDomainException(
                $"Cannot approve invoice in state {Status}. Expected Draft or RejectedByKsef.");

        EnforceApprovalInvariants();

        Status = DocumentStatus.Approved;
        ApprovedAt = now;
        KsefSubmissionState = KsefSubmissionRequirement == KsefSubmissionRequirement.Required
            ? KsefSubmissionState.Ready
            : KsefSubmissionState.NotPlanned;

        Raise(new InvoiceApproved(Id, now));
    }

    /// <summary>Transitions from Approved to Draft if policy permits.</summary>
    public void Reopen(DateTime now, bool allowReopen)
    {
        if (Status != DocumentStatus.Approved)
            throw new InvoiceDomainException(
                $"Cannot reopen invoice in state {Status}. Expected Approved.");

        if (!allowReopen)
            throw new InvoiceDomainException(
                "Policy does not permit reopening an approved invoice.");

        Status = DocumentStatus.Draft;
        ApprovedAt = null;
        KsefSubmissionState = KsefSubmissionState.NotPlanned;

        Raise(new InvoiceApprovalReverted(Id, now));
    }

    /// <summary>Transitions from Approved to SubmittedToKsef.</summary>
    public void SubmitToKsef(DateTime now)
    {
        if (Status != DocumentStatus.Approved)
            throw new InvoiceDomainException(
                $"Cannot submit invoice in state {Status}. Expected Approved.");

        if (Kind == DocumentKind.Proforma || KsefSubmissionRequirement == KsefSubmissionRequirement.Forbidden)
            throw new InvoiceDomainException(
                "Proforma invoices cannot be submitted to KSeF.");

        Status = DocumentStatus.SubmittedToKsef;
        SubmittedToKsefAt = now;
        KsefSubmissionState = KsefSubmissionState.Submitted;

        Raise(new InvoiceSubmittedToKsef(Id, now));
    }

    /// <summary>Transitions from SubmittedToKsef to AcceptedByKsef.</summary>
    public void AcceptByKsef(KsefIdentifiers identifiers, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(identifiers);

        if (Status != DocumentStatus.SubmittedToKsef)
            throw new InvoiceDomainException(
                $"Cannot accept invoice in state {Status}. Expected SubmittedToKsef.");

        Status = DocumentStatus.AcceptedByKsef;
        AcceptedByKsefAt = now;
        KsefIdentifiers = identifiers;
        KsefSubmissionState = KsefSubmissionState.Accepted;

        Raise(new InvoiceAcceptedByKsef(Id, identifiers, now));
    }

    /// <summary>Transitions from SubmittedToKsef to RejectedByKsef.</summary>
    public void RejectByKsef(string rejectionReason, DateTime now)
    {
        if (Status != DocumentStatus.SubmittedToKsef)
            throw new InvoiceDomainException(
                $"Cannot reject invoice in state {Status}. Expected SubmittedToKsef.");

        Status = DocumentStatus.RejectedByKsef;
        KsefRejectionReason = rejectionReason;
        KsefSubmissionState = KsefSubmissionState.Rejected;

        Raise(new InvoiceRejectedByKsef(Id, rejectionReason, now));
    }

    // ── Invariants ────────────────────────────────────────────────────────────

    private void EnforceApprovalInvariants()
    {
        if (_lines.Count == 0)
            throw new InvoiceDomainException(
                "Invoice must have at least one line item before approval.");

        if (!Totals.IsConsistent())
            throw new InvoiceDomainException(
                "Invoice totals are not consistent with line items. Call RecalculateTotals() first.");

        // Totals must be non-zero when there are lines (zero totals indicate RecalculateTotals was not called)
        if (_lines.Count > 0 && Totals.GrossTotal.Amount == 0m
            && _lines.Any(l => l.GrossAmount.Amount != 0m))
            throw new InvoiceDomainException(
                "Invoice totals are zero but lines have non-zero amounts. Call RecalculateTotals() first.");

        if (Kind == DocumentKind.CorrectionInvoice && CorrectionReference is null)
            throw new InvoiceDomainException(
                "Correction invoice must contain a correction reference to the original document.");

        if (Kind == DocumentKind.FinalInvoice && _advanceDocumentIds.Count == 0)
            throw new InvoiceDomainException(
                "Final invoice must reference at least one advance invoice.");
    }

    private void EnsureNotImmutable()
    {
        if (Status == DocumentStatus.AcceptedByKsef)
            throw new InvoiceDomainException(
                "Invoice accepted by KSeF is immutable. Create a correction document instead.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void Raise(IDomainEvent evt) => _domainEvents.Add(evt);

    private static VatRate FindOrCreateRateKey(
        Dictionary<VatRate, (decimal, decimal, decimal)> dict,
        VatRate rate)
    {
        // VatRate uses structural equality via Equals/GetHashCode
        foreach (var key in dict.Keys)
        {
            if (key.Equals(rate)) return key;
        }
        dict[rate] = (0m, 0m, 0m);
        return rate;
    }
}
