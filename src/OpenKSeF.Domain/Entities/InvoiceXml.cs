namespace OpenKSeF.Domain.Entities;

/// <summary>Invoice XML exactly as KSeF returned it, kept apart from <see cref="InvoiceHeader"/> so header loads stay small.</summary>
public class InvoiceXml
{
    public Guid InvoiceHeaderId { get; set; }
    public required byte[] Content { get; set; }
    public required string Sha256 { get; set; }
    public DateTime StoredAt { get; set; }

    public InvoiceHeader InvoiceHeader { get; set; } = null!;
}
