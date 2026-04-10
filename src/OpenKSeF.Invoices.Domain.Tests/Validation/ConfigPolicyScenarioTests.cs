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

public class ConfigPolicyScenarioTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly CurrencyCode Eur = new("EUR");
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot BusinessBuyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void Cfg001_CustomNumberingPerDocumentType_UsesConfiguredSequences()
    {
        var policy = new KindAwareNumberingPolicy();
        var vat = MakeInvoice(DocumentKind.VatInvoice, BusinessBuyer, Pln);
        var proforma = MakeInvoice(DocumentKind.Proforma, BusinessBuyer, Pln, KsefSubmissionRequirement.Forbidden);
        var correction = MakeCorrectionInvoice();

        vat.SetDocumentNumber(policy.AssignNumber(vat));
        proforma.SetDocumentNumber(policy.AssignNumber(proforma));
        correction.SetDocumentNumber(policy.AssignNumber(correction));

        Assert.Equal("FV/2026/0001", vat.DocumentNumber!.Value);
        Assert.Equal("PRO/2026/0001", proforma.DocumentNumber!.Value);
        Assert.Equal("KOR/2026/0001", correction.DocumentNumber!.Value);
    }

    [Fact]
    public void Cfg002_UniquenessScopePerYear_AllowsSameSerialAcrossYears()
    {
        var invoice = MakeInvoice(DocumentKind.VatInvoice, BusinessBuyer, Pln, documentNumber: new DocumentNumber("FV/2026/0001"));

        var result = CreateApprovalService().Validate(
            invoice,
            MakeApprovalContext(new Dictionary<string, object?>
            {
                ["DocumentUniquenessPolicy"] = new YearScopedUniquenessPolicy("FV/2025/0001")
            }));

        Assert.DoesNotContain(result.Messages, m => m.Code == "INV-VAL-031");
    }

    [Fact]
    public void Cfg003_PlnOnlyTenant_BlocksEurWithInvVal041()
    {
        var invoice = MakeInvoice(DocumentKind.VatInvoice, BusinessBuyer, Eur);

        var result = CreateApprovalService().Validate(
            invoice,
            MakeApprovalContext(currency: new CurrencyPolicy("PLN")));

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-041");
    }

    [Fact]
    public void Cfg004_ForeignCurrencyDraftModeWithoutExchangeRate_ReturnsWarningInvVal042()
    {
        var invoice = MakeInvoice(DocumentKind.VatInvoice, BusinessBuyer, Eur);

        var result = CreateDraftService().Validate(
            invoice,
            MakeDraftContext(currency: new CurrencyPolicy("PLN")));

        Assert.Contains(result.Messages, m => m.Code == "INV-VAL-042" && m.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void Cfg005_DraftWarningBecomesApprovalBlock_WhenKsefResolutionIsRequired()
    {
        var invoice = MakeInvoice(DocumentKind.VatInvoice, new BuyerSnapshot(new PartyName("Buyer"), BuyerKind.Unknown), Pln);

        var draftResult = CreateDraftBuyerService().Validate(invoice, MakeDraftContext());
        var approvalResult = CreateApprovalBuyerService().Validate(
            invoice,
            MakeApprovalContext(new Dictionary<string, object?>
            {
                ["KsefObligationDependsOnBuyerClassification"] = true
            }));

        Assert.Contains(draftResult.Messages, m => m.Code == "INV-VAL-012" && m.Severity == ValidationSeverity.Warning);
        Assert.Contains(approvalResult.Messages, m => m.Code == "INV-VAL-090" && m.Severity == ValidationSeverity.Error);
    }

    private static ApprovalValidationService CreateApprovalService() =>
        new(
            [
                new DocumentNumberMustBeUniqueRule(),
                new ForeignCurrencyBlockedByPolicyRule()
            ],
            []);

    private static DraftValidationService CreateDraftService() =>
        new([new ForeignCurrencyRequiresExchangeRateMetadataRule()], []);

    private static DraftValidationService CreateDraftBuyerService() =>
        new([new BuyerKindMustBeResolvedRule()], []);

    private static ApprovalValidationService CreateApprovalBuyerService() =>
        new([new BuyerClassificationMustResolveForKsefRule()], []);

    private static ValidationContext MakeDraftContext(
        CurrencyPolicy? currency = null,
        IReadOnlyDictionary<string, object?>? items = null) =>
        new(
            ValidationStage.Draft,
            Tenant,
            DateTime.UtcNow,
            new PolicySnapshotForTests(currency ?? new CurrencyPolicy()),
            IsKsefSubmissionRequested: false,
            IsNumberAssigned: false,
            Items: items ?? new Dictionary<string, object?>());

    private static ValidationContext MakeApprovalContext(
        IReadOnlyDictionary<string, object?>? items = null,
        CurrencyPolicy? currency = null) =>
        new(
            ValidationStage.Approve,
            Tenant,
            DateTime.UtcNow,
            new PolicySnapshotForTests(currency ?? new CurrencyPolicy()),
            IsKsefSubmissionRequested: false,
            IsNumberAssigned: true,
            Items: items ?? new Dictionary<string, object?>());

    private static Invoice MakeInvoice(
        DocumentKind kind,
        BuyerSnapshot buyer,
        CurrencyCode currency,
        KsefSubmissionRequirement requirement = KsefSubmissionRequirement.Required,
        DocumentNumber? documentNumber = null)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            kind,
            Seller,
            buyer,
            currency,
            new DateTime(2026, 04, 10),
            requirement,
            documentNumber: documentNumber);

        invoice.AddLine(InvoiceLine.Create(
            1,
            "Service",
            1m,
            new Money(100m, currency),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        return invoice;
    }

    private static Invoice MakeCorrectionInvoice()
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            DocumentKind.CorrectionInvoice,
            Seller,
            BusinessBuyer,
            Pln,
            new DateTime(2026, 04, 10),
            KsefSubmissionRequirement.Required,
            correctionReference: new CorrectionReference(
                InvoiceId.New(),
                new DocumentNumber("FV/2026/0099"),
                CorrectionReasonKind.ValueChange,
                "Correction"));

        invoice.AddLine(InvoiceLine.Create(
            1,
            "Correction",
            1m,
            new Money(100m, Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23)),
            correctionRole: CorrectionRole.AfterCorrection));
        invoice.RecalculateTotals();
        return invoice;
    }

    private sealed class PolicySnapshotForTests(CurrencyPolicy currency) : IPolicySnapshot
    {
        public NumberingPolicy Numbering { get; } = new();
        public KsefPolicy Ksef { get; } = new();
        public VatPolicy Vat { get; } = new();
        public EditPolicy Edit { get; } = new();
        public ValidationPolicy Validation { get; } = new();
        public CurrencyPolicy Currency { get; } = currency;
    }

    private sealed class KindAwareNumberingPolicy : INumberingPolicy
    {
        public bool AssignOnApproval => true;

        public DocumentNumber AssignNumber(Invoice invoice) =>
            invoice.Kind switch
            {
                DocumentKind.Proforma => new DocumentNumber("PRO/2026/0001"),
                DocumentKind.CorrectionInvoice => new DocumentNumber("KOR/2026/0001"),
                _ => new DocumentNumber("FV/2026/0001")
            };
    }

    private sealed class YearScopedUniquenessPolicy(string usedNumber) : IDocumentUniquenessPolicy
    {
        public bool IsDuplicate(TenantId tenantId, DocumentNumber number) =>
            ExtractSerial(number.Value) == ExtractSerial(usedNumber) &&
            ExtractYear(number.Value) == ExtractYear(usedNumber);

        private static string ExtractYear(string number) => number.Split('/')[1];
        private static string ExtractSerial(string number) => number.Split('/')[2];
    }
}
