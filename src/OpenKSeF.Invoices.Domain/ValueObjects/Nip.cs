namespace OpenKSeF.Invoices.Domain.ValueObjects;

/// <summary>Polish NIP (tax identification number) — 10 digits.</summary>
public sealed record Nip
{
    public string Value { get; }

    public Nip(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var digits = value.Replace("-", "").Trim();
        if (digits.Length != 10 || !digits.All(char.IsDigit))
            throw new ArgumentException($"NIP must be exactly 10 digits. Got: '{value}'", nameof(value));
        Value = digits;
    }

    public override string ToString() => Value;
}
