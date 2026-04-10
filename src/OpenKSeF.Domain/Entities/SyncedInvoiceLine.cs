namespace OpenKSeF.Domain.Entities;

/// <summary>
/// Synchronized read-side entity for a line item belonging to <see cref="SyncedInvoice"/>.
/// Do NOT add business behaviour here.
/// </summary>
public class SyncedInvoiceLine
{
    public Guid Id { get; set; }
    /// <summary>FK to <see cref="SyncedInvoice"/>. Stored as column <c>InvoiceHeaderId</c> in the database.</summary>
    public Guid SyncedInvoiceId { get; set; }
    public int LineNumber { get; set; }
    public string? Name { get; set; }
    public string? Unit { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? UnitPriceNet { get; set; }
    public decimal? UnitPriceGross { get; set; }
    public decimal? AmountNet { get; set; }
    public decimal? AmountGross { get; set; }
    public decimal? AmountVat { get; set; }
    public string? VatRate { get; set; }

    public SyncedInvoice SyncedInvoice { get; set; } = null!;
}
