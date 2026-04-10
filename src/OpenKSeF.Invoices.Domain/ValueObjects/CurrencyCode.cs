namespace OpenKSeF.Invoices.Domain.ValueObjects;

public sealed record CurrencyCode
{
    public static readonly CurrencyCode Pln = new("PLN");

    public string Value { get; }

    public CurrencyCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != 3)
            throw new ArgumentException("Currency code must be 3 characters (ISO 4217).", nameof(value));
        Value = value.ToUpperInvariant();
    }

    public override string ToString() => Value;
}
