using System.Globalization;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.ValueObjects;
using OpenKSeF.Invoices.Infrastructure.Mapping;

namespace OpenKSeF.Invoices.Domain.Tests.InfrastructureTests;

public class InvoiceToKsefPayloadMapperTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly SellerSnapshot Seller = new(new PartyName("Seller Sp. z o.o."), new Nip("1234567890"));
    private static readonly BuyerSnapshot Buyer =
        new(new PartyName("Buyer SA"), BuyerKind.Business, new Nip("9876543210"));

    [Fact]
    public void TryMap_ReturnsPayload_ForApprovedInvoiceWithDocumentNumber()
    {
        var invoice = MakeApprovedInvoice("FV/2026/001");

        var mapper = new InvoiceToKsefPayloadMapper();
        var payload = mapper.TryMap(invoice);

        Assert.NotNull(payload);
        Assert.Equal("FV/2026/001", payload.DocumentNumber);
        Assert.Equal("1234567890", payload.SellerNip);
        Assert.False(string.IsNullOrWhiteSpace(payload.InvoiceXml));
    }

    [Fact]
    public void TryMap_XmlContainsSellerNipAndBuyerNip()
    {
        var invoice = MakeApprovedInvoice("FV/2026/002");

        var payload = new InvoiceToKsefPayloadMapper().TryMap(invoice)!;

        Assert.Contains("1234567890", payload.InvoiceXml);
        Assert.Contains("9876543210", payload.InvoiceXml);
    }

    [Fact]
    public void TryMap_XmlContainsCurrencyAndDocumentNumber()
    {
        var invoice = MakeApprovedInvoice("FV/2026/003");

        var payload = new InvoiceToKsefPayloadMapper().TryMap(invoice)!;

        Assert.Contains("PLN", payload.InvoiceXml);
        Assert.Contains("FV/2026/003", payload.InvoiceXml);
    }

    [Fact]
    public void TryMap_ReturnsNull_WhenDocumentNumberMissing()
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(), Tenant, DocumentKind.VatInvoice, Seller, Buyer,
            Pln, new DateTime(2026, 4, 10), KsefSubmissionRequirement.Required);
        invoice.AddLine(InvoiceLine.Create(1, "Item", 1m, new Money(100m, Pln), PricingMode.Net, VatRate.Zero));
        invoice.RecalculateTotals();
        invoice.Approve(new DateTime(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc));

        var payload = new InvoiceToKsefPayloadMapper().TryMap(invoice);

        Assert.Null(payload);
    }

    [Fact]
    public void TryMap_ReturnsNull_ForDraftInvoice()
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(), Tenant, DocumentKind.VatInvoice, Seller, Buyer,
            Pln, new DateTime(2026, 4, 10), KsefSubmissionRequirement.Required,
            documentNumber: new DocumentNumber("FV/2026/004"));
        invoice.AddLine(InvoiceLine.Create(1, "Item", 1m, new Money(100m, Pln), PricingMode.Net, VatRate.Zero));
        invoice.RecalculateTotals();

        var payload = new InvoiceToKsefPayloadMapper().TryMap(invoice);

        Assert.Null(payload);
    }

    [Fact]
    public void TryMap_XmlContainsVatSummaryAndLineData()
    {
        var invoice = MakeApprovedInvoice("FV/2026/005");

        var payload = new InvoiceToKsefPayloadMapper().TryMap(invoice)!;

        // Line element with description
        Assert.Contains("Test line", payload.InvoiceXml);
        // VAT rate 23
        Assert.Contains("23", payload.InvoiceXml);
    }

    [Fact]
    public void TryMap_UsesInvariantCultureForNumericFields()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
        CultureInfo.CurrentUICulture = new CultureInfo("pl-PL");

        try
        {
            var invoice = Invoice.Draft(
                InvoiceId.New(), Tenant, DocumentKind.VatInvoice, Seller, Buyer,
                Pln, new DateTime(2026, 4, 10), KsefSubmissionRequirement.Required,
                documentNumber: new DocumentNumber("FV/2026/006"));

            invoice.AddLine(InvoiceLine.Create(
                1, "Invariant line", 1.5m,
                new Money(100m, Pln),
                PricingMode.Net,
                VatRate.OfPercentage(new Percentage(23))));
            invoice.RecalculateTotals();
            invoice.Approve(new DateTime(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc));

            var payload = new InvoiceToKsefPayloadMapper().TryMap(invoice)!;

            Assert.Contains(">1.5<", payload.InvoiceXml);
            Assert.Contains(">150.00<", payload.InvoiceXml);
            Assert.Contains(">34.50<", payload.InvoiceXml);
            Assert.Contains(">184.50<", payload.InvoiceXml);
            Assert.DoesNotContain(">1,5<", payload.InvoiceXml);
            Assert.DoesNotContain(">150,00<", payload.InvoiceXml);
            Assert.DoesNotContain(">34,50<", payload.InvoiceXml);
            Assert.DoesNotContain(">184,50<", payload.InvoiceXml);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static Invoice MakeApprovedInvoice(string number)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(), Tenant, DocumentKind.VatInvoice, Seller, Buyer,
            Pln, new DateTime(2026, 4, 10), KsefSubmissionRequirement.Required,
            documentNumber: new DocumentNumber(number));

        invoice.AddLine(InvoiceLine.Create(
            1, "Test line", 1m,
            new Money(100m, Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        invoice.Approve(new DateTime(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc));
        return invoice;
    }
}
