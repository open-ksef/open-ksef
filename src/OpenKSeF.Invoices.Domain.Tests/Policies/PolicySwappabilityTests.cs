using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.Policies;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.Tests.TestSupport;
using OpenKSeF.Invoices.Domain.ValueObjects;
using InvoiceLine = OpenKSeF.Invoices.Domain.Entities.InvoiceLine;

namespace OpenKSeF.Invoices.Domain.Tests.Policies;

public class PolicySwappabilityTests
{
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly SellerSnapshot Seller =
        new(new PartyName("Seller Sp. z o.o."), new Nip("1234567890"));
    private static readonly VatRate Vat23 = VatRate.OfPercentage(new Percentage(23));

    private static Invoice MakeApprovedInvoice()
    {
        var inv = Invoice.Draft(
            InvoiceId.New(), Tenant, DocumentKind.VatInvoice,
            Seller,
            new BuyerSnapshot(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210")),
            Pln, DateTime.UtcNow, KsefSubmissionRequirement.Required);
        inv.AddLine(InvoiceLine.Create(1, "Item", 1m, new Money(100m, Pln), PricingMode.Net, Vat23));
        inv.RecalculateTotals();
        inv.Approve(DateTime.UtcNow);
        return inv;
    }

    // ── INumberingPolicy ──────────────────────────────────────────────────────

    [Fact]
    public void NumberingPolicy_Sequential_AssignsIncrementalNumbers()
    {
        var policy = new SequentialNumberingPolicy();
        var inv1 = Invoice.Draft(InvoiceId.New(), Tenant, DocumentKind.VatInvoice, Seller,
            new BuyerSnapshot(new PartyName("B"), BuyerKind.Business), Pln, DateTime.UtcNow, KsefSubmissionRequirement.Optional);
        var inv2 = Invoice.Draft(InvoiceId.New(), Tenant, DocumentKind.VatInvoice, Seller,
            new BuyerSnapshot(new PartyName("B"), BuyerKind.Business), Pln, DateTime.UtcNow, KsefSubmissionRequirement.Optional);

        var n1 = policy.AssignNumber(inv1);
        var n2 = policy.AssignNumber(inv2);

        Assert.NotEqual(n1, n2);
        Assert.Equal("TEST/0001", n1.Value);
        Assert.Equal("TEST/0002", n2.Value);
    }

    // ── IDocumentUniquenessPolicy ─────────────────────────────────────────────

    [Fact]
    public void UniquenessPolicy_DetectsDuplicate()
    {
        var policy = new InMemoryUniquenessPolicy();
        policy.Register("FV/2026/001");

        Assert.True(policy.IsDuplicate(Tenant, new DocumentNumber("FV/2026/001")));
        Assert.False(policy.IsDuplicate(Tenant, new DocumentNumber("FV/2026/002")));
    }

    // ── IBuyerClassificationPolicy ────────────────────────────────────────────

    [Fact]
    public void BuyerClassificationPolicy_NipPresent_ReturnsB2B()
    {
        var policy = new NipBasedBuyerClassificationPolicy();
        var buyer = new BuyerSnapshot(new PartyName("Corp"), BuyerKind.Unknown, new Nip("9876543210"));
        Assert.Equal(BuyerKind.Business, policy.Classify(buyer));
    }

    [Fact]
    public void BuyerClassificationPolicy_NoNip_ReturnsB2C()
    {
        var policy = new NipBasedBuyerClassificationPolicy();
        var buyer = new BuyerSnapshot(new PartyName("Jan Kowalski"), BuyerKind.Unknown);
        Assert.Equal(BuyerKind.Consumer, policy.Classify(buyer));
    }

    // ── IKsefRequirementPolicy ────────────────────────────────────────────────

    [Fact]
    public void KsefRequirementPolicy_B2BInvoice_ReturnsRequired()
    {
        var policy = new StandardKsefRequirementPolicy();
        var inv = Invoice.Draft(InvoiceId.New(), Tenant, DocumentKind.VatInvoice, Seller,
            new BuyerSnapshot(new PartyName("Corp"), BuyerKind.Business, new Nip("9876543210")),
            Pln, DateTime.UtcNow, KsefSubmissionRequirement.Required);

        Assert.Equal(KsefSubmissionRequirement.Required, policy.Resolve(inv));
    }

    [Fact]
    public void KsefRequirementPolicy_Proforma_ReturnsForbidden()
    {
        var policy = new StandardKsefRequirementPolicy();
        var inv = Invoice.Draft(InvoiceId.New(), Tenant, DocumentKind.Proforma, Seller,
            new BuyerSnapshot(new PartyName("B"), BuyerKind.Business),
            Pln, DateTime.UtcNow, KsefSubmissionRequirement.Forbidden);

        Assert.Equal(KsefSubmissionRequirement.Forbidden, policy.Resolve(inv));
    }

    // ── IVatPolicy ────────────────────────────────────────────────────────────

    [Fact]
    public void VatPolicy_StandardPolishRates_Allowed()
    {
        var policy = new PolishVatPolicy();
        Assert.Contains(23m, policy.AllowedRates);
        Assert.Contains(8m, policy.AllowedRates);
        Assert.Contains(5m, policy.AllowedRates);
        Assert.Contains(0m, policy.AllowedRates);
        Assert.DoesNotContain(7m, policy.AllowedRates);
    }

    [Fact]
    public void VatPolicy_Round_UsesAwayFromZero()
    {
        var policy = new PolishVatPolicy();
        Assert.Equal(1.23m, policy.Round(1.225m)); // rounds up
    }

    // ── ICorrectionPolicy ─────────────────────────────────────────────────────

    [Fact]
    public void CorrectionPolicy_AcceptedInvoice_CanCorrect()
    {
        var policy = new DefaultCorrectionPolicy();
        var inv = MakeApprovedInvoice();
        inv.SubmitToKsef(DateTime.UtcNow);
        inv.AcceptByKsef(new KsefIdentifiers("KS/2026/001", "REF-001"), DateTime.UtcNow);

        Assert.True(policy.CanCorrect(inv));
    }

    [Fact]
    public void CorrectionPolicy_Proforma_CannotCorrect()
    {
        var policy = new DefaultCorrectionPolicy();
        var proforma = Invoice.Draft(InvoiceId.New(), Tenant, DocumentKind.Proforma, Seller,
            new BuyerSnapshot(new PartyName("B"), BuyerKind.Business),
            Pln, DateTime.UtcNow, KsefSubmissionRequirement.Forbidden);

        Assert.False(policy.CanCorrect(proforma));
    }

    // ── IAdvanceSettlementPolicy ──────────────────────────────────────────────

    [Fact]
    public void AdvanceSettlementPolicy_AllocationWithinGross_IsValid()
    {
        var policy = new DefaultAdvanceSettlementPolicy();
        var finalInv = MakeApprovedInvoice(); // gross = 123
        var alloc = new AdvanceAllocation(InvoiceId.New(), new DocumentNumber("ZAL/001"), new Money(100m, Pln));

        Assert.True(policy.AreAllocationsValid(finalInv, new[] { alloc }));
    }

    [Fact]
    public void AdvanceSettlementPolicy_AllocationExceedsGross_IsInvalid()
    {
        var policy = new DefaultAdvanceSettlementPolicy();
        var finalInv = MakeApprovedInvoice(); // gross = 123
        var alloc = new AdvanceAllocation(InvoiceId.New(), new DocumentNumber("ZAL/001"), new Money(200m, Pln));

        Assert.False(policy.AreAllocationsValid(finalInv, new[] { alloc }));
    }

    // ── IApprovedEditPolicy ───────────────────────────────────────────────────

    [Fact]
    public void ApprovedEditPolicy_AlwaysAllow_ReopenSucceeds()
    {
        var policy = new AlwaysAllowReopenPolicy();
        var inv = MakeApprovedInvoice();

        inv.Reopen(DateTime.UtcNow, allowReopen: policy.CanReopen(inv));

        Assert.Equal(DocumentStatus.Draft, inv.Status);
    }

    [Fact]
    public void ApprovedEditPolicy_NeverAllow_ReopenThrows()
    {
        var policy = new NeverAllowReopenPolicy();
        var inv = MakeApprovedInvoice();

        Assert.Throws<InvoiceDomainException>(() =>
            inv.Reopen(DateTime.UtcNow, allowReopen: policy.CanReopen(inv)));
    }

    // ── IClock ────────────────────────────────────────────────────────────────

    [Fact]
    public void Clock_FixedClock_ReturnsDeterministicTime()
    {
        var expected = new DateTime(2026, 4, 9, 12, 0, 0, DateTimeKind.Utc);
        IClock clock = new FixedClock(expected);
        Assert.Equal(expected, clock.UtcNow);
    }
}
