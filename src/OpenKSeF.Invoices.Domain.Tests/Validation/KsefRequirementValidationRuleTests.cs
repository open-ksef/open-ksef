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

public class KsefRequirementValidationRuleTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));

    [Fact]
    public void InvVal090_SendToKsef_ReturnsError_WhenBuyerClassificationIsUnknownAndRequirementDependsOnIt()
    {
        var invoice = MakeInvoice(DocumentKind.VatInvoice, new BuyerSnapshot(new PartyName("Buyer"), BuyerKind.Unknown), KsefSubmissionRequirement.Required);
        var service = CreateService();

        var result = service.Validate(
            invoice,
            payload: null,
            MakeContext(
                new Dictionary<string, object?>
                {
                    ["KsefObligationDependsOnBuyerClassification"] = true,
                    ["KsefConfigAvailable"] = true
                }));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-090", message.Code);
    }

    [Fact]
    public void InvVal091_SendToKsef_ReturnsError_WhenDocumentKindIsNotEligible()
    {
        var invoice = MakeInvoice(DocumentKind.Proforma, BusinessBuyer(), KsefSubmissionRequirement.Forbidden);
        var service = CreateService();

        var result = service.Validate(
            invoice,
            payload: null,
            MakeContext(new Dictionary<string, object?> { ["KsefConfigAvailable"] = true }));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-091", message.Code);
    }

    [Fact]
    public void InvVal092_SendToKsef_ReturnsError_WhenKsefConfigMissing()
    {
        var invoice = MakeInvoice(DocumentKind.VatInvoice, BusinessBuyer(), KsefSubmissionRequirement.Required);
        var service = CreateService();

        var result = service.Validate(
            invoice,
            payload: null,
            MakeContext(new Dictionary<string, object?> { ["KsefConfigAvailable"] = false }));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-092", message.Code);
    }

    [Fact]
    public void InvVal093_SendToKsef_ReturnsError_WhenDocumentAlreadyAcceptedByKsef()
    {
        var invoice = MakeInvoice(DocumentKind.VatInvoice, BusinessBuyer(), KsefSubmissionRequirement.Required);
        invoice.Approve(DateTime.UtcNow);
        invoice.SubmitToKsef(DateTime.UtcNow);
        invoice.AcceptByKsef(new KsefIdentifiers("KS/1", "REF/1"), DateTime.UtcNow);
        var service = CreateService();

        var result = service.Validate(
            invoice,
            payload: null,
            MakeContext(new Dictionary<string, object?> { ["KsefConfigAvailable"] = true }));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-093", message.Code);
    }

    [Fact]
    public void KsefRequirementRules_ReturnNoMessages_WhenSubmissionIsAllowed()
    {
        var invoice = MakeInvoice(DocumentKind.VatInvoice, BusinessBuyer(), KsefSubmissionRequirement.Required);
        invoice.Approve(DateTime.UtcNow);
        var service = CreateService();

        var result = service.Validate(
            invoice,
            payload: null,
            MakeContext(
                new Dictionary<string, object?>
                {
                    ["KsefObligationDependsOnBuyerClassification"] = true,
                    ["KsefConfigAvailable"] = true
                }));

        Assert.Empty(result.Messages);
    }

    private static KsefSubmissionValidationService CreateService() =>
        new(
            [
                new BuyerClassificationMustResolveForKsefRule(),
                new DocumentKindMustBeKsefEligibleRule(),
                new KsefConfigurationMustBeAvailableRule(),
                new AcceptedKsefDocumentCannotBeResentRule()
            ],
            [],
            []);

    private static ValidationContext MakeContext(IReadOnlyDictionary<string, object?> items) =>
        new(
            ValidationStage.SendToKsef,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: true,
            IsNumberAssigned: true,
            Items: items);

    private static BuyerSnapshot BusinessBuyer() =>
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    private static Invoice MakeInvoice(DocumentKind kind, BuyerSnapshot buyer, KsefSubmissionRequirement requirement)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            kind,
            Seller,
            buyer,
            Pln,
            new DateTime(2026, 04, 10),
            requirement);

        invoice.AddLine(InvoiceLine.Create(
            1,
            "Service",
            1m,
            new Money(100m, Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        return invoice;
    }
}
