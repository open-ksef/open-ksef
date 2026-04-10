using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Domain.Entities;

/// <summary>
/// An invoice line entity belonging to an <see cref="Invoice"/> aggregate.
/// Calculates monetary amounts from unit price, quantity, pricing mode, VAT rate, and discount.
/// </summary>
public sealed class InvoiceLine
{
    public LineId LineId { get; private init; } = LineId.New();
    public int LineNumber { get; private init; }
    public string Description { get; private init; } = string.Empty;
    public decimal Quantity { get; private init; }
    public string? UnitOfMeasure { get; private init; }
    public Money UnitPrice { get; private init; } = null!;
    public PricingMode PricingMode { get; private init; }
    public Percentage? Discount { get; private init; }
    public VatRate VatRate { get; private init; } = VatRate.Zero;
    public VatClassification? VatClassification { get; private init; }
    public CorrectionRole CorrectionRole { get; private init; } = CorrectionRole.Normal;

    public Money NetAmount { get; private set; } = null!;
    public Money VatAmount { get; private set; } = null!;
    public Money GrossAmount { get; private set; } = null!;

    private InvoiceLine() { }

    public static InvoiceLine Create(
        int lineNumber,
        string description,
        decimal quantity,
        Money unitPrice,
        PricingMode pricingMode,
        VatRate vatRate,
        Percentage? discount = null,
        string? unitOfMeasure = null,
        VatClassification? vatClassification = null,
        CorrectionRole correctionRole = CorrectionRole.Normal)
    {
        ArgumentNullException.ThrowIfNull(unitPrice);
        ArgumentNullException.ThrowIfNull(vatRate);

        var line = new InvoiceLine
        {
            LineNumber = lineNumber,
            Description = description ?? string.Empty,
            Quantity = quantity,
            UnitOfMeasure = unitOfMeasure,
            UnitPrice = unitPrice,
            PricingMode = pricingMode,
            Discount = discount,
            VatRate = vatRate,
            VatClassification = vatClassification,
            CorrectionRole = correctionRole
        };

        line.Recalculate();
        return line;
    }

    private void Recalculate()
    {
        var discountFactor = 1m - (Discount?.AsFraction ?? 0m);
        var effectiveVatFraction = VatRate.EffectiveFraction;
        var currency = UnitPrice.Currency;

        if (PricingMode == PricingMode.Net)
        {
            var rawNet = UnitPrice.Amount * Quantity * discountFactor;
            var net = Math.Round(rawNet, 2, MidpointRounding.AwayFromZero);
            var rawVat = net * effectiveVatFraction;
            var vat = Math.Round(rawVat, 2, MidpointRounding.AwayFromZero);

            NetAmount = new Money(net, currency);
            VatAmount = new Money(vat, currency);
            GrossAmount = new Money(net + vat, currency);
        }
        else // Gross
        {
            var rawGross = UnitPrice.Amount * Quantity * discountFactor;
            var gross = Math.Round(rawGross, 2, MidpointRounding.AwayFromZero);
            var rawNet = gross / (1m + effectiveVatFraction);
            var net = Math.Round(rawNet, 2, MidpointRounding.AwayFromZero);

            GrossAmount = new Money(gross, currency);
            NetAmount = new Money(net, currency);
            VatAmount = new Money(gross - net, currency);
        }
    }
}
