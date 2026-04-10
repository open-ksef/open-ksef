namespace OpenKSeF.Invoices.Domain.ValueObjects;

/// <summary>Human-readable document number (e.g. FV/2026/04/001).</summary>
public sealed record DocumentNumber
{
    public string Value { get; }

    public DocumentNumber(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 256)
            throw new ArgumentException("Document number cannot exceed 256 characters.", nameof(value));
        Value = value;
    }

    public override string ToString() => Value;
}
