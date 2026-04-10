namespace OpenKSeF.Invoices.Domain.ValueObjects;

/// <summary>
/// GTU classification marker or equivalent VAT annotation required for certain goods/services.
/// E.g. "GTU_01" through "GTU_13" in Polish VAT rules.
/// </summary>
public sealed record VatClassification
{
    public string Code { get; }

    public VatClassification(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public override string ToString() => Code;
}
