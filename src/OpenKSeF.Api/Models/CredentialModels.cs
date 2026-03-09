using OpenKSeF.Domain.Entities;

namespace OpenKSeF.Api.Models;

public record AddCredentialRequest(
    CredentialType Type = CredentialType.Token,
    string? Token = null,
    string? CertificatePem = null,
    string? PrivateKeyPem = null,
    string? CertificateBase64 = null,
    string? CertificatePassword = null);

public record CredentialStatusResponse(
    bool Exists,
    CredentialType? CredentialType,
    DateTime? UpdatedAt);
