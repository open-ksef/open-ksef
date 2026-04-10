namespace OpenKSeF.Invoices.Domain.ValueObjects;

/// <summary>Represents a percentage value (e.g. 23 for 23%).</summary>
public sealed record Percentage
{
    public decimal Value { get; }

    public Percentage(decimal value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Percentage cannot be negative.");
        Value = value;
    }

    public decimal AsFraction => Value / 100m;

    public override string ToString() => $"{Value}%";
}
