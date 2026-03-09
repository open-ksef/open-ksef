using OpenKSeF.Domain.Entities;

namespace OpenKSeF.Api.Models;

public record TenantCredentialStatusResponse(
    Guid TenantId,
    string TenantDisplayName,
    bool HasCredential,
    CredentialType? CredentialType,
    DateTime? LastUpdatedAt);
