using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Integration;
using OpenKSeF.Invoices.Domain.Policies;
using OpenKSeF.Invoices.Domain.Presentation;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.Tests.TestSupport;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Tests.ValidationTests;

public class RegressionScenarioTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void Reg001_ReusingConfigurableNumberingPatterns_FollowsFormatAndGroupCounters()
    {
        var policy = new GroupAwareNumberingPolicy();
        var januaryVat = MakeInvoice(DocumentKind.VatInvoice, new DateTime(2026, 01, 15), "A");
        var februaryVat = MakeInvoice(DocumentKind.VatInvoice, new DateTime(2026, 02, 10), "A");
        var januaryCorrection = MakeCorrectionInvoice(new DateTime(2026, 01, 20), "B");

        januaryVat.SetDocumentNumber(policy.AssignNumber(januaryVat));
        februaryVat.SetDocumentNumber(policy.AssignNumber(februaryVat));
        januaryCorrection.SetDocumentNumber(policy.AssignNumber(januaryCorrection));

        Assert.Equal("FV/2026/01/A/0001", januaryVat.DocumentNumber!.Value);
        Assert.Equal("FV/2026/02/A/0001", februaryVat.DocumentNumber!.Value);
        Assert.Equal("KOR/2026/01/B/0001", januaryCorrection.DocumentNumber!.Value);
    }

    [Fact]
    public void Reg002_CorrectionKeepsRelationToOriginalAndRootOriginal()
    {
        var originalInvoiceId = InvoiceId.New();
        var correctionB = CorrectionReference.NormalizeFrom(
            originalInvoiceId,
            new DocumentNumber("FV/2026/0001"),
            CorrectionReasonKind.ValueChange,
            "First correction");

        var correctionC = CorrectionReference.NormalizeFrom(
            InvoiceId.New(),
            new DocumentNumber("KOR/2026/0001"),
            CorrectionReasonKind.ValueChange,
            "Second correction",
            correctionB);

        Assert.Equal(originalInvoiceId, correctionB.OriginalInvoiceId);
        Assert.Equal(originalInvoiceId, correctionB.EffectiveRootOriginalInvoiceId);
        Assert.Equal("KOR/2026/0001", correctionC.OriginalDocumentNumber.Value);
        Assert.Equal(correctionB.OriginalInvoiceId, correctionC.EffectiveRootOriginalInvoiceId);
    }

    [Fact]
    public void Reg003_EnglishPrintDoesNotChangeFiscalPayload()
    {
        var invoice = MakeInvoice(DocumentKind.VatInvoice, new DateTime(2026, 04, 10), "A");
        invoice.SetDocumentNumber(new DocumentNumber("FV/2026/0001"));

        var payloadBefore = ToPayload(invoice);
        var printModel = InvoicePrintModelFactory.CreateEnglish(invoice);
        var payloadAfter = ToPayload(invoice);

        Assert.Equal("en", printModel.Language);
        Assert.Equal("Invoice", printModel.Title);
        Assert.Equal(DocumentKind.VatInvoice, invoice.Kind);
        Assert.Equal(payloadBefore, payloadAfter);
    }

    [Fact]
    public void Reg004_DuplicateIsPresentationOnly_NoNewAggregateOrNumberConsumed()
    {
        var invoice = MakeAcceptedInvoice();
        var numberingPolicy = new SequentialNumberingPolicy();
        var nextBefore = numberingPolicy.AssignNumber(MakeInvoice(DocumentKind.VatInvoice, new DateTime(2026, 04, 10), "A"));

        var duplicate = InvoicePrintModelFactory.CreateDuplicate(invoice, new DateTime(2026, 04, 11));
        invoice.RecordDuplicateIssue(new DateTime(2026, 04, 11), "operator");

        var nextAfter = numberingPolicy.AssignNumber(MakeInvoice(DocumentKind.VatInvoice, new DateTime(2026, 04, 10), "A"));

        Assert.Equal("Duplikat Faktury", duplicate.Title);
        Assert.Equal("FV/1/2026", invoice.DocumentNumber!.Value);
        Assert.Equal("TEST/0001", nextBefore.Value);
        Assert.Equal("TEST/0002", nextAfter.Value);
        Assert.Single(invoice.DuplicateIssuances);
    }

    private static Invoice MakeInvoice(DocumentKind kind, DateTime issueDate, string groupKey)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            kind,
            Seller,
            Buyer,
            Pln,
            issueDate,
            kind == DocumentKind.Proforma ? KsefSubmissionRequirement.Forbidden : KsefSubmissionRequirement.Required,
            externalReference: groupKey);

        invoice.AddLine(InvoiceLine.Create(
            1,
            "Service",
            1m,
            new Money(100m, Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23)),
            correctionRole: kind == DocumentKind.CorrectionInvoice ? CorrectionRole.AfterCorrection : CorrectionRole.Normal));
        invoice.RecalculateTotals();
        return invoice;
    }

    private static Invoice MakeCorrectionInvoice(DateTime issueDate, string groupKey)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            Tenant,
            DocumentKind.CorrectionInvoice,
            Seller,
            Buyer,
            Pln,
            issueDate,
            KsefSubmissionRequirement.Required,
            correctionReference: CorrectionReference.NormalizeFrom(
                InvoiceId.New(),
                new DocumentNumber("FV/2026/0001"),
                CorrectionReasonKind.ValueChange,
                "Correction"),
            externalReference: groupKey);

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

    private static Invoice MakeAcceptedInvoice()
    {
        var invoice = MakeInvoice(DocumentKind.VatInvoice, new DateTime(2026, 04, 10), "A");
        invoice.SetDocumentNumber(new DocumentNumber("FV/1/2026"));
        invoice.Approve(new DateTime(2026, 04, 10, 10, 0, 0, DateTimeKind.Utc));
        invoice.SubmitToKsef(new DateTime(2026, 04, 10, 11, 0, 0, DateTimeKind.Utc));
        invoice.AcceptByKsef(new KsefIdentifiers("KSEF-REG-1", "REF-REG-1"), new DateTime(2026, 04, 10, 12, 0, 0, DateTimeKind.Utc));
        return invoice;
    }

    private static KsefInvoicePayload ToPayload(Invoice invoice) =>
        new(
            $"<Invoice number=\"{invoice.DocumentNumber?.Value}\" kind=\"{invoice.Kind}\" />",
            invoice.DocumentNumber?.Value ?? string.Empty,
            invoice.Seller.Nip?.Value ?? string.Empty);

    private sealed class GroupAwareNumberingPolicy : INumberingPolicy
    {
        private readonly Dictionary<string, int> _counters = new();

        public bool AssignOnApproval => true;

        public DocumentNumber AssignNumber(Invoice invoice)
        {
            var prefix = invoice.Kind == DocumentKind.CorrectionInvoice ? "KOR" : "FV";
            var month = invoice.IssueDate.ToString("MM");
            var groupKey = invoice.ExternalReference ?? "DEFAULT";
            var counterKey = $"{prefix}/{invoice.IssueDate.Year}/{month}/{groupKey}";
            _counters.TryGetValue(counterKey, out var current);
            current++;
            _counters[counterKey] = current;
            return new DocumentNumber($"{counterKey}/{current:0000}");
        }
    }
}
