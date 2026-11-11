using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenKSeF.Api.Controllers;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;

namespace OpenKSeF.Api.Tests.Controllers;

public class CredentialsControllerListTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly string _userId = "user-1";

    public CredentialsControllerListTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);

        _currentUser = Substitute.For<ICurrentUserService>();
        _currentUser.UserId.Returns(_userId);
    }

    private CredentialsSummaryController CreateController() =>
        new(_db, _currentUser);

    [Fact]
    public async Task ListCredentials_ReturnsTenantStatusesForUser()
    {
        var now = DateTime.UtcNow;
        var t1 = new Tenant { Id = Guid.NewGuid(), UserId = _userId, Nip = "1111111111", DisplayName = "T1", CreatedAt = now, UpdatedAt = now };
        var t2 = new Tenant { Id = Guid.NewGuid(), UserId = _userId, Nip = "2222222222", DisplayName = "T2", CreatedAt = now, UpdatedAt = now };
        var otherTenant = new Tenant { Id = Guid.NewGuid(), UserId = "other", Nip = "3333333333", DisplayName = "Other", CreatedAt = now, UpdatedAt = now };
        _db.Tenants.AddRange(t1, t2, otherTenant);

        _db.KSeFCredentials.Add(new KSeFCredential { Id = Guid.NewGuid(), TenantId = t1.Id, Type = CredentialType.Token, EncryptedToken = "enc", CreatedAt = now, UpdatedAt = now });
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.ListCredentials() as OkObjectResult;

        var list = Assert.IsAssignableFrom<IEnumerable<TenantCredentialStatusResponse>>(result!.Value);
        var items = list.ToList();
        Assert.Equal(2, items.Count);
        var t1Status = items.Single(x => x.TenantId == t1.Id);
        var t2Status = items.Single(x => x.TenantId == t2.Id);
        Assert.True(t1Status.HasCredential);
        Assert.Equal(CredentialType.Token, t1Status.CredentialType);
        Assert.False(t2Status.HasCredential);
        Assert.Null(t2Status.CredentialType);
    }

    [Fact]
    public async Task ListCredentials_NoTenants_ReturnsEmpty()
    {
        var controller = CreateController();
        var result = await controller.ListCredentials() as OkObjectResult;

        var list = Assert.IsAssignableFrom<IEnumerable<TenantCredentialStatusResponse>>(result!.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task ListCredentials_IncludesCredentialType_Certificate()
    {
        var now = DateTime.UtcNow;
        var tenant = new Tenant { Id = Guid.NewGuid(), UserId = _userId, Nip = "4444444444", DisplayName = "CertCo", CreatedAt = now, UpdatedAt = now };
        _db.Tenants.Add(tenant);
        _db.KSeFCredentials.Add(new KSeFCredential
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Type = CredentialType.Certificate,
            EncryptedCertificateData = "enc-cert",
            CertificateFingerprint = "abc123",
            CreatedAt = now,
            UpdatedAt = now
        });
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.ListCredentials() as OkObjectResult;
        var list = Assert.IsAssignableFrom<IEnumerable<TenantCredentialStatusResponse>>(result!.Value).ToList();
        Assert.Single(list);
        Assert.Equal(CredentialType.Certificate, list[0].CredentialType);
    }

    public void Dispose() => _db.Dispose();
}

