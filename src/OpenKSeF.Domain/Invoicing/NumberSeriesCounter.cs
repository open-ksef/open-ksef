namespace OpenKSeF.Domain.Invoicing;

public sealed class NumberSeriesCounter
{
    public Guid TenantId { get; set; }
    public required string Series { get; set; }
    public required string Period { get; set; }
    public int Next { get; set; }
}
