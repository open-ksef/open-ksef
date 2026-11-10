using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KSeF.Client.Core.Interfaces;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;

using CirfmfKSeFClient = KSeF.Client.Core.Interfaces.Clients.IKSeFClient;

namespace OpenKSeF.Api.Tests.Services;

public class KSeFGatewayLoginTests
{
    private const string Nip = "1111111111";
    private readonly IAuthCoordinator _auth = Substitute.For<IAuthCoordinator>();
    private readonly ICryptographyService _cryptography = Substitute.For<ICryptographyService>();
    private readonly AesGcmEncryptionService _encryption = new(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public async Task TokenCredential_LogsInWithDecryptedKsefToken()
    {
        _auth.AuthKsefTokenAsync(default, default!, default!, default!, default, default, default).ReturnsForAnyArgs(Login());
        var credential = new KSeFCredential { Type = CredentialType.Token, EncryptedToken = _encryption.Encrypt("ksef-token") };

        var session = await Gateway().InitSessionAsync(Nip, credential);

        Assert.Equal("access", session.Token);
        await _auth.Received(1).AuthKsefTokenAsync(
            AuthenticationTokenContextIdentifierType.Nip, Nip, "ksef-token", _cryptography, EncryptionMethodEnum.Rsa, null, Arg.Any<CancellationToken>());
        await _auth.DidNotReceiveWithAnyArgs().AuthAsync(default, default!, default, default!, default, default, default);
    }

    [Fact]
    public async Task CertificateCredential_SignsChallengeWithStoredCertificate()
    {
        using var rsa = RSA.Create(2048);
        using var certificate = new CertificateRequest("CN=Test, OID.2.5.4.97=VATPL-1111111111", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var credential = new KSeFCredential
        {
            Type = CredentialType.Certificate,
            EncryptedCertificateData = _encryption.Encrypt(Convert.ToBase64String(certificate.Export(X509ContentType.Pfx)))
        };
        string? signed = null;
        _auth.AuthAsync(default, default!, default, default!, default, default, default).ReturnsForAnyArgs(async call =>
        {
            signed = await call.Arg<Func<string, Task<string>>>()("<AuthTokenRequest xmlns=\"http://ksef.mf.gov.pl/auth/token/2.0\"><Challenge>c</Challenge></AuthTokenRequest>");
            return Login();
        });

        var session = await Gateway().InitSessionAsync(Nip, credential);

        Assert.Equal("access", session.Token);
        await _auth.Received(1).AuthAsync(
            AuthenticationTokenContextIdentifierType.Nip, Nip, AuthenticationTokenSubjectIdentifierTypeEnum.CertificateSubject,
            Arg.Any<Func<string, Task<string>>>(), Arg.Any<AuthenticationTokenAuthorizationPolicy>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        Assert.Contains("SignatureValue", signed);
        Assert.Contains(Convert.ToBase64String(certificate.RawData), signed);
        await _auth.DidNotReceiveWithAnyArgs().AuthKsefTokenAsync(default, default!, default!, default!, default, default, default);
    }

    private KSeFGateway Gateway() =>
        new(Substitute.For<CirfmfKSeFClient>(), _auth, _cryptography, _encryption, NullLogger<KSeFGateway>.Instance);

    private static AuthenticationOperationStatusResponse Login() => new()
    {
        AccessToken = new TokenInfo { Token = "access", ValidUntil = DateTimeOffset.UtcNow.AddMinutes(15) }
    };
}
