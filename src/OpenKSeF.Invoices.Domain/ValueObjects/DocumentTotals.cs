namespace OpenKSeF.Invoices.Domain.ValueObjects;

public sealed record DocumentTotals(Money NetTotal, Money VatTotal, Money GrossTotal)
{
    public static DocumentTotals Zero(CurrencyCode currency) =>
        new(Money.Zero(currency), Money.Zero(currency), Money.Zero(currency));

    public bool IsConsistent(decimal tolerance = 0.01m) =>
        Math.Abs(GrossTotal.Amount - (NetTotal.Amount + VatTotal.Amount)) <= tolerance;
}
