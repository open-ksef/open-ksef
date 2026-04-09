namespace OpenKSeF.Invoices.Domain.ValueObjects;

/// <summary>
/// Code-based reason for VAT exemption (e.g. "zw", "np.", Art. 43 references, etc.).
/// Stored as an opaque string code whose valid values are governed by IVatPolicy.
/// </summary>
public sealed record TaxExemptionReason
{
    public string Code { get; }

    public TaxExemptionReason(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public override string ToString() => Code;
}
