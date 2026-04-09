using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Tests.InvoiceLineTests;

public class InvoiceLineCalculationTests
{
    private static readonly CurrencyCode Pln = CurrencyCode.Pln;
    private static readonly VatRate Vat23 = VatRate.OfPercentage(new Percentage(23));
    private static readonly VatRate Vat8 = VatRate.OfPercentage(new Percentage(8));
    private static readonly VatRate VatZero = VatRate.Zero;
    private static readonly VatRate VatExempt = VatRate.OfExemption(new TaxExemptionReason("zw"));

    [Fact]
    public void Create_NetPricing_23Vat_CalculatesCorrectAmounts()
    {
        // 100 net × 23% = 23 VAT, 123 gross
        var line = Domain.Entities.InvoiceLine.Create(
            lineNumber: 1,
            description: "Service",
            quantity: 1m,
            unitPrice: new Money(100m, Pln),
            pricingMode: PricingMode.Net,
            vatRate: Vat23);

        Assert.Equal(100m, line.NetAmount.Amount);
        Assert.Equal(23m, line.VatAmount.Amount);
        Assert.Equal(123m, line.GrossAmount.Amount);
    }

    [Fact]
    public void Create_GrossPricing_23Vat_CalculatesCorrectAmounts()
    {
        // 123 gross → net = 123 / 1.23 ≈ 100, vat = 23
        var line = Domain.Entities.InvoiceLine.Create(
            lineNumber: 1,
            description: "Product",
            quantity: 1m,
            unitPrice: new Money(123m, Pln),
            pricingMode: PricingMode.Gross,
            vatRate: Vat23);

        Assert.Equal(100m, line.NetAmount.Amount);
        Assert.Equal(23m, line.VatAmount.Amount);
        Assert.Equal(123m, line.GrossAmount.Amount);
    }

    [Fact]
    public void Create_NetPricing_ZeroVat_VatAmountIsZero()
    {
        var line = Domain.Entities.InvoiceLine.Create(1, "Item", 1m, new Money(50m, Pln), PricingMode.Net, VatZero);

        Assert.Equal(0m, line.VatAmount.Amount);
        Assert.Equal(50m, line.NetAmount.Amount);
        Assert.Equal(50m, line.GrossAmount.Amount);
    }

    [Fact]
    public void Create_NetPricing_ExemptVat_VatAmountIsZero()
    {
        var line = Domain.Entities.InvoiceLine.Create(1, "Exempt service", 1m, new Money(200m, Pln), PricingMode.Net, VatExempt);

        Assert.Equal(0m, line.VatAmount.Amount);
        Assert.Equal(200m, line.NetAmount.Amount);
        Assert.Equal(200m, line.GrossAmount.Amount);
    }

    [Fact]
    public void Create_NetPricing_MultipleQuantity_ScalesAmounts()
    {
        var line = Domain.Entities.InvoiceLine.Create(1, "Item", 3m, new Money(100m, Pln), PricingMode.Net, Vat23);

        Assert.Equal(300m, line.NetAmount.Amount);
        Assert.Equal(69m, line.VatAmount.Amount);
        Assert.Equal(369m, line.GrossAmount.Amount);
    }

    [Fact]
    public void Create_NetPricing_WithDiscount_ReducesBase()
    {
        // 100 net × 50% discount = 50 net; × 23% = 11.50 VAT, 61.50 gross
        var line = Domain.Entities.InvoiceLine.Create(
            1, "Discounted", 1m, new Money(100m, Pln), PricingMode.Net, Vat23,
            discount: new Percentage(50));

        Assert.Equal(50m, line.NetAmount.Amount);
        Assert.Equal(11.50m, line.VatAmount.Amount);
        Assert.Equal(61.50m, line.GrossAmount.Amount);
    }

    [Fact]
    public void Create_GrossPricing_8Vat_CalculatesCorrectAmounts()
    {
        // 108 gross → net = 108/1.08 = 100, vat = 8
        var line = Domain.Entities.InvoiceLine.Create(
            1, "Food", 1m, new Money(108m, Pln), PricingMode.Gross, Vat8);

        Assert.Equal(100m, line.NetAmount.Amount);
        Assert.Equal(8m, line.VatAmount.Amount);
        Assert.Equal(108m, line.GrossAmount.Amount);
    }

    [Fact]
    public void Create_CorrectionRole_IsPreserved()
    {
        var line = Domain.Entities.InvoiceLine.Create(
            1, "Before", 1m, new Money(100m, Pln), PricingMode.Net, Vat23,
            correctionRole: CorrectionRole.BeforeCorrection);

        Assert.Equal(CorrectionRole.BeforeCorrection, line.CorrectionRole);
    }
}
