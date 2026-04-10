using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.ValueObjects;
using InvoiceLine = OpenKSeF.Invoices.Domain.Entities.InvoiceLine;

namespace OpenKSeF.Invoices.Domain.Tests.InvoiceTests;

public class InvoiceTotalsTests
{
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly VatRate Vat23 = VatRate.OfPercentage(new Percentage(23));
    private static readonly VatRate Vat8 = VatRate.OfPercentage(new Percentage(8));
    private static readonly VatRate VatZero = VatRate.Zero;

    private static Domain.Aggregates.Invoice EmptyVatInvoice() =>
        Domain.Aggregates.Invoice.Draft(
            InvoiceId.New(), Tenant, DocumentKind.VatInvoice,
            new SellerSnapshot(new PartyName("Seller"), new Nip("1234567890")),
            new BuyerSnapshot(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210")),
            Pln, DateTime.UtcNow, KsefSubmissionRequirement.Required);

    [Fact]
    public void RecalculateTotals_SingleLine_SumsTotals()
    {
        var inv = EmptyVatInvoice();
        inv.AddLine(InvoiceLine.Create(1, "Service", 1m, new Money(100m, Pln), PricingMode.Net, Vat23));
        inv.RecalculateTotals();

        Assert.Equal(100m, inv.Totals.NetTotal.Amount);
        Assert.Equal(23m, inv.Totals.VatTotal.Amount);
        Assert.Equal(123m, inv.Totals.GrossTotal.Amount);
    }

    [Fact]
    public void RecalculateTotals_MultipleLines_SumsAll()
    {
        var inv = EmptyVatInvoice();
        inv.AddLine(InvoiceLine.Create(1, "A", 1m, new Money(100m, Pln), PricingMode.Net, Vat23));
        inv.AddLine(InvoiceLine.Create(2, "B", 2m, new Money(50m, Pln), PricingMode.Net, Vat8));
        inv.RecalculateTotals();

        // Line 1: net=100, vat=23, gross=123
        // Line 2: net=100, vat=8, gross=108
        Assert.Equal(200m, inv.Totals.NetTotal.Amount);
        Assert.Equal(31m, inv.Totals.VatTotal.Amount);
        Assert.Equal(231m, inv.Totals.GrossTotal.Amount);
    }

    [Fact]
    public void RecalculateTotals_TotalsAreConsistent()
    {
        var inv = EmptyVatInvoice();
        inv.AddLine(InvoiceLine.Create(1, "A", 1m, new Money(100m, Pln), PricingMode.Net, Vat23));
        inv.AddLine(InvoiceLine.Create(2, "B", 1m, new Money(50m, Pln), PricingMode.Net, Vat8));
        inv.RecalculateTotals();

        Assert.True(inv.Totals.IsConsistent());
    }

    [Fact]
    public void RecalculateTotals_VatBreakdown_GroupedByRate()
    {
        var inv = EmptyVatInvoice();
        // Two lines at 23%, one at 8%
        inv.AddLine(InvoiceLine.Create(1, "A", 1m, new Money(100m, Pln), PricingMode.Net, Vat23));
        inv.AddLine(InvoiceLine.Create(2, "B", 1m, new Money(200m, Pln), PricingMode.Net, Vat23));
        inv.AddLine(InvoiceLine.Create(3, "C", 1m, new Money(50m, Pln), PricingMode.Net, Vat8));
        inv.RecalculateTotals();

        // Should have 2 groups: 23% and 8%
        Assert.Equal(2, inv.VatBreakdown.Count);

        var group23 = inv.VatBreakdown.Single(v => v.Rate.Rate?.Value == 23m);
        Assert.Equal(300m, group23.TaxableBase.Amount);
        Assert.Equal(69m, group23.VatAmount.Amount);

        var group8 = inv.VatBreakdown.Single(v => v.Rate.Rate?.Value == 8m);
        Assert.Equal(50m, group8.TaxableBase.Amount);
        Assert.Equal(4m, group8.VatAmount.Amount);
    }

    [Fact]
    public void RecalculateTotals_NoLines_TotalsAreZero()
    {
        var inv = EmptyVatInvoice();
        inv.RecalculateTotals();

        Assert.Equal(0m, inv.Totals.NetTotal.Amount);
        Assert.Equal(0m, inv.Totals.VatTotal.Amount);
        Assert.Equal(0m, inv.Totals.GrossTotal.Amount);
    }
}
