namespace OpenKSeF.Domain.Entities;

public class KSeFCredential
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public CredentialType Type { get; set; } = CredentialType.Token;
    public string? EncryptedToken { get; set; }
    public string? EncryptedCertificateData { get; set; }
    public string? CertificateFingerprint { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
}
