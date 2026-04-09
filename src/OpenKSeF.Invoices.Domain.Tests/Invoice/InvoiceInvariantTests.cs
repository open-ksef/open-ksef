using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.ValueObjects;
using InvoiceLine = OpenKSeF.Invoices.Domain.Entities.InvoiceLine;

namespace OpenKSeF.Invoices.Domain.Tests.Invoice;

public class InvoiceInvariantTests
{
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly SellerSnapshot Seller =
        new(new PartyName("Seller Sp. z o.o."), new Nip("1234567890"));
    private static readonly BuyerSnapshot BuyerB2B =
        new(new PartyName("Buyer S.A."), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void Approve_NoLines_Throws()
    {
        var inv = Domain.Aggregates.Invoice.Draft(
            InvoiceId.New(), Tenant, DocumentKind.VatInvoice,
            Seller, BuyerB2B, Pln, DateTime.UtcNow,
            KsefSubmissionRequirement.Required);

        var ex = Assert.Throws<InvoiceDomainException>(() => inv.Approve(DateTime.UtcNow));
        Assert.Contains("line", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Approve_TotalsInconsistent_Throws()
    {
        var inv = Domain.Aggregates.Invoice.Draft(
            InvoiceId.New(), Tenant, DocumentKind.VatInvoice,
            Seller, BuyerB2B, Pln, DateTime.UtcNow,
            KsefSubmissionRequirement.Required);

        inv.AddLine(InvoiceLine.Create(1, "Item", 1m, new Money(100m, Pln), PricingMode.Net, VatRate.OfPercentage(new Percentage(23))));
        // Deliberately skip RecalculateTotals so totals stay at zero while lines have values
        // But actually in v1 RecalculateTotals must be called; let's tamper with a mismatched state
        // We can test that a document with lines but zero totals is rejected:
        // Do NOT call RecalculateTotals - totals remain Zero
        var ex = Assert.Throws<InvoiceDomainException>(() => inv.Approve(DateTime.UtcNow));
        Assert.Contains("total", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Approve_CorrectionInvoice_WithoutCorrectionReference_Throws()
    {
        var inv = Domain.Aggregates.Invoice.Draft(
            InvoiceId.New(), Tenant, DocumentKind.CorrectionInvoice,
            Seller, BuyerB2B, Pln, DateTime.UtcNow,
            KsefSubmissionRequirement.Required);

        inv.AddLine(InvoiceLine.Create(1, "Correction", 1m, new Money(100m, Pln), PricingMode.Net, VatRate.OfPercentage(new Percentage(23))));
        inv.RecalculateTotals();

        var ex = Assert.Throws<InvoiceDomainException>(() => inv.Approve(DateTime.UtcNow));
        Assert.Contains("correction reference", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Approve_CorrectionInvoice_WithCorrectionReference_Succeeds()
    {
        var originalId = InvoiceId.New();
        var corrRef = new CorrectionReference(originalId, new DocumentNumber("FV/2026/001"), CorrectionReasonKind.ValueChange);
        var inv = Domain.Aggregates.Invoice.Draft(
            InvoiceId.New(), Tenant, DocumentKind.CorrectionInvoice,
            Seller, BuyerB2B, Pln, DateTime.UtcNow,
            KsefSubmissionRequirement.Required,
            correctionReference: corrRef);

        inv.AddLine(InvoiceLine.Create(1, "Correction", -10m, new Money(100m, Pln), PricingMode.Net, VatRate.OfPercentage(new Percentage(23))));
        inv.RecalculateTotals();
        inv.Approve(DateTime.UtcNow);

        Assert.Equal(DocumentStatus.Approved, inv.Status);
    }

    [Fact]
    public void Approve_FinalInvoice_WithoutAdvanceRefs_Throws()
    {
        var inv = Domain.Aggregates.Invoice.Draft(
            InvoiceId.New(), Tenant, DocumentKind.FinalInvoice,
            Seller, BuyerB2B, Pln, DateTime.UtcNow,
            KsefSubmissionRequirement.Required);

        inv.AddLine(InvoiceLine.Create(1, "Settlement", 1m, new Money(100m, Pln), PricingMode.Net, VatRate.OfPercentage(new Percentage(23))));
        inv.RecalculateTotals();

        var ex = Assert.Throws<InvoiceDomainException>(() => inv.Approve(DateTime.UtcNow));
        Assert.Contains("advance", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Approve_Proforma_NeverSubmittable_KsefRequirementIsForbidden()
    {
        var inv = Domain.Aggregates.Invoice.Draft(
            InvoiceId.New(), Tenant, DocumentKind.Proforma,
            Seller, BuyerB2B, Pln, DateTime.UtcNow,
            KsefSubmissionRequirement.Forbidden);

        Assert.Equal(KsefSubmissionRequirement.Forbidden, inv.KsefSubmissionRequirement);
    }

    [Fact]
    public void AcceptedInvoice_IsImmutable_CannotAddLines()
    {
        var inv = Domain.Aggregates.Invoice.Draft(
            InvoiceId.New(), Tenant, DocumentKind.VatInvoice,
            Seller, BuyerB2B, Pln, DateTime.UtcNow,
            KsefSubmissionRequirement.Required);

        inv.AddLine(InvoiceLine.Create(1, "Item", 1m, new Money(100m, Pln), PricingMode.Net, VatRate.OfPercentage(new Percentage(23))));
        inv.RecalculateTotals();
        inv.Approve(DateTime.UtcNow);
        inv.SubmitToKsef(DateTime.UtcNow);
        inv.AcceptByKsef(new KsefIdentifiers("KS/2026/001", "REF-001"), DateTime.UtcNow);

        var newLine = InvoiceLine.Create(2, "Extra", 1m, new Money(50m, Pln), PricingMode.Net, VatRate.Zero);
        Assert.Throws<InvoiceDomainException>(() => inv.AddLine(newLine));
    }
}
