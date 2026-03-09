namespace OpenKSeF.Domain.Entities;

public class Tenant
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public required string Nip { get; set; }
    public string? DisplayName { get; set; }
    public string? NotificationEmail { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<KSeFCredential> KSeFCredentials { get; set; } = [];
    public ICollection<InvoiceHeader> Invoices { get; set; } = [];
    public SyncState? SyncState { get; set; }
    public ICollection<DeviceToken> DeviceTokens { get; set; } = [];
}
