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

public class VatValidationRuleTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void InvVal060_Approve_ReturnsError_WhenLineHasNoVatTreatment()
    {
        var invoice = MakeInvoice(CreateLine());
        var line = invoice.LineItems.Single();
        SetProperty(line, nameof(InvoiceLine.VatRate), null);
        var service = CreateLineApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-060", message.Code);
    }

    [Fact]
    public void InvVal061_Approve_ReturnsError_WhenExemptLineHasNoReason()
    {
        var invoice = MakeInvoice(CreateLine());
        var line = invoice.LineItems.Single();
        SetProperty(line, nameof(InvoiceLine.VatRate), null);
        SetProperty(line, nameof(InvoiceLine.VatClassification), new VatClassification("EXEMPT"));
        var service = CreateLineApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-061", message.Code);
    }

    [Fact]
    public void InvVal062_Approve_ReturnsError_WhenExemptLineHasPositiveVat()
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
        var service = CreateLineApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-062", message.Code);
    }

    [Fact]
    public void InvVal063_Approve_ReturnsError_WhenVatBreakdownDoesNotMatchLines()
    {
        var invoice = MakeInvoice(CreateLine());
        var line = invoice.LineItems.Single();
        Assert.Equal(23m, line.VatAmount.Amount);
        SetBackingField(invoice, nameof(Invoice.VatBreakdown), new List<VatSummary>
        {
            new(line.VatRate, new Money(100m, Pln), new Money(22.99m, Pln), new Money(122.99m, Pln))
        });
        Assert.Equal(22.99m, invoice.VatBreakdown.Single().VatAmount.Amount);
        var service = CreateSummaryApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-063", message.Code);
    }

    [Fact]
    public void InvVal064_Draft_ReturnsWarning_WhenSplitPaymentFlagHasNoVisibleMarker()
    {
        var invoice = MakeInvoice(CreateLine());
        var service = CreateDraftService();

        var result = service.Validate(
            invoice,
            MakeContext(
                ValidationStage.Draft,
                new Dictionary<string, object?> { ["SplitPaymentRequired"] = true }));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-064", message.Code);
        Assert.Equal(ValidationSeverity.Warning, message.Severity);
    }

    [Fact]
    public void VatRules_ReturnNoMessages_WhenVatDataIsConsistent()
    {
        var invoice = MakeInvoice(CreateLine());
        invoice.SetCommercialData(publicNotes: "mechanizm podzielonej płatności");
        var approvalService = CreateApprovalService();
        var draftService = CreateDraftService();

        var approval = approvalService.Validate(invoice, MakeContext(ValidationStage.Approve));
        var draft = draftService.Validate(
            invoice,
            MakeContext(
                ValidationStage.Draft,
                new Dictionary<string, object?> { ["SplitPaymentRequired"] = true }));

        Assert.Empty(approval.Messages);
        Assert.Empty(draft.Messages);
    }

    private static DraftValidationService CreateDraftService() =>
        new(
            [
                new SplitPaymentRequiresVisibleMarkerRule()
            ],
            []);

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
        new(
            [],
            [
                new VatTreatmentRequiredRule(),
                new ExemptLineRequiresReasonRule(),
                new ExemptLineCannotHavePositiveVatRule()
            ]);

    private static ApprovalValidationService CreateSummaryApprovalService() =>
        new(
            [
                new VatSummaryMustMatchLinesRule()
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
