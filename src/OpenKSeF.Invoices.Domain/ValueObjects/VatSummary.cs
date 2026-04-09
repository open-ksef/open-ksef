namespace OpenKSeF.Invoices.Domain.ValueObjects;

/// <summary>VAT breakdown for one tax treatment group.</summary>
public sealed record VatSummary(VatRate Rate, Money TaxableBase, Money VatAmount, Money GrossSubtotal);
