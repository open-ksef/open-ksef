using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Policies;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Tests.TestSupport;

/// <summary>Sequential counter-based numbering for unit tests.</summary>
public sealed class SequentialNumberingPolicy : INumberingPolicy
{
    private int _counter;
    public bool AssignOnApproval => true;

    public DocumentNumber AssignNumber(Invoice invoice)
    {
        _counter++;
        return new DocumentNumber($"TEST/{_counter:D4}");
    }
}

/// <summary>In-memory uniqueness check backed by a HashSet.</summary>
public sealed class InMemoryUniquenessPolicy : IDocumentUniquenessPolicy
{
    private readonly HashSet<string> _used = new();
    public void Register(string number) => _used.Add(number);
    public bool IsDuplicate(TenantId tenantId, DocumentNumber number) => _used.Contains(number.Value);
}

/// <summary>Classifies buyers: presence of NIP → B2B, otherwise B2C.</summary>
public sealed class NipBasedBuyerClassificationPolicy : IBuyerClassificationPolicy
{
    public BuyerKind Classify(BuyerSnapshot buyer) =>
        buyer.Nip is not null ? BuyerKind.Business : BuyerKind.Consumer;
}

/// <summary>B2B (NIP present) → Required; Proforma → Forbidden; otherwise → Optional.</summary>
public sealed class StandardKsefRequirementPolicy : IKsefRequirementPolicy
{
    public KsefSubmissionRequirement Resolve(Invoice invoice)
    {
        if (invoice.Kind == DocumentKind.Proforma) return KsefSubmissionRequirement.Forbidden;
        return invoice.BuyerKind == BuyerKind.Business
            ? KsefSubmissionRequirement.Required
            : KsefSubmissionRequirement.Optional;
    }
}

/// <summary>Standard Polish VAT rates: 23, 8, 5, 0.</summary>
public sealed class PolishVatPolicy : IVatPolicy
{
    public IReadOnlySet<decimal> AllowedRates { get; } = new HashSet<decimal> { 23m, 8m, 5m, 0m };
    public bool IsExemptionCodeValid(string code) => !string.IsNullOrWhiteSpace(code);
    public decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Allows corrections of any AcceptedByKsef document; blocks corrections of proforma.</summary>
public sealed class DefaultCorrectionPolicy : ICorrectionPolicy
{
    public bool CanCorrect(Invoice original) =>
        original.Kind != DocumentKind.Proforma &&
        original.Status == DocumentStatus.AcceptedByKsef;
}

/// <summary>Validates that total allocated amount does not exceed final invoice gross.</summary>
public sealed class DefaultAdvanceSettlementPolicy : IAdvanceSettlementPolicy
{
    public bool AreAllocationsValid(Invoice finalInvoice, IReadOnlyList<AdvanceAllocation> allocations)
    {
        var totalAllocated = allocations.Sum(a => a.SettledAmount.Amount);
        return totalAllocated <= finalInvoice.Totals.GrossTotal.Amount;
    }
}

/// <summary>Always permits reopening approved invoices (for tests that need open editing).</summary>
public sealed class AlwaysAllowReopenPolicy : IApprovedEditPolicy
{
    public bool CanReopen(Invoice invoice) => true;
}

/// <summary>Never permits reopening approved invoices.</summary>
public sealed class NeverAllowReopenPolicy : IApprovedEditPolicy
{
    public bool CanReopen(Invoice invoice) => false;
}

/// <summary>Fixed-time clock for deterministic test assertions.</summary>
public sealed class FixedClock : IClock
{
    public FixedClock(DateTime utcNow) => UtcNow = utcNow;
    public DateTime UtcNow { get; }
}
