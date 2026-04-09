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

public class LineTotalsValidationRuleTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void InvVal050_Approve_ReturnsError_WhenLineDescriptionIsMissing()
    {
        var invoice = MakeInvoice(CreateLine(description: ""));
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-050", message.Code);
    }

    [Fact]
    public void InvVal051_Approve_ReturnsError_WhenNormalLineQuantityIsNotPositive()
    {
        var invoice = MakeInvoice(CreateLine(quantity: 0m));
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-051", message.Code);
    }

    [Fact]
    public void InvVal052_Approve_ReturnsError_WhenLineAmountsAreInconsistent()
    {
        var line = CreateLine();
        SetProperty(line, nameof(InvoiceLine.GrossAmount), new Money(130m, Pln));
        var invoice = MakeInvoice(line);
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-052", message.Code);
    }

    [Fact]
    public void InvVal053_Approve_ReturnsError_WhenDocumentTotalsDoNotMatchLineSum()
    {
        var invoice = MakeInvoice(CreateLine());
        SetProperty(invoice, nameof(Invoice.Totals), new DocumentTotals(new Money(1m, Pln), new Money(1m, Pln), new Money(1m, Pln)));
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-053", message.Code);
    }

    [Fact]
    public void LineAndTotalsRules_Approve_ReturnNoMessages_WhenValuesAreConsistent()
    {
        var invoice = MakeInvoice(CreateLine());
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        Assert.Empty(result.Messages);
    }

    private static ApprovalValidationService CreateApprovalService() =>
        new(
            [
                new DocumentTotalsMustMatchLineSumRule()
            ],
            [
                new LineDescriptionRequiredRule(),
                new LineQuantityMustBePositiveRule(),
                new LineAmountsMustBeInternallyConsistentRule()
            ]);

    private static ValidationContext MakeContext(ValidationStage stage) =>
        new(
            stage,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: false,
            IsNumberAssigned: false,
            Items: new Dictionary<string, object?>());

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

    private static InvoiceLine CreateLine(
        string description = "Service",
        decimal quantity = 1m,
        CorrectionRole correctionRole = CorrectionRole.Normal) =>
        InvoiceLine.Create(
            1,
            description,
            quantity,
            new Money(100m, Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23)),
            correctionRole: correctionRole);

    private static void SetProperty(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        property!.SetValue(target, value);
    }
}
