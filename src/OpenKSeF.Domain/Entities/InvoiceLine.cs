namespace OpenKSeF.Domain.Entities;

public class InvoiceLine
{
    public Guid Id { get; set; }
    public Guid InvoiceHeaderId { get; set; }
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

    public InvoiceHeader InvoiceHeader { get; set; } = null!;
}
