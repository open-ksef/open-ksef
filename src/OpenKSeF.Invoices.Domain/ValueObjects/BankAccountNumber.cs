namespace OpenKSeF.Invoices.Domain.ValueObjects;

/// <summary>Bank account number (IBAN or domestic format).</summary>
public sealed record BankAccountNumber
{
    public string Value { get; }

    public BankAccountNumber(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public override string ToString() => Value;
}
