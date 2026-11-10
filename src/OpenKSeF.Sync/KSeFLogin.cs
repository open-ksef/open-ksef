using System.Security.Cryptography.X509Certificates;
using KSeF.Client.Api.Services;
using KSeF.Client.Core.Interfaces;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models.Authorization;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Sync;

/// <summary>KSeF login with a tenant's stored credential, shared by sync and issuing: KSeF token, or XAdES signed with the stored certificate.</summary>
internal static class KSeFLogin
{
    public static async Task<AuthenticationOperationStatusResponse> LoginAsync(
        this IAuthCoordinator coordinator, string nip, KSeFCredential credential,
        IEncryptionService encryption, ICryptographyService cryptography, CancellationToken ct)
    {
        if (credential.Type == CredentialType.Certificate)
        {
            if (string.IsNullOrEmpty(credential.EncryptedCertificateData))
                throw new InvalidOperationException("Certificate credential is missing certificate data.");

            using var certificate = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(encryption.Decrypt(credential.EncryptedCertificateData)), null);
            return await coordinator.AuthAsync(
                AuthenticationTokenContextIdentifierType.Nip,
                nip,
                AuthenticationTokenSubjectIdentifierTypeEnum.CertificateSubject,
                xmlSigner: xml => Task.FromResult(SignatureService.Sign(xml, certificate)),
                cancellationToken: ct);
        }

        if (string.IsNullOrEmpty(credential.EncryptedToken))
            throw new InvalidOperationException("Token credential is missing encrypted token data.");

        return await coordinator.AuthKsefTokenAsync(
            AuthenticationTokenContextIdentifierType.Nip,
            nip,
            encryption.Decrypt(credential.EncryptedToken),
            cryptography,
            EncryptionMethodEnum.Rsa,
            cancellationToken: ct);
    }
}
