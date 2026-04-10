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

public class BuyerClassificationScenarioTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));

    [Fact]
    public void Buy001_B2BInvoiceWithNip_RequiresKsef()
    {
        var invoice = MakeInvoice(new BuyerSnapshot(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210")));

        var requirement = new StandardKsefRequirementPolicy().Resolve(invoice);

        Assert.Equal(KsefSubmissionRequirement.Required, requirement);
    }

    [Fact]
    public void Buy002_B2CInvoiceWithoutNip_DoesNotRequireKsef()
    {
        var invoice = MakeInvoice(new BuyerSnapshot(new PartyName("Consumer"), BuyerKind.Consumer));

        var requirement = new StandardKsefRequirementPolicy().Resolve(invoice);

        Assert.Equal(KsefSubmissionRequirement.Optional, requirement);
        Assert.NotEqual(KsefSubmissionRequirement.Required, requirement);
    }

    [Fact]
    public void Buy003_UnknownBuyerKindInDraft_ReturnsWarningInvVal012()
    {
        var invoice = MakeInvoice(new BuyerSnapshot(new PartyName("Buyer"), BuyerKind.Unknown));

        var result = CreateDraftService().Validate(invoice, MakeDraftContext());

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-012" && m.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void Buy004_B2BWithoutNipOnApproval_BlocksWithInvVal013()
    {
        var invoice = MakeInvoice(new BuyerSnapshot(new PartyName("Buyer"), BuyerKind.Business));

        var result = CreateApprovalService().Validate(invoice, MakeApprovalContext());

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-013");
    }

    [Fact]
    public void Buy005_UnresolvedKsefObligationAtSendStage_BlocksWithInvVal090()
    {
        var invoice = MakeInvoice(new BuyerSnapshot(new PartyName("Buyer"), BuyerKind.Unknown));
        invoice.Approve(new DateTime(2026, 04, 10, 10, 0, 0, DateTimeKind.Utc));

        var result = CreateSubmissionService().Validate(
            invoice,
            payload: null,
            MakeSendContext(new Dictionary<string, object?>
            {
                ["KsefObligationDependsOnBuyerClassification"] = true,
                ["KsefConfigAvailable"] = true
            }));

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-090");
    }

    private static DraftValidationService CreateDraftService() =>
        new([new BuyerKindMustBeResolvedRule()], []);

    private static ApprovalValidationService CreateApprovalService() =>
        new([new BusinessBuyerRequiresNipRule()], []);

    private static KsefSubmissionValidationService CreateSubmissionService() =>
        new([new BuyerClassificationMustResolveForKsefRule()], [], []);

    private static ValidationContext MakeDraftContext() =>
        new(
            ValidationStage.Draft,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: false,
            IsNumberAssigned: false,
            Items: new Dictionary<string, object?>());

    private static ValidationContext MakeApprovalContext() =>
        new(
            ValidationStage.Approve,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: false,
            IsNumberAssigned: true,
            Items: new Dictionary<string, object?>());

    private static ValidationContext MakeSendContext(IReadOnlyDictionary<string, object?> items) =>
        new(
            ValidationStage.SendToKsef,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: true,
            IsNumberAssigned: true,
            Items: items);

    private static Invoice MakeInvoice(BuyerSnapshot buyer)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            DocumentKind.VatInvoice,
            Seller,
            buyer,
            Pln,
            new DateTime(2026, 04, 10),
            KsefSubmissionRequirement.Required);

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
