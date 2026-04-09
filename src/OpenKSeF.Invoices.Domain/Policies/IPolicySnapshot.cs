namespace OpenKSeF.Invoices.Domain.Policies;

/// <summary>
/// Immutable snapshot of all tenant-level policy configuration resolved for a single operation.
/// Passed through <see cref="OpenKSeF.Invoices.Domain.Validation.ValidationContext"/> so validation rules
/// can make policy-aware decisions without querying storage.
/// </summary>
public interface IPolicySnapshot
{
    NumberingPolicy Numbering { get; }
    KsefPolicy Ksef { get; }
    VatPolicy Vat { get; }
    EditPolicy Edit { get; }
    ValidationPolicy Validation { get; }
    CurrencyPolicy Currency { get; }
}
