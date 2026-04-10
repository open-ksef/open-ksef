namespace OpenKSeF.Invoices.Domain.ValueObjects;

public sealed record Money(decimal Amount, CurrencyCode Currency)
{
    public static Money Zero(CurrencyCode currency) => new(0m, currency);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    public Money RoundTo(int decimals, MidpointRounding mode = MidpointRounding.AwayFromZero)
        => new(Math.Round(Amount, decimals, mode), Currency);

    public override string ToString() => $"{Amount:F2} {Currency}";

    private void EnsureSameCurrency(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException(
                $"Cannot operate on different currencies: {Currency} and {other.Currency}.");
    }
}
