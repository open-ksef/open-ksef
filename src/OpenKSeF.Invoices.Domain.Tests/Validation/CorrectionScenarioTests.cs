using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.Tests.TestSupport;
using OpenKSeF.Invoices.Domain.Validation;
using OpenKSeF.Invoices.Domain.Validation.Orchestrators;
using OpenKSeF.Invoices.Domain.Validation.Rules;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Tests.ValidationTests;

public class CorrectionScenarioTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void Cor001_ValidCorrectionInvoice_ApprovalSucceeds()
    {
        var original = MakeOriginalInvoice();
        var correction = MakeCorrectionInvoice(original, CorrectionReasonKind.ValueChange, "Price change");

        var result = CreateApprovalService().Validate(
            correction,
            MakeContext(new Dictionary<string, object?>
            {
                ["OriginalDocumentKind"] = original.Kind,
                ["CorrectionReasonProvided"] = true,
                ["CorrectionHasChanges"] = true
            }));

        Assert.Empty(result.Messages);
    }

    [Fact]
    public void Cor002_CorrectionWithoutOriginalReference_BlocksApprovalWithInvVal080()
    {
        var correction = MakeCorrectionInvoice(reference: null, reasonDescription: "Price change");

        var result = CreateApprovalService().Validate(correction, MakeContext());

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-080");
    }

    [Fact]
    public void Cor003_CorrectionWithoutReason_BlocksApprovalWithInvVal081()
    {
        var original = MakeOriginalInvoice();
        var correction = MakeCorrectionInvoice(original, CorrectionReasonKind.Other, reasonDescription: null);

        var result = CreateApprovalService().Validate(
            correction,
            MakeContext(new Dictionary<string, object?>
            {
                ["OriginalDocumentKind"] = original.Kind,
                ["CorrectionReasonProvided"] = false,
                ["CorrectionHasChanges"] = true
            }));

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-081");
    }

    [Fact]
    public void Cor004_CorrectionWithNoEffectiveChange_BlocksApprovalWithInvVal082()
    {
        var original = MakeOriginalInvoice();
        var correction = MakeCorrectionInvoice(original, CorrectionReasonKind.ValueChange, "No-op");

        var result = CreateApprovalService().Validate(
            correction,
            MakeContext(new Dictionary<string, object?>
            {
                ["OriginalDocumentKind"] = original.Kind,
                ["CorrectionReasonProvided"] = true,
                ["CorrectionHasChanges"] = false
            }));

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-082");
    }

    [Fact]
    public void Cor005_AttemptToCorrectProforma_BlocksApprovalWithInvVal083()
    {
        var proforma = MakeProformaInvoice();
        var correction = MakeCorrectionInvoice(proforma, CorrectionReasonKind.ValueChange, "Try to fiscalize");

        var result = CreateApprovalService().Validate(
            correction,
            MakeContext(new Dictionary<string, object?>
            {
                ["OriginalDocumentKind"] = DocumentKind.Proforma,
                ["CorrectionReasonProvided"] = true,
                ["CorrectionHasChanges"] = true
            }));

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-083");
    }

    [Fact]
    public void Cor006_CorrectionAfterKsefAcceptance_AllowsCorrectionPathAndKeepsOriginalImmutable()
    {
        var acceptedOriginal = MakeAcceptedOriginalInvoice();
        var correction = MakeCorrectionInvoice(acceptedOriginal, CorrectionReasonKind.ValueChange, "Post-KSeF correction");

        var mutateOriginal = () => acceptedOriginal.SetCommercialData(publicNotes: "not allowed");

        var exception = Assert.Throws<InvoiceDomainException>(mutateOriginal);
        Assert.Contains("immutable", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(acceptedOriginal.Id, correction.CorrectionReference!.OriginalInvoiceId);

        correction.AddLine(InvoiceLine.Create(
            2,
            "Correction delta",
            1m,
            new Money(20m, Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23)),
            correctionRole: CorrectionRole.AfterCorrection));
        correction.RecalculateTotals();

        var result = CreateApprovalService().Validate(
            correction,
            MakeContext(new Dictionary<string, object?>
            {
                ["OriginalDocumentKind"] = acceptedOriginal.Kind,
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

    private static ValidationContext MakeContext(IReadOnlyDictionary<string, object?>? items = null) =>
        new(
            ValidationStage.Approve,
            Tenant,
            DateTime.UtcNow,
            TestPolicySnapshot.Default,
            IsKsefSubmissionRequested: false,
            IsNumberAssigned: true,
            Items: items ?? new Dictionary<string, object?>());

    private static Invoice MakeOriginalInvoice()
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            DocumentKind.VatInvoice,
            Seller,
            Buyer,
            Pln,
            new DateTime(2026, 04, 10),
            KsefSubmissionRequirement.Required,
            new DocumentNumber("FV/ORIGINAL/1"));

        invoice.AddLine(InvoiceLine.Create(
            1,
            "Original line",
            1m,
            new Money(100m, Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        invoice.Approve(new DateTime(2026, 04, 10, 10, 0, 0, DateTimeKind.Utc));
        return invoice;
    }

    private static Invoice MakeAcceptedOriginalInvoice()
    {
        var invoice = MakeOriginalInvoice();
        invoice.SubmitToKsef(new DateTime(2026, 04, 10, 11, 0, 0, DateTimeKind.Utc));
        invoice.AcceptByKsef(
            new KsefIdentifiers("KSEF-123", "REF-123"),
            new DateTime(2026, 04, 10, 12, 0, 0, DateTimeKind.Utc));
        return invoice;
    }

    private static Invoice MakeProformaInvoice()
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
            new DocumentNumber("PRO/1"));

        invoice.AddLine(InvoiceLine.Create(
            1,
            "Proforma line",
            1m,
            new Money(100m, Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        return invoice;
    }

    private static Invoice MakeCorrectionInvoice(
        Invoice original,
        CorrectionReasonKind reasonKind,
        string? reasonDescription) =>
        MakeCorrectionInvoice(
            new CorrectionReference(
                original.Id,
                original.DocumentNumber ?? new DocumentNumber("FV/UNKNOWN"),
                reasonKind,
                reasonDescription),
            reasonDescription);

    private static Invoice MakeCorrectionInvoice(
        CorrectionReference? reference = null,
        string? reasonDescription = null)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            DocumentKind.CorrectionInvoice,
            Seller,
            Buyer,
            Pln,
            new DateTime(2026, 04, 11),
            KsefSubmissionRequirement.Required,
            correctionReference: reference);

        invoice.AddLine(InvoiceLine.Create(
            1,
            "Corrected line",
            1m,
            new Money(120m, Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23)),
            correctionRole: string.IsNullOrWhiteSpace(reasonDescription)
                ? CorrectionRole.Normal
                : CorrectionRole.AfterCorrection));
        invoice.RecalculateTotals();
        return invoice;
    }
}
