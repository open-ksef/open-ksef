using System.Reflection;
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

public class VatScenarioTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void Vat001_StandardVatInvoiceInPln_AllowsApproval()
    {
        var invoice = MakeInvoice(CreateLine());

        var result = CreateApprovalService().Validate(invoice, MakeApprovalContext());

        Assert.Empty(result.Messages);
    }

    [Fact]
    public void Vat002_MissingVatTreatmentOnLine_BlocksApprovalWithInvVal060()
    {
        var invoice = MakeInvoice(CreateLine());
        SetProperty(invoice.LineItems.Single(), nameof(InvoiceLine.VatRate), null);

        var result = CreateLineApprovalService().Validate(invoice, MakeApprovalContext());

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-060");
    }

    [Fact]
    public void Vat003_ExemptLineWithoutLegalReason_BlocksApprovalWithInvVal061()
    {
        var invoice = MakeInvoice(CreateLine());
        var line = invoice.LineItems.Single();
        SetProperty(line, nameof(InvoiceLine.VatRate), null);
        SetProperty(line, nameof(InvoiceLine.VatClassification), new VatClassification("EXEMPT"));

        var result = CreateLineApprovalService().Validate(invoice, MakeApprovalContext());

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-061");
    }

    [Fact]
    public void Vat004_ExemptLineWithNonZeroVat_BlocksApprovalWithInvVal062()
    {
        var line = InvoiceLine.Create(
            1,
            "Service",
            1m,
            new Money(100m, Pln),
            PricingMode.Net,
            VatRate.OfExemption(new TaxExemptionReason("zw")));
        SetProperty(line, nameof(InvoiceLine.VatAmount), new Money(10m, Pln));
        SetProperty(line, nameof(InvoiceLine.GrossAmount), new Money(110m, Pln));
        var invoice = MakeInvoice(line);

        var result = CreateLineApprovalService().Validate(invoice, MakeApprovalContext());

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-062");
    }

    [Fact]
    public void Vat005_VatSummaryMismatch_BlocksApprovalWithInvVal063()
    {
        var invoice = MakeInvoice(CreateLine());
        var line = invoice.LineItems.Single();
        SetBackingField(invoice, nameof(Invoice.VatBreakdown), new List<VatSummary>
        {
            new(line.VatRate, new Money(100m, Pln), new Money(22.99m, Pln), new Money(122.99m, Pln))
        });

        var result = CreateSummaryApprovalService().Validate(invoice, MakeApprovalContext());

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-063");
    }

    [Fact]
    public void Vat006_SplitPaymentMarkerVisibility_ReturnsDraftWarningInvVal064()
    {
        var invoice = MakeInvoice(CreateLine());

        var result = CreateDraftService().Validate(
            invoice,
            MakeDraftContext(new Dictionary<string, object?> { ["SplitPaymentRequired"] = true }));

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-064");
    }

    private static ApprovalValidationService CreateApprovalService() =>
        new(
            [
                new VatSummaryMustMatchLinesRule()
            ],
            [
                new VatTreatmentRequiredRule(),
                new ExemptLineRequiresReasonRule(),
                new ExemptLineCannotHavePositiveVatRule()
            ]);

    private static ApprovalValidationService CreateLineApprovalService() =>
        new([], [
            new VatTreatmentRequiredRule(),
            new ExemptLineRequiresReasonRule(),
            new ExemptLineCannotHavePositiveVatRule()
        ]);

    private static ApprovalValidationService CreateSummaryApprovalService() =>
        new([new VatSummaryMustMatchLinesRule()], []);

    private static DraftValidationService CreateDraftService() =>
        new([new SplitPaymentRequiresVisibleMarkerRule()], []);

    private static ValidationContext MakeApprovalContext() =>
        new(
            ValidationStage.Approve,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: false,
            IsNumberAssigned: true,
            Items: new Dictionary<string, object?>());

    private static ValidationContext MakeDraftContext(IReadOnlyDictionary<string, object?> items) =>
        new(
            ValidationStage.Draft,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: false,
            IsNumberAssigned: true,
            Items: items);

    private static Invoice MakeInvoice(InvoiceLine line)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            DocumentKind.VatInvoice,
            Seller,
            Buyer,
            Pln,
            new DateTime(2026, 04, 10),
            KsefSubmissionRequirement.Required);
        invoice.AddLine(line);
        invoice.RecalculateTotals();
        return invoice;
    }

    private static InvoiceLine CreateLine() =>
        InvoiceLine.Create(
            1,
            "Service",
            1m,
            new Money(100m, Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23)));

    private static void SetProperty(object target, string propertyName, object? value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        property!.SetValue(target, value);
    }

    private static void SetBackingField(object target, string propertyName, object? value)
    {
        var field = target.GetType().GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }
}
