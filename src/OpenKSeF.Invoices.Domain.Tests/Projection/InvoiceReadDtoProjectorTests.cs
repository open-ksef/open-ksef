using OpenKSeF.Invoices.Application.Projection;
using OpenKSeF.Invoices.Contracts.Dtos;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Tests.ProjectionTests;

public class InvoiceReadDtoProjectorTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller Sp. z o.o."), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer SA"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void Project_MapsIdentityFields()
    {
        var invoice = MakeDraftInvoice("FV/2026/001");
        var projector = new InvoiceReadDtoProjector();

        var dto = projector.Project(invoice);

        Assert.Equal(invoice.Id.Value, dto.Id);
        Assert.Equal(invoice.TenantId.Value, dto.TenantId);
        Assert.Equal("FV/2026/001", dto.DocumentNumber);
        Assert.Equal("VatInvoice", dto.Kind);
        Assert.Equal("Draft", dto.Status);
    }

    [Fact]
    public void Project_MapsSellerAndBuyer()
    {
        var invoice = MakeDraftInvoice("FV/2026/002");
        var dto = new InvoiceReadDtoProjector().Project(invoice);

        Assert.Equal("Seller Sp. z o.o.", dto.Seller.Name);
        Assert.Equal("1234567890", dto.Seller.Nip);
        Assert.Equal("Buyer SA", dto.Buyer.Name);
        Assert.Equal("9876543210", dto.Buyer.Nip);
    }

    [Fact]
    public void Project_MapsTotals()
    {
        var invoice = MakeDraftInvoice("FV/2026/003");
        var dto = new InvoiceReadDtoProjector().Project(invoice);

        Assert.Equal(100m, dto.TotalNet.Amount);
        Assert.True(dto.TotalVat.Amount > 0);
        Assert.Equal("PLN", dto.TotalNet.Currency);
    }

    [Fact]
    public void Project_MapsLines()
    {
        var invoice = MakeDraftInvoice("FV/2026/004");
        var dto = new InvoiceReadDtoProjector().Project(invoice);

        var line = Assert.Single(dto.Lines);
        Assert.Equal("Test service", line.Description);
        Assert.Equal(1m, line.Quantity);
        Assert.Equal("23%", line.VatRate);
    }

    [Fact]
    public void Project_MapsCorrectionReference_WhenPresent()
    {
        var originalId = InvoiceId.New();
        var ref_ = new CorrectionReference(
            originalId,
            new DocumentNumber("FV/ORIG/1"),
            CorrectionReasonKind.ValueChange,
            "Price change");

        var correction = Invoice.Draft(
            InvoiceId.New(), Tenant, DocumentKind.CorrectionInvoice, Seller, Buyer,
            Pln, new DateTime(2026, 4, 10), KsefSubmissionRequirement.Required,
            correctionReference: ref_);
        correction.AddLine(InvoiceLine.Create(
            1, "Line", 1m, new Money(100m, Pln), PricingMode.Net, VatRate.Zero));
        correction.RecalculateTotals();

        var dto = new InvoiceReadDtoProjector().Project(correction);

        Assert.NotNull(dto.CorrectionReference);
        Assert.Equal(originalId.Value, dto.CorrectionReference.OriginalInvoiceId);
        Assert.Equal("FV/ORIG/1", dto.CorrectionReference.OriginalDocumentNumber);
        Assert.Equal("ValueChange", dto.CorrectionReference.ReasonKind);
        Assert.Equal("Price change", dto.CorrectionReference.ReasonDescription);
    }

    [Fact]
    public void Project_MapsAdvanceAllocations()
    {
        var invoice = MakeDraftInvoice("FIN/2026/001");
        var advId = InvoiceId.New();
        invoice.AddAdvanceDocumentId(advId);
        invoice.AddAdvanceAllocation(
            new AdvanceAllocation(advId, new DocumentNumber("ADV/1"), new Money(300m, Pln)));

        var dto = new InvoiceReadDtoProjector().Project(invoice);

        var alloc = Assert.Single(dto.SettledAdvanceAllocations);
        Assert.Equal(advId.Value, alloc.AdvanceInvoiceId);
        Assert.Equal("ADV/1", alloc.AdvanceDocumentNumber);
        Assert.Equal(300m, alloc.SettledAmount.Amount);
    }

    [Fact]
    public void Project_MapsDuplicateIssuances()
    {
        var invoice = MakeDraftInvoice("FV/2026/005");
        invoice.Approve(new DateTime(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc));
        invoice.SubmitToKsef(new DateTime(2026, 4, 10, 11, 0, 0, DateTimeKind.Utc));
        invoice.AcceptByKsef(
            new KsefIdentifiers("KSEF-1", "REF-1"),
            new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc));
        invoice.RecordDuplicateIssue(new DateTime(2026, 4, 10, 13, 0, 0, DateTimeKind.Utc), "admin");

        var dto = new InvoiceReadDtoProjector().Project(invoice);

        var dup = Assert.Single(dto.DuplicateIssuances);
        Assert.Equal("admin", dup.IssuedBy);
    }

    private static Invoice MakeDraftInvoice(string number)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(), Tenant, DocumentKind.VatInvoice, Seller, Buyer,
            Pln, new DateTime(2026, 4, 10), KsefSubmissionRequirement.Required,
            documentNumber: new DocumentNumber(number));

        invoice.AddLine(InvoiceLine.Create(
            1, "Test service", 1m,
            new Money(100m, Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        return invoice;
    }
}
