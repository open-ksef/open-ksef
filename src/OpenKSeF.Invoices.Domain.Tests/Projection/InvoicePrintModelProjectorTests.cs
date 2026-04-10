using OpenKSeF.Invoices.Application.Projection;
using OpenKSeF.Invoices.Contracts.Dtos;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.ValueObjects;
using OpenKSeF.Invoices.Domain.Tests.TestSupport;

namespace OpenKSeF.Invoices.Domain.Tests.ProjectionTests;

public class InvoicePrintModelProjectorTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller"), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210"));
    private static readonly AlwaysAllowReopenPolicy ReopenPolicy = new();

    // ── REG-003: Standard print ────────────────────────────────────────────

    [Fact]
    public void Reg003_StandardPrint_HasPolishLabelsAndNoduplicateInfo()
    {
        var invoice = MakeApprovedInvoice("FV/2026/REG003");
        var projector = new InvoicePrintModelProjector(PrintVariant.Standard, ReopenPolicy);

        var print = projector.Project(invoice);

        Assert.Equal(PrintVariant.Standard, print.Variant);
        Assert.Equal("FAKTURA VAT", print.Labels.InvoiceTitle);
        Assert.Null(print.DuplicateInfo);
    }

    // ── REG-004: English print ─────────────────────────────────────────────

    [Fact]
    public void Reg004_EnglishPrint_HasEnglishLabels_SameData()
    {
        var invoice = MakeApprovedInvoice("FV/2026/REG004");
        var projector = new InvoicePrintModelProjector(PrintVariant.English, ReopenPolicy);

        var print = projector.Project(invoice);

        Assert.Equal(PrintVariant.English, print.Variant);
        Assert.Equal("VAT INVOICE", print.Labels.InvoiceTitle);
        Assert.Equal("FV/2026/REG004", print.InvoiceData.DocumentNumber);
        Assert.Null(print.DuplicateInfo);
    }

    // ── IMM-003: Duplicate print ──────────────────────────────────────────

    [Fact]
    public void Imm003_DuplicatePrint_RecordsMetadataWithoutChangingContent()
    {
        var invoice = MakeAcceptedInvoice("FV/2026/IMM003");
        invoice.RecordDuplicateIssue(new DateTime(2026, 4, 10, 13, 0, 0, DateTimeKind.Utc), "admin");
        var projector = new InvoicePrintModelProjector(PrintVariant.Duplicate, ReopenPolicy);

        var print = projector.Project(invoice);

        Assert.Equal(PrintVariant.Duplicate, print.Variant);
        Assert.NotNull(print.DuplicateInfo);
        Assert.Equal("admin", print.DuplicateInfo.IssuedBy);
        Assert.Equal("FV/2026/IMM003", print.DuplicateInfo.OriginalDocumentNumber);
        // Fiscal data unchanged
        Assert.Equal("FV/2026/IMM003", print.InvoiceData.DocumentNumber);
        Assert.Equal("DUPLIKAT", print.Labels.DuplicateLabel);
    }

    [Fact]
    public void DuplicatePrint_WhenNoDuplicateIssuanceExists_StillProducesPrint()
    {
        // Invoice accepted but RecordDuplicateIssue not yet called — print is still valid
        var invoice = MakeAcceptedInvoice("FV/2026/DUP002");
        var projector = new InvoicePrintModelProjector(PrintVariant.Duplicate, ReopenPolicy);

        var print = projector.Project(invoice);

        Assert.Equal(PrintVariant.Duplicate, print.Variant);
        Assert.NotNull(print.DuplicateInfo);
    }

    private static Invoice MakeDraftInvoice(string number)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(), Tenant, DocumentKind.VatInvoice, Seller, Buyer,
            Pln, new DateTime(2026, 4, 10), KsefSubmissionRequirement.Required,
            documentNumber: new DocumentNumber(number));
        invoice.AddLine(InvoiceLine.Create(
            1, "Service", 1m, new Money(100m, Pln), PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        return invoice;
    }

    private static Invoice MakeApprovedInvoice(string number)
    {
        var invoice = MakeDraftInvoice(number);
        invoice.Approve(new DateTime(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc));
        return invoice;
    }

    private static Invoice MakeAcceptedInvoice(string number)
    {
        var invoice = MakeApprovedInvoice(number);
        invoice.SubmitToKsef(new DateTime(2026, 4, 10, 11, 0, 0, DateTimeKind.Utc));
        invoice.AcceptByKsef(
            new KsefIdentifiers("KSEF-TEST", "REF-TEST"),
            new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc));
        return invoice;
    }
}
