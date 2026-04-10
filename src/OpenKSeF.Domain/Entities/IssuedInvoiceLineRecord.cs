namespace OpenKSeF.Domain.Entities;

/// <summary>
/// EF Core persistence record for a line item belonging to <see cref="IssuedInvoiceRecord"/>.
/// </summary>
public class IssuedInvoiceLineRecord
{
    public Guid Id { get; set; }
    public Guid IssuedInvoiceId { get; set; }
    public int LineNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? UnitOfMeasure { get; set; }
    public string PricingMode { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal? DiscountPercent { get; set; }
    public string VatRate { get; set; } = string.Empty;
    public string? VatClassification { get; set; }
    public string? CorrectionRole { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal GrossAmount { get; set; }

    public IssuedInvoiceRecord IssuedInvoice { get; set; } = null!;
}
