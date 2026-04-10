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

public class ProformaScenarioTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void Pro001_ValidProformaApprovalForCommercialWorkflow_SucceedsAndRemainsNonFiscal()
    {
        var invoice = MakeProforma();

        invoice.Approve(new DateTime(2026, 04, 10, 10, 0, 0, DateTimeKind.Utc));

        Assert.Equal(DocumentStatus.Approved, invoice.Status);
        Assert.Equal(KsefSubmissionRequirement.Forbidden, invoice.KsefSubmissionRequirement);
        Assert.Equal(KsefSubmissionState.NotPlanned, invoice.KsefSubmissionState);
    }

    [Fact]
    public void Pro002_ApprovedProformaSendToKsef_IsBlockedWithFiscalPathValidationCode()
    {
        var invoice = MakeProforma();
        invoice.Approve(new DateTime(2026, 04, 10, 10, 0, 0, DateTimeKind.Utc));

        var result = CreateSubmissionService().Validate(
            invoice,
            payload: null,
            MakeSendContext(new Dictionary<string, object?> { ["KsefConfigAvailable"] = true }));

        Assert.Contains(result.Messages, m => m.Code is "INV-VAL-003" or "INV-VAL-091");
    }

    [Fact]
    public void Pro003_SeparateNumberingPattern_AssignsProformaSequence()
    {
        var policy = new KindAwareNumberingPolicy();
        var proforma = MakeProforma(documentNumber: null);
        var vatInvoice = MakeVatInvoice();

        proforma.SetDocumentNumber(policy.AssignNumber(proforma));
        vatInvoice.SetDocumentNumber(policy.AssignNumber(vatInvoice));

        Assert.Equal("PRO/2026/0001", proforma.DocumentNumber!.Value);
        Assert.Equal("FV/2026/0001", vatInvoice.DocumentNumber!.Value);
    }

    private static KsefSubmissionValidationService CreateSubmissionService() =>
        new(
            [
                new ProformaCannotEnterFiscalPathRule(),
                new DocumentKindMustBeKsefEligibleRule()
            ],
            [],
            []);

    private static ValidationContext MakeSendContext(IReadOnlyDictionary<string, object?> items) =>
        new(
            ValidationStage.SendToKsef,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: true,
            IsNumberAssigned: true,
            Items: items);

    private static Invoice MakeProforma(DocumentNumber? documentNumber = null)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            DocumentKind.Proforma,
            Seller,
            Buyer,
            Pln,
            new DateTime(2026, 04, 10),
            KsefSubmissionRequirement.Forbidden,
            documentNumber);

        invoice.AddLine(InvoiceLine.Create(
            1,
            "Commercial quote",
            1m,
            new Money(100m, Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        return invoice;
    }

    private static Invoice MakeVatInvoice()
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

        invoice.AddLine(InvoiceLine.Create(
            1,
            "Fiscal sale",
            1m,
            new Money(100m, Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        return invoice;
    }

    private sealed class KindAwareNumberingPolicy : INumberingPolicy
    {
        public bool AssignOnApproval => true;

        public DocumentNumber AssignNumber(Invoice invoice) =>
            invoice.Kind == DocumentKind.Proforma
                ? new DocumentNumber("PRO/2026/0001")
                : new DocumentNumber("FV/2026/0001");
    }
}
