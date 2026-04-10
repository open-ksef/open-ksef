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

public class StructureIdentityValidationRuleTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void InvVal001_Approve_ReturnsError_WhenDocumentKindIsUnsupported()
    {
        var invoice = MakeInvoice((DocumentKind)999, addLine: true);
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-001", message.Code);
        Assert.Equal(ValidationSeverity.Error, message.Severity);
    }

    [Fact]
    public void InvVal001_Approve_ReturnsNoMessage_WhenDocumentKindIsSupported()
    {
        var invoice = MakeInvoice(DocumentKind.VatInvoice, addLine: true);
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        Assert.DoesNotContain(result.Messages, m => m.Code == "INV-VAL-001");
    }

    [Fact]
    public void InvVal002_Approve_ReturnsError_WhenFiscalDocumentHasNoLines()
    {
        var invoice = MakeInvoice(DocumentKind.VatInvoice, addLine: false);
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-002", message.Code);
        Assert.Equal("LineItems", message.Path);
    }

    [Fact]
    public void InvVal002_Approve_ReturnsNoMessage_WhenFiscalDocumentHasLineItems()
    {
        var invoice = MakeInvoice(DocumentKind.VatInvoice, addLine: true);
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        Assert.DoesNotContain(result.Messages, m => m.Code == "INV-VAL-002");
    }

    [Fact]
    public void InvVal003_SendToKsef_ReturnsError_WhenProformaEntersKsefPath()
    {
        var invoice = MakeInvoice(DocumentKind.Proforma, addLine: true);
        var service = CreateKsefService();

        var result = service.Validate(invoice, payload: null, MakeContext(ValidationStage.SendToKsef, isKsefSubmissionRequested: true));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-003", message.Code);
        Assert.Equal(ValidationStage.SendToKsef, message.Stage);
    }

    [Fact]
    public void InvVal003_Approve_ReturnsNoMessage_WhenProformaStaysCommercial()
    {
        var invoice = MakeInvoice(DocumentKind.Proforma, addLine: true);
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve, isKsefSubmissionRequested: false));

        Assert.DoesNotContain(result.Messages, m => m.Code == "INV-VAL-003");
    }

    private static ApprovalValidationService CreateApprovalService() =>
        new(
            [
                new SupportedDocumentKindRule(),
                new FiscalDocumentRequiresLineItemsRule(),
                new ProformaCannotEnterFiscalPathRule()
            ],
            []);

    private static KsefSubmissionValidationService CreateKsefService() =>
        new(
            [
                new SupportedDocumentKindRule(),
                new FiscalDocumentRequiresLineItemsRule(),
                new ProformaCannotEnterFiscalPathRule()
            ],
            [],
            []);

    private static ValidationContext MakeContext(ValidationStage stage, bool isKsefSubmissionRequested = false) =>
        new(
            stage,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: isKsefSubmissionRequested,
            IsNumberAssigned: false,
            Items: new Dictionary<string, object?>());

    private static Invoice MakeInvoice(DocumentKind kind, bool addLine)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            kind,
            Seller,
            Buyer,
            Pln,
            DateTime.UtcNow,
            kind == DocumentKind.Proforma
                ? KsefSubmissionRequirement.Forbidden
                : KsefSubmissionRequirement.Required);

        if (!addLine)
        {
            return invoice;
        }

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
