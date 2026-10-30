namespace OpenKSeF.Domain.Invoicing;

public sealed class IssuedInvoiceLine
{
    public const string DefaultUnit = "szt.";

    public Guid IssuedInvoiceId { get; private set; }
    public int LineNo { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Unit { get; private set; } = DefaultUnit;
    public decimal Quantity { get; private set; }
    public decimal UnitNetPrice { get; private set; }
    public VatRate VatRate { get; private set; }
    public decimal NetAmount { get; private set; }
    public decimal VatAmount { get; private set; }

    private IssuedInvoiceLine() { }

    internal IssuedInvoiceLine(Guid issuedInvoiceId, int lineNo, string? name, string? unit, decimal quantity, decimal unitNetPrice, VatRate vatRate)
    {
        IssuedInvoiceId = issuedInvoiceId;
        LineNo = lineNo;
        Name = name?.Trim() ?? string.Empty;
        Unit = string.IsNullOrWhiteSpace(unit) ? DefaultUnit : unit.Trim();
        Quantity = quantity;
        UnitNetPrice = unitNetPrice;
        VatRate = vatRate;
        NetAmount = Math.Round(quantity * unitNetPrice, 2, MidpointRounding.AwayFromZero);
        VatAmount = Math.Round(NetAmount * vatRate.Fraction(), 2, MidpointRounding.AwayFromZero);
    }
}
