using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.Tests.TestSupport;
using OpenKSeF.Invoices.Domain.Validation;
using OpenKSeF.Invoices.Domain.Validation.Orchestrators;
using OpenKSeF.Invoices.Domain.Validation.Rules;
using OpenKSeF.Invoices.Domain.ValueObjects;
using InvoiceLine = OpenKSeF.Invoices.Domain.Entities.InvoiceLine;

namespace OpenKSeF.Invoices.Domain.Tests.ValidationTests;

public class PartyValidationRuleTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));

    [Fact]
    public void InvVal010_Approve_ReturnsError_WhenSellerNameIsMissing()
    {
        var invoice = MakeInvoice(new SellerSnapshot(null!, new Nip("1234567890")), BusinessBuyer());
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-010", message.Code);
        Assert.Equal("Seller.Name", message.Path);
    }

    [Fact]
    public void InvVal010_Approve_ReturnsNoMessage_WhenSellerNameIsPresent()
    {
        var invoice = MakeInvoice(Seller, BusinessBuyer());
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        Assert.DoesNotContain(result.Messages, m => m.Code == "INV-VAL-010");
    }

    [Fact]
    public void InvVal011_Approve_ReturnsError_WhenSellerNipIsMissingForPolishFiscalDocument()
    {
        var invoice = MakeInvoice(new SellerSnapshot(new PartyName("Seller"), null!), BusinessBuyer());
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-011", message.Code);
        Assert.Equal("Seller.Nip", message.Path);
    }

    [Fact]
    public void InvVal011_Approve_ReturnsNoMessage_WhenSellerNipIsPresentForPolishFiscalDocument()
    {
        var invoice = MakeInvoice(Seller, BusinessBuyer());
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        Assert.DoesNotContain(result.Messages, m => m.Code == "INV-VAL-011");
    }

    [Fact]
    public void InvVal012_Draft_ReturnsWarning_WhenBuyerKindIsUnknown()
    {
        var invoice = MakeInvoice(Seller, new BuyerSnapshot(new PartyName("Buyer"), BuyerKind.Unknown));
        var service = CreateDraftService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Draft));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-012", message.Code);
        Assert.Equal(ValidationSeverity.Warning, message.Severity);
    }

    [Fact]
    public void InvVal012_Draft_ReturnsNoMessage_WhenBuyerKindIsResolved()
    {
        var invoice = MakeInvoice(Seller, BusinessBuyer());
        var service = CreateDraftService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Draft));

        Assert.DoesNotContain(result.Messages, m => m.Code == "INV-VAL-012");
    }

    [Fact]
    public void InvVal013_Approve_ReturnsError_WhenBusinessBuyerHasNoNip()
    {
        var invoice = MakeInvoice(Seller, new BuyerSnapshot(new PartyName("Buyer"), BuyerKind.Business));
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-013", message.Code);
        Assert.Equal("Buyer.Nip", message.Path);
    }

    [Fact]
    public void InvVal013_Approve_ReturnsNoMessage_WhenBusinessBuyerHasNip()
    {
        var invoice = MakeInvoice(Seller, BusinessBuyer());
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        Assert.DoesNotContain(result.Messages, m => m.Code == "INV-VAL-013");
    }

    private static DraftValidationService CreateDraftService() =>
        new(
            [
                new BuyerKindMustBeResolvedRule()
            ],
            []);

    private static ApprovalValidationService CreateApprovalService() =>
        new(
            [
                new SellerLegalNameRequiredRule(),
                new SellerNipRequiredForPolishFiscalDocumentRule(),
                new BusinessBuyerRequiresNipRule()
            ],
            []);

    private static ValidationContext MakeContext(ValidationStage stage) =>
        new(
            stage,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: false,
            IsNumberAssigned: false,
            Items: new Dictionary<string, object?>());

    private static BuyerSnapshot BusinessBuyer() =>
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    private static Invoice MakeInvoice(SellerSnapshot seller, BuyerSnapshot buyer)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            DocumentKind.VatInvoice,
            seller,
            buyer,
            Pln,
            DateTime.UtcNow,
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