public class CredentialsControllerCrudTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IEncryptionService _encryption;
    private readonly ILogger<CredentialsController> _logger = Substitute.For<ILogger<CredentialsController>>();
    private readonly string _userId = "user-1";
    private readonly Guid _tenantId;

    public CredentialsControllerCrudTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);

        _currentUser = Substitute.For<ICurrentUserService>();
        _currentUser.UserId.Returns(_userId);

        _encryption = Substitute.For<IEncryptionService>();
        _encryption.Encrypt(Arg.Any<string>()).Returns(x => $"encrypted:{x.Arg<string>()}");
        _encryption.Decrypt(Arg.Any<string>()).Returns(x => x.Arg<string>()["encrypted:".Length..]);

        _tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        _db.Tenants.Add(new Tenant { Id = _tenantId, UserId = _userId, Nip = "5555555555", DisplayName = "Test", CreatedAt = now, UpdatedAt = now });
        _db.SaveChanges();
    }

    private CredentialsController CreateController() =>
        new(_db, _currentUser, _encryption, CredentialsControllerSyncTests.Jobs(Substitute.For<ITenantSyncService>()), _logger);

    [Fact]
    public async Task AddOrUpdate_Token_CreatesCredential()
    {
        var controller = CreateController();
        var request = new AddCredentialRequest(Type: CredentialType.Token, Token: "my-token");

        var result = await controller.AddOrUpdate(_tenantId, request);

        Assert.IsType<OkObjectResult>(result);
        var cred = await _db.KSeFCredentials.FirstAsync(c => c.TenantId == _tenantId);
        Assert.Equal(CredentialType.Token, cred.Type);
        Assert.Equal("encrypted:my-token", cred.EncryptedToken);
        Assert.Null(cred.EncryptedCertificateData);
        Assert.Null(cred.CertificateFingerprint);
    }

    [Fact]
    public async Task AddOrUpdate_Token_MissingToken_ReturnsBadRequest()
    {
        var controller = CreateController();
        var request = new AddCredentialRequest(Type: CredentialType.Token, Token: null);

        var result = await controller.AddOrUpdate(_tenantId, request);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AddOrUpdate_Certificate_CreatesCredential()
    {
        var pfxBase64 = GenerateTestPfxBase64("Test1234!");
        var controller = CreateController();
        var request = new AddCredentialRequest(
            Type: CredentialType.Certificate,
            CertificateBase64: pfxBase64,
            CertificatePassword: "Test1234!");

        var result = await controller.AddOrUpdate(_tenantId, request);

        Assert.IsType<OkObjectResult>(result);
        var cred = await _db.KSeFCredentials.FirstAsync(c => c.TenantId == _tenantId);
        Assert.Equal(CredentialType.Certificate, cred.Type);
        Assert.Null(cred.EncryptedToken);
        Assert.NotNull(cred.EncryptedCertificateData);
        Assert.NotNull(cred.CertificateFingerprint);
        Assert.Matches("^[0-9a-f]{64}$", cred.CertificateFingerprint);
    }

    [Fact]
    public async Task AddOrUpdate_Certificate_MissingFields_ReturnsBadRequest()
    {
        var controller = CreateController();
        var request = new AddCredentialRequest(Type: CredentialType.Certificate, CertificateBase64: null);

        var result = await controller.AddOrUpdate(_tenantId, request);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AddOrUpdate_Certificate_InvalidPfx_ReturnsBadRequest()
    {
        var controller = CreateController();
        var request = new AddCredentialRequest(
            Type: CredentialType.Certificate,
            CertificateBase64: Convert.ToBase64String(new byte[] { 1, 2, 3 }),
            CertificatePassword: "wrong");

        var result = await controller.AddOrUpdate(_tenantId, request);

        Assert.Equal(CredentialsController.UnreadableCertificateMessage, Error(result));
    }

    [Fact]
    public async Task AddOrUpdate_Certificate_InvalidBase64_ReturnsBadRequest()
    {
        var controller = CreateController();
        var request = new AddCredentialRequest(
            Type: CredentialType.Certificate,
            CertificateBase64: "not-base64!!!",
            CertificatePassword: "test");

        var result = await controller.AddOrUpdate(_tenantId, request);

        Assert.Equal(CredentialsController.UnreadableCertificateMessage, Error(result));
    }

    [Fact]
    public async Task AddOrUpdate_Pfx_MissingPassword_ReturnsPolishError()
    {
        var result = await CreateController().AddOrUpdate(_tenantId, new AddCredentialRequest(
            Type: CredentialType.Certificate,
            CertificateBase64: GenerateTestPfxBase64("Test1234!")));

        Assert.Equal(CredentialsController.MissingCertificatePasswordMessage, Error(result));
    }

    [Fact]
    public async Task AddOrUpdate_UpdatesExistingCredential()
    {
        var now = DateTime.UtcNow;
        _db.KSeFCredentials.Add(new KSeFCredential
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Type = CredentialType.Token,
            EncryptedToken = "old-enc",
            CreatedAt = now,
            UpdatedAt = now
        });
        await _db.SaveChangesAsync();

        var pfxBase64 = GenerateTestPfxBase64("Test1234!");
        var controller = CreateController();
        var request = new AddCredentialRequest(
            Type: CredentialType.Certificate,
            CertificateBase64: pfxBase64,
            CertificatePassword: "Test1234!");

        var result = await controller.AddOrUpdate(_tenantId, request);

        Assert.IsType<OkObjectResult>(result);
        var creds = await _db.KSeFCredentials.Where(c => c.TenantId == _tenantId).ToListAsync();
        Assert.Single(creds);
        Assert.Equal(CredentialType.Certificate, creds[0].Type);
        Assert.Null(creds[0].EncryptedToken);
        Assert.NotNull(creds[0].EncryptedCertificateData);
    }

    [Fact]
    public async Task Status_LegacyPasswordProtectedPfx_ReturnsTypeWithoutValidTo()
    {
        var now = DateTime.UtcNow;
        _db.KSeFCredentials.Add(new KSeFCredential
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Type = CredentialType.Certificate,
            EncryptedCertificateData = "encrypted:" + GenerateTestPfxBase64("Test1234!"),
            CertificateFingerprint = "fp",
            CreatedAt = now,
            UpdatedAt = now
        });
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.Status(_tenantId) as OkObjectResult;
        var status = Assert.IsType<CredentialStatusResponse>(result!.Value);
        Assert.True(status.Exists);
        Assert.Equal(CredentialType.Certificate, status.CredentialType);
        Assert.Null(status.CertificateValidTo);
    }

    [Fact]
    public async Task AddOrUpdate_Pfx_StoredWithoutPassword_StatusReturnsValidTo()
    {
        var validTo = DateTimeOffset.UtcNow.AddDays(30);
        var controller = CreateController();

        var result = await controller.AddOrUpdate(_tenantId, new AddCredentialRequest(
            Type: CredentialType.Certificate,
            CertificateBase64: GenerateTestPfxBase64("Test1234!", validTo),
            CertificatePassword: "Test1234!"));

        Assert.IsType<OkObjectResult>(result);
        var stored = (await _db.KSeFCredentials.SingleAsync(c => c.TenantId == _tenantId)).EncryptedCertificateData!;
        using (var cert = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(stored["encrypted:".Length..]), null))
            Assert.True(cert.HasPrivateKey);
        var status = Assert.IsType<CredentialStatusResponse>(((OkObjectResult)await controller.Status(_tenantId)).Value);
        Assert.Equal(validTo.UtcDateTime, status.CertificateValidTo!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal(DateTimeKind.Utc, status.CertificateValidTo.Value.Kind);
    }

    [Fact]
    public async Task AddOrUpdate_PfxWithoutPrivateKey_ReturnsPolishError_SavesNothing()
    {
        using var rsa = RSA.Create(2048);
        using var cert = new CertificateRequest("CN=No Key", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
        using var publicOnly = X509CertificateLoader.LoadCertificate(cert.RawData);

        var result = await CreateController().AddOrUpdate(_tenantId, new AddCredentialRequest(
            Type: CredentialType.Certificate,
            CertificateBase64: Convert.ToBase64String(publicOnly.Export(X509ContentType.Pfx, "Test1234!")),
            CertificatePassword: "Test1234!"));

        Assert.Equal(CredentialsController.MissingPrivateKeyMessage, Error(result));
        Assert.False(await _db.KSeFCredentials.AnyAsync());
    }

    [Fact]
    public async Task AddOrUpdate_ExpiredCertificate_ReturnsPolishError_SavesNothing()
    {
        using var rsa = RSA.Create(2048);
        using var cert = new CertificateRequest("CN=Expired", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(-1));

        var result = await CreateController().AddOrUpdate(_tenantId, new AddCredentialRequest(
            Type: CredentialType.Certificate,
            CertificatePem: new string(PemEncoding.Write("CERTIFICATE", cert.RawData)),
            PrivateKeyPem: new string(PemEncoding.Write("PRIVATE KEY", rsa.ExportPkcs8PrivateKey()))));

        Assert.Equal(CredentialsController.ExpiredCertificateMessage, Error(result));
        Assert.False(await _db.KSeFCredentials.AnyAsync());
    }

    private static string? Error(IActionResult result)
    {
        var value = Assert.IsType<BadRequestObjectResult>(result).Value!;
        return (string?)value.GetType().GetProperty("error")!.GetValue(value);
    }

    [Fact]
    public async Task AddOrUpdate_WrongTenant_ReturnsForbid()
    {
        var otherTenantId = Guid.NewGuid();
        var controller = CreateController();
        var request = new AddCredentialRequest(Type: CredentialType.Token, Token: "test");

        var result = await controller.AddOrUpdate(otherTenantId, request);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task AddOrUpdate_PemCertificate_EcdsaEncryptedKey_CreatesCredential()
    {
        var (certPem, keyPem) = GenerateTestEcdsaPem("TestPwd123!");
        var controller = CreateController();
        var request = new AddCredentialRequest(
            Type: CredentialType.Certificate,
            CertificatePem: certPem,
            PrivateKeyPem: keyPem,
            CertificatePassword: "TestPwd123!");

        var result = await controller.AddOrUpdate(_tenantId, request);

        Assert.IsType<OkObjectResult>(result);
        var cred = await _db.KSeFCredentials.FirstAsync(c => c.TenantId == _tenantId);
        Assert.Equal(CredentialType.Certificate, cred.Type);
        Assert.Null(cred.EncryptedToken);
        Assert.NotNull(cred.EncryptedCertificateData);
        Assert.NotNull(cred.CertificateFingerprint);
        Assert.Matches("^[0-9a-f]{64}$", cred.CertificateFingerprint);
    }

    [Fact]
    public async Task AddOrUpdate_PemCertificate_UnencryptedKey_WorksWithoutPassword()
    {
        var (certPem, keyPem) = GenerateTestEcdsaPem(password: null);
        var controller = CreateController();
        var request = new AddCredentialRequest(
            Type: CredentialType.Certificate,
            CertificatePem: certPem,
            PrivateKeyPem: keyPem);

        var result = await controller.AddOrUpdate(_tenantId, request);

        Assert.IsType<OkObjectResult>(result);
        var cred = await _db.KSeFCredentials.FirstAsync(c => c.TenantId == _tenantId);
        Assert.Equal(CredentialType.Certificate, cred.Type);
        Assert.NotNull(cred.EncryptedCertificateData);
        Assert.NotNull(cred.CertificateFingerprint);
    }

    [Fact]
    public async Task AddOrUpdate_PemCertificate_InvalidPem_ReturnsBadRequest()
    {
        var controller = CreateController();
        var request = new AddCredentialRequest(
            Type: CredentialType.Certificate,
            CertificatePem: "not a cert",
            PrivateKeyPem: "not a key",
            CertificatePassword: "test");

        var result = await controller.AddOrUpdate(_tenantId, request);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AddOrUpdate_PemCertificate_MissingKeyFile_ReturnsBadRequest()
    {
        var (certPem, _) = GenerateTestEcdsaPem(password: null);
        var controller = CreateController();
        var request = new AddCredentialRequest(
            Type: CredentialType.Certificate,
            CertificatePem: certPem,
            PrivateKeyPem: "");

        var result = await controller.AddOrUpdate(_tenantId, request);

        Assert.Equal(CredentialsController.MissingPrivateKeyMessage, Error(result));
    }

    [Theory]
    [InlineData(PbeEncryptionAlgorithm.Aes256Cbc, "SHA256")]
    [InlineData(PbeEncryptionAlgorithm.TripleDes3KeyPkcs12, "SHA1")]
    public async Task AddOrUpdate_EncryptedPemKey_IterationsAboveCap_ReturnsPolishError_SavesNothing(PbeEncryptionAlgorithm algorithm, string hash)
    {
        using var rsa = RSA.Create(2048);
        using var cert = new CertificateRequest("CN=Costly Key", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
        var keyPem = rsa.ExportEncryptedPkcs8PrivateKeyPem("Test1234!",
            new PbeParameters(algorithm, new HashAlgorithmName(hash), CredentialsController.MaxKeyKdfIterations + 1));

        var result = await CreateController().AddOrUpdate(_tenantId, new AddCredentialRequest(
            Type: CredentialType.Certificate,
            CertificatePem: cert.ExportCertificatePem(),
            PrivateKeyPem: keyPem,
            CertificatePassword: "Test1234!"));

        Assert.Equal(CredentialsController.KeyKdfTooExpensiveMessage, Error(result));
        Assert.False(await _db.KSeFCredentials.AnyAsync());
    }

    [Fact]
    public async Task Status_StoredCertificateDecryptFails_Throws_NotReportedAsLegacy()
    {
        var now = DateTime.UtcNow;
        _db.KSeFCredentials.Add(new KSeFCredential
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, Type = CredentialType.Certificate,
            EncryptedCertificateData = "encrypted:stale", CertificateFingerprint = "fp", CreatedAt = now, UpdatedAt = now
        });
        await _db.SaveChangesAsync();
        _encryption.Decrypt("encrypted:stale").Returns(_ => throw new AuthenticationTagMismatchException());

        await Assert.ThrowsAnyAsync<CryptographicException>(() => CreateController().Status(_tenantId));
    }

    [Fact]
    public async Task AddOrUpdate_PemCertificate_RsaKey_CreatesCredential()
    {
        var (certPem, keyPem) = GenerateTestRsaPem(password: null);
        var controller = CreateController();
        var request = new AddCredentialRequest(
            Type: CredentialType.Certificate,
            CertificatePem: certPem,
            PrivateKeyPem: keyPem);

        var result = await controller.AddOrUpdate(_tenantId, request);

        Assert.IsType<OkObjectResult>(result);
        var cred = await _db.KSeFCredentials.FirstAsync(c => c.TenantId == _tenantId);
        Assert.Equal(CredentialType.Certificate, cred.Type);
        Assert.NotNull(cred.EncryptedCertificateData);
        Assert.NotNull(cred.CertificateFingerprint);
    }

    private static string GenerateTestPfxBase64(string password, DateTimeOffset? validTo = null)
    {
        using var rsa = RSA.Create(2048);
        var distinguishedName = new X500DistinguishedName("CN=E2E Test");
        var req = new CertificateRequest(distinguishedName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow, validTo ?? DateTimeOffset.UtcNow.AddDays(1));
        var pfxBytes = cert.Export(X509ContentType.Pfx, password);
        return Convert.ToBase64String(pfxBytes);
    }

    private static (string certPem, string keyPem) GenerateTestEcdsaPem(string? password)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var distinguishedName = new X500DistinguishedName("CN=ECDSA Test");
        var req = new CertificateRequest(distinguishedName, ecdsa, HashAlgorithmName.SHA256);
        // CreateFromEncryptedPem picks ECDH for an EC key with no KeyUsage, and Windows CNG refuses it. KSeF certificates carry a signing KeyUsage.
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));

        var certPem = new string(PemEncoding.Write("CERTIFICATE", cert.RawData));

        string keyPem;
        if (password is not null)
        {
            var pbeParams = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000);
            var keyBytes = ecdsa.ExportEncryptedPkcs8PrivateKey(password, pbeParams);
            keyPem = new string(PemEncoding.Write("ENCRYPTED PRIVATE KEY", keyBytes));
        }
        else
        {
            var keyBytes = ecdsa.ExportPkcs8PrivateKey();
            keyPem = new string(PemEncoding.Write("PRIVATE KEY", keyBytes));
        }

        return (certPem, keyPem);
    }

    private static (string certPem, string keyPem) GenerateTestRsaPem(string? password)
    {
        using var rsa = RSA.Create(2048);
        var distinguishedName = new X500DistinguishedName("CN=RSA PEM Test");
        var req = new CertificateRequest(distinguishedName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));

        var certPem = new string(PemEncoding.Write("CERTIFICATE", cert.RawData));

        string keyPem;
        if (password is not null)
        {
            var pbeParams = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000);
            var keyBytes = rsa.ExportEncryptedPkcs8PrivateKey(password, pbeParams);
            keyPem = new string(PemEncoding.Write("ENCRYPTED PRIVATE KEY", keyBytes));
        }
        else
        {
            var keyBytes = rsa.ExportPkcs8PrivateKey();
            keyPem = new string(PemEncoding.Write("PRIVATE KEY", keyBytes));
        }

        return (certPem, keyPem);
    }

    public void Dispose() => _db.Dispose();
}
