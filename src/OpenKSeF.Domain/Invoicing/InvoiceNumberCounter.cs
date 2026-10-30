namespace OpenKSeF.Domain.Invoicing;

public sealed class InvoiceNumberCounter
{
    public Guid TenantId { get; set; }
    public required string Period { get; set; }
    public int Next { get; set; }
}
