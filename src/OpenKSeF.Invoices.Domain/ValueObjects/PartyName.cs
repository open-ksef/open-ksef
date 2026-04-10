namespace OpenKSeF.Invoices.Domain.ValueObjects;

public sealed record PartyName
{
    public string Value { get; }

    public PartyName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 500)
            throw new ArgumentException("Party name cannot exceed 500 characters.", nameof(value));
        Value = value;
    }

    public override string ToString() => Value;
}
