using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.Tests.TestSupport;
using OpenKSeF.Invoices.Domain.Validation;
using OpenKSeF.Invoices.Domain.Validation.Orchestrators;
using OpenKSeF.Invoices.Domain.Validation.Rules;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Tests.ValidationTests;

public class FinalInvoiceScenarioTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void Fin001_FinalInvoiceSettlingTwoAdvances_ApprovalSucceeds()
    {
        var id1 = InvoiceId.New();
        var id2 = InvoiceId.New();
        var invoice = MakeFinalInvoice(1000m);
        invoice.AddAdvanceDocumentId(id1);
        invoice.AddAdvanceDocumentId(id2);
        invoice.AddAdvanceAllocation(new AdvanceAllocation(id1, new DocumentNumber("ADV/1"), new Money(300m, Pln)));
        invoice.AddAdvanceAllocation(new AdvanceAllocation(id2, new DocumentNumber("ADV/2"), new Money(400m, Pln)));

        var result = CreateApprovalService().Validate(
            invoice,
            MakeContext(new Dictionary<string, object?>
            {
                ["AdvanceSettlementPolicy"] = new DefaultAdvanceSettlementPolicy(),
                ["AdvanceReferenceContexts"] = new Dictionary<InvoiceId, AdvanceReferenceContext>
                {
                    [id1] = new("1234567890", "9876543210", "PLN"),
                    [id2] = new("1234567890", "9876543210", "PLN")
                }
            }));

        Assert.Empty(result.Messages);
    }

    [Fact]
    public void Fin002_FinalInvoiceWithoutAdvanceReferences_BlocksApprovalWithInvVal071()
    {
        var invoice = MakeFinalInvoice(1000m);

        var result = CreateApprovalService().Validate(invoice, MakeContext());

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-071");
    }

    [Fact]
    public void Fin003_AdvanceSettlementOverflow_BlocksApprovalWithInvVal072()
    {
        var id = InvoiceId.New();
        var invoice = MakeFinalInvoice(1000m);
        invoice.AddAdvanceDocumentId(id);
        invoice.AddAdvanceAllocation(new AdvanceAllocation(id, new DocumentNumber("ADV/1"), new Money(1200m, Pln)));

        var result = CreateApprovalService().Validate(
            invoice,
            MakeContext(new Dictionary<string, object?> { ["AdvanceSettlementPolicy"] = new DefaultAdvanceSettlementPolicy() }));

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-072");
    }

    [Fact]
    public void Fin004_AdvanceFromDifferentBuyer_BlocksApprovalWithInvVal073()
    {
        var id = InvoiceId.New();
        var invoice = MakeFinalInvoice(1000m);
        invoice.AddAdvanceDocumentId(id);
        invoice.AddAdvanceAllocation(new AdvanceAllocation(id, new DocumentNumber("ADV/1"), new Money(500m, Pln)));

        var result = CreateApprovalService().Validate(
            invoice,
            MakeContext(new Dictionary<string, object?>
            {
                ["AdvanceSettlementPolicy"] = new DefaultAdvanceSettlementPolicy(),
                ["AdvanceReferenceContexts"] = new Dictionary<InvoiceId, AdvanceReferenceContext>
                {
                    [id] = new("1234567890", "1111111111", "PLN")
                }
            }));

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-073");
    }

    private static ApprovalValidationService CreateApprovalService() =>
        new(
            [
                new FinalInvoiceRequiresAdvanceReferencesRule(),
                new FinalInvoiceAdvanceSettlementsMustNotOverflowRule(),
                new AdvanceReferencesMustMatchCommercialContextRule()
            ],
            []);

    private static ValidationContext MakeContext(IReadOnlyDictionary<string, object?>? items = null) =>
        new(
            ValidationStage.Approve,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: false,
            IsNumberAssigned: true,
            Items: items ?? new Dictionary<string, object?>());

    private static Invoice MakeFinalInvoice(decimal grossAmount)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            DocumentKind.FinalInvoice,
            Seller,
            Buyer,
            Pln,
            new DateTime(2026, 04, 10),
            KsefSubmissionRequirement.Required);

        invoice.AddLine(InvoiceLine.Create(
            1,
            "Settlement",
            1m,
            new Money(grossAmount / 1.23m, Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        return invoice;
    }
}
