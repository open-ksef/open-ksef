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

public class CorrectionValidationRuleTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void InvVal080_Approve_ReturnsError_WhenCorrectionHasNoReference()
    {
        var invoice = MakeCorrectionInvoice(correctionReference: null);
        var service = CreateApprovalService();

        var result = service.Validate(invoice, MakeContext(ValidationStage.Approve));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-080", message.Code);
    }

    [Fact]
    public void InvVal081_Approve_ReturnsError_WhenCorrectionReasonMissing()
    {
        var invoice = MakeCorrectionInvoice(new CorrectionReference(
            InvoiceId.New(),
            new DocumentNumber("FV/1"),
            CorrectionReasonKind.Other));
        var service = CreateApprovalService();

        var result = service.Validate(
            invoice,
            MakeContext(
                ValidationStage.Approve,
                new Dictionary<string, object?> { ["CorrectionReasonProvided"] = false }));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-081", message.Code);
    }

    [Fact]
    public void InvVal082_Approve_ReturnsError_WhenCorrectionHasNoEffectiveChange()
    {
        var invoice = MakeCorrectionInvoice(DefaultReference());
        var service = CreateApprovalService();

        var result = service.Validate(
            invoice,
            MakeContext(
                ValidationStage.Approve,
                new Dictionary<string, object?> { ["CorrectionHasChanges"] = false, ["CorrectionReasonProvided"] = true }));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-082", message.Code);
    }

    [Fact]
    public void InvVal083_Approve_ReturnsError_WhenCorrectionReferencesProforma()
    {
        var invoice = MakeCorrectionInvoice(DefaultReference());
        var service = CreateApprovalService();

        var result = service.Validate(
            invoice,
            MakeContext(
                ValidationStage.Approve,
                new Dictionary<string, object?>
                {
                    ["OriginalDocumentKind"] = DocumentKind.Proforma,
                    ["CorrectionReasonProvided"] = true,
                    ["CorrectionHasChanges"] = true
                }));

        var message = Assert.Single(result.Messages);
        Assert.Equal("INV-VAL-083", message.Code);
    }

    [Fact]
    public void CorrectionRules_ReturnNoMessages_WhenCorrectionIsValid()
    {
        var invoice = MakeCorrectionInvoice(DefaultReference());
        var service = CreateApprovalService();

        var result = service.Validate(
            invoice,
            MakeContext(
                ValidationStage.Approve,
                new Dictionary<string, object?>
                {
                    ["OriginalDocumentKind"] = DocumentKind.VatInvoice,
                    ["CorrectionReasonProvided"] = true,
                    ["CorrectionHasChanges"] = true
                }));

        Assert.Empty(result.Messages);
    }

    private static ApprovalValidationService CreateApprovalService() =>
        new(
            [
                new CorrectionMustReferenceOriginalDocumentRule(),
                new CorrectionReasonRequiredRule(),
                new CorrectionMustContainEffectiveChangeRule(),
                new ProformaCannotBeCorrectedFiscalyRule()
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

    private static Invoice MakeCorrectionInvoice(CorrectionReference? correctionReference)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            DocumentKind.CorrectionInvoice,
            Seller,
            Buyer,
            Pln,
            new DateTime(2026, 04, 10),
            KsefSubmissionRequirement.Required,
            correctionReference: correctionReference);

        invoice.AddLine(InvoiceLine.Create(
            1,
            "Correction line",
            1m,
            new Money(100m, Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23)),
            correctionRole: CorrectionRole.AfterCorrection));
        invoice.RecalculateTotals();
        return invoice;
    }

    private static CorrectionReference DefaultReference() =>
        new(InvoiceId.New(), new DocumentNumber("FV/1"), CorrectionReasonKind.ValueChange, "Price change");
}
