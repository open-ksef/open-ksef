using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Policies;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.Tests.TestSupport;
using OpenKSeF.Invoices.Domain.Validation;
using OpenKSeF.Invoices.Domain.Validation.Orchestrators;
using OpenKSeF.Invoices.Domain.Validation.Rules;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Tests.ValidationTests;

public class AdvanceFinalValidationRuleTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void InvVal070_Approve_ReturnsError_WhenAdvanceInvoiceAmountIsZero()
    {
        var invoice = MakeInvoice(DocumentKind.AdvanceInvoice, grossAmount: 0m);
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-070", message.Code);
    }

    [Fact]
    public void InvVal071_Approve_ReturnsError_WhenFinalInvoiceHasNoAdvanceReferences()
    {
        var invoice = MakeInvoice(DocumentKind.FinalInvoice, grossAmount: 123m);
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-071", message.Code);
    }

    [Fact]
    public void InvVal072_Approve_ReturnsError_WhenAdvanceSettlementsOverflow()
    {
        var invoice = MakeInvoice(DocumentKind.FinalInvoice, grossAmount: 1000m);
        invoice.AddAdvanceDocumentId(InvoiceId.New());
        invoice.AddAdvanceAllocation(new AdvanceAllocation(InvoiceId.New(), new DocumentNumber("ADV/1"), new Money(1200m, Pln)));
        var service = CreateApprovalService();

        var result = service.Validate(
            invoice,
            MakeContext(
                ValidationStage.Approve,
                items: new Dictionary<string, object?> { ["AdvanceSettlementPolicy"] = new DefaultAdvanceSettlementPolicy() }));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-072", message.Code);
    }

    [Fact]
    public void InvVal073_Approve_ReturnsError_WhenAdvanceReferenceContextDiffers()
    {
        var advanceId = InvoiceId.New();
        var invoice = MakeInvoice(DocumentKind.FinalInvoice, grossAmount: 1000m);
        invoice.AddAdvanceDocumentId(advanceId);
        invoice.AddAdvanceAllocation(new AdvanceAllocation(advanceId, new DocumentNumber("ADV/1"), new Money(500m, Pln)));
        var service = CreateApprovalService();

        var result = service.Validate(
            invoice,
            MakeContext(
                ValidationStage.Approve,
                items: new Dictionary<string, object?>
                {
                    ["AdvanceReferenceContexts"] = new Dictionary<InvoiceId, AdvanceReferenceContext>
                    {
                        [advanceId] = new(
                            SellerNip: "1234567890",
                            BuyerNip: "1111111111",
                            Currency: "PLN")
                    },
                    ["AdvanceSettlementPolicy"] = new DefaultAdvanceSettlementPolicy()
                }));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-073", message.Code);
    }

    [Fact]
    public void AdvanceAndFinalRules_ReturnNoMessages_WhenAdvanceDataIsConsistent()
    {
        var advanceId = InvoiceId.New();
        var invoice = MakeInvoice(DocumentKind.FinalInvoice, grossAmount: 1000m);
        invoice.AddAdvanceDocumentId(advanceId);
        invoice.AddAdvanceAllocation(new AdvanceAllocation(advanceId, new DocumentNumber("ADV/1"), new Money(500m, Pln)));
        var service = CreateApprovalService();

        var result = service.Validate(
            invoice,
            MakeContext(
                ValidationStage.Approve,
                items: new Dictionary<string, object?>
                {
                    ["AdvanceReferenceContexts"] = new Dictionary<InvoiceId, AdvanceReferenceContext>
                    {
                        [advanceId] = new(
                            SellerNip: "1234567890",
                            BuyerNip: "9876543210",
                            Currency: "PLN")
                    },
                    ["AdvanceSettlementPolicy"] = new DefaultAdvanceSettlementPolicy()
                }));

        Assert.Empty(result.Messages);
    }

    private static ApprovalValidationService CreateApprovalService() =>
        new(
            [
                new AdvanceInvoiceAmountMustBePositiveRule(),
                new FinalInvoiceRequiresAdvanceReferencesRule(),
                new FinalInvoiceAdvanceSettlementsMustNotOverflowRule(),
                new AdvanceReferencesMustMatchCommercialContextRule()
            ],
            []);

    private static ValidationContext MakeContext(
        ValidationStage stage,
        IReadOnlyDictionary<string, object?>? items = null) =>
        new(
            stage,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: false,
            IsNumberAssigned: false,
            Items: items ?? new Dictionary<string, object?>());

    private static Invoice MakeInvoice(DocumentKind kind, decimal grossAmount)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            kind,
            Seller,
            Buyer,
            Pln,
            new DateTime(2026, 04, 10),
            KsefSubmissionRequirement.Required);

        if (grossAmount > 0m)
        {
            invoice.AddLine(InvoiceLine.Create(
                1,
                "Service",
                1m,
                new Money(grossAmount / 1.23m, Pln),
                PricingMode.Net,
                VatRate.OfPercentage(new Percentage(23))));
            invoice.RecalculateTotals();
            return invoice;
        }

        invoice.AddLine(InvoiceLine.Create(
            1,
            "Zero",
            0m,
            Money.Zero(Pln),
            PricingMode.Net,
            VatRate.Zero));
        invoice.RecalculateTotals();
        return invoice;
    }
}
