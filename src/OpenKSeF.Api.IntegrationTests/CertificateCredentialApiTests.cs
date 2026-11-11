using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenKSeF.Api.IntegrationTests.Infrastructure;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class CertificateCredentialApiTests : IDisposable
{
    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _client;

    private const string TestKeyPassword = "OpenKSeF-selfsigned-test-key";
    private static readonly (string CertPem, string KeyPem) TestCertificate = CreateTestCertificate();

    public CertificateCredentialApiTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
        _factory = new OpenKSeFWebApplicationFactory(fixture);
        _client = _factory.CreateClient();
    }

    private static (string CertPem, string KeyPem) CreateTestCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            "CN=OpenKSeF Test, OID.2.5.4.97=VATPL-1111111111, C=PL", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var keyPem = key.ExportEncryptedPkcs8PrivateKeyPem(
            TestKeyPassword,
            new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000));
        return (cert.ExportCertificatePem(), keyPem);
    }

    private async Task AuthenticateAsync()
    {
        var token = await _fixture.GetAccessTokenAsync();
        _client.SetBearerToken(token);
    }

    private async Task<Guid> CreateTestTenantAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/tenants", new
        {
            nip = TestNip.New(),
            displayName = $"Cert Test {Guid.NewGuid():N}"
        });
        response.EnsureSuccessStatusCode();
        var tenant = await response.Content.ReadFromJsonAsync<TenantDto>();
        return tenant!.Id;
    }

    // --- PEM upload tests ---

    [Fact]
    public async Task AddPemCertificate_WithEncryptedKey_Returns200()
    {
        await AuthenticateAsync();
        var tenantId = await CreateTestTenantAsync();

        var certPem = TestCertificate.CertPem;
        var keyPem = TestCertificate.KeyPem;

        var response = await _client.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/credentials",
            new
            {
                type = "Certificate",
                certificatePem = certPem,
                privateKeyPem = keyPem,
                certificatePassword = TestKeyPassword
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AddPemCertificate_CredentialStatus_ShowsCertificate()
    {
        await AuthenticateAsync();
        var tenantId = await CreateTestTenantAsync();

        var certPem = TestCertificate.CertPem;
        var keyPem = TestCertificate.KeyPem;

        var addResponse = await _client.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/credentials",
            new
            {
                type = "Certificate",
                certificatePem = certPem,
                privateKeyPem = keyPem,
                certificatePassword = TestKeyPassword
            });
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        var statusResponse = await _client.GetAsync(
            $"/api/tenants/{tenantId}/credentials/status");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);

        var status = await statusResponse.Content.ReadFromJsonAsync<CredentialStatusDto>();
        Assert.NotNull(status);
        Assert.True(status.Exists);
        Assert.Equal("Certificate", status.CredentialType);
    }

    [Fact]
    public async Task AddPfx_WithPassword_StatusShowsValidTo_NoSecretReturned()
    {
        await AuthenticateAsync();
        var tenantId = await CreateTestTenantAsync();
        var validTo = DateTimeOffset.UtcNow.AddDays(90);
        var pfx = CreatePfxBase64("Pfx-Test-1234", DateTimeOffset.UtcNow.AddDays(-1), validTo);

        var addResponse = await _client.PostAsJsonAsync($"/api/tenants/{tenantId}/credentials",
            new { type = "Certificate", certificateBase64 = pfx, certificatePassword = "Pfx-Test-1234" });
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        var body = await _client.GetStringAsync($"/api/tenants/{tenantId}/credentials/status");
        Assert.DoesNotContain(pfx[..32], body);
        Assert.DoesNotContain("Pfx-Test-1234", body);
        var status = System.Text.Json.JsonSerializer.Deserialize<CredentialStatusDto>(body)!;
        Assert.Equal("Certificate", status.CredentialType);
        Assert.Equal(validTo.UtcDateTime, status.CertificateValidTo!.Value.ToUniversalTime(), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task AddPfx_Expired_Returns400WithPolishMessage()
    {
        await AuthenticateAsync();
        var tenantId = await CreateTestTenantAsync();
        var pfx = CreatePfxBase64("Pfx-Test-1234", DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(-1));

        var response = await _client.PostAsJsonAsync($"/api/tenants/{tenantId}/credentials",
            new { type = "Certificate", certificateBase64 = pfx, certificatePassword = "Pfx-Test-1234" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OpenKSeF.Api.Controllers.CredentialsController.ExpiredCertificateMessage,
            (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("error").GetString());
    }

    private static string CreatePfxBase64(string password, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var key = RSA.Create(2048);
        using var cert = new CertificateRequest("CN=OpenKSeF Test, OID.2.5.4.97=VATPL-1111111111, C=PL", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(notBefore, notAfter);
        return Convert.ToBase64String(cert.Export(X509ContentType.Pfx, password));
    }

    [Fact]
    public async Task AddPemCertificate_ListCredentials_ShowsCertificateType()
    {
        await AuthenticateAsync();
        var tenantId = await CreateTestTenantAsync();

        var certPem = TestCertificate.CertPem;
        var keyPem = TestCertificate.KeyPem;

        var addResponse = await _client.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/credentials",
            new
            {
                type = "Certificate",
                certificatePem = certPem,
                privateKeyPem = keyPem,
                certificatePassword = TestKeyPassword
            });
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        var listResponse = await _client.GetAsync("/api/credentials");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var list = await listResponse.Content
            .ReadFromJsonAsync<List<TenantCredentialStatusDto>>();
        Assert.NotNull(list);

        var entry = list.FirstOrDefault(x => x.TenantId == tenantId);
        Assert.NotNull(entry);
        Assert.True(entry.HasCredential);
        Assert.Equal("Certificate", entry.CredentialType);
    }

    [Fact]
    public async Task AddPemCertificate_WrongPassword_Returns400()
    {
        await AuthenticateAsync();
        var tenantId = await CreateTestTenantAsync();

        var certPem = TestCertificate.CertPem;
        var keyPem = TestCertificate.KeyPem;

        var response = await _client.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/credentials",
            new
            {
                type = "Certificate",
                certificatePem = certPem,
                privateKeyPem = keyPem,
                certificatePassword = "wrong-password"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddPemCertificate_MissingPassword_Returns400()
    {
        await AuthenticateAsync();
        var tenantId = await CreateTestTenantAsync();

        var certPem = TestCertificate.CertPem;
        var keyPem = TestCertificate.KeyPem;

        var response = await _client.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/credentials",
            new
            {
                type = "Certificate",
                certificatePem = certPem,
                privateKeyPem = keyPem
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddPemCertificate_MissingKeyFile_Returns400()
    {
        await AuthenticateAsync();
        var tenantId = await CreateTestTenantAsync();

        var certPem = TestCertificate.CertPem;

        var response = await _client.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/credentials",
            new
            {
                type = "Certificate",
                certificatePem = certPem,
                certificatePassword = TestKeyPassword
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddPemCertificate_InvalidPem_Returns400()
    {
        await AuthenticateAsync();
        var tenantId = await CreateTestTenantAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/credentials",
            new
            {
                type = "Certificate",
                certificatePem = "not a real cert",
                privateKeyPem = "not a real key",
                certificatePassword = TestKeyPassword
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- Overwrite / update tests ---

    [Fact]
    public async Task AddPemCertificate_OverwritesExistingTokenCredential()
    {
        await AuthenticateAsync();
        var tenantId = await CreateTestTenantAsync();

        var tokenResponse = await _client.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/credentials",
            new { type = "Token", token = "fake-token-value" });
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);

        var statusBefore = await _client.GetAsync(
            $"/api/tenants/{tenantId}/credentials/status");
        var before = await statusBefore.Content.ReadFromJsonAsync<CredentialStatusDto>();
        Assert.Equal("Token", before!.CredentialType);

        var certPem = TestCertificate.CertPem;
        var keyPem = TestCertificate.KeyPem;
        var certResponse = await _client.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/credentials",
            new
            {
                type = "Certificate",
                certificatePem = certPem,
                privateKeyPem = keyPem,
                certificatePassword = TestKeyPassword
            });
        Assert.Equal(HttpStatusCode.OK, certResponse.StatusCode);

        var statusAfter = await _client.GetAsync(
            $"/api/tenants/{tenantId}/credentials/status");
        var after = await statusAfter.Content.ReadFromJsonAsync<CredentialStatusDto>();
        Assert.Equal("Certificate", after!.CredentialType);
    }

    [Fact]
    public async Task DeleteCertificateCredential_Returns204()
    {
        await AuthenticateAsync();
        var tenantId = await CreateTestTenantAsync();

        var certPem = TestCertificate.CertPem;
        var keyPem = TestCertificate.KeyPem;
        var addResponse = await _client.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/credentials",
            new
            {
                type = "Certificate",
                certificatePem = certPem,
                privateKeyPem = keyPem,
                certificatePassword = TestKeyPassword
            });
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        var deleteResponse = await _client.DeleteAsync(
            $"/api/tenants/{tenantId}/credentials");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var statusResponse = await _client.GetAsync(
            $"/api/tenants/{tenantId}/credentials/status");
        var status = await statusResponse.Content.ReadFromJsonAsync<CredentialStatusDto>();
        Assert.False(status!.Exists);
        Assert.Null(status.CredentialType);
    }

    // --- Auth guard ---

    [Fact]
    public async Task AddCredential_BodyOver64KB_Returns413_SavesNothing()
    {
        await AuthenticateAsync();
        var tenantId = await CreateTestTenantAsync();
        var errors = new ErrorLog();
        using var baseFactory = new OpenKSeFWebApplicationFactory(_fixture);
        using var kestrel = baseFactory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddSingleton<ILoggerFactory>(new LoggerFactory([errors]))));
        kestrel.UseKestrel(0);
        kestrel.StartServer();
        using var client = kestrel.CreateClient();
        client.DefaultRequestHeaders.Authorization = _client.DefaultRequestHeaders.Authorization;

        var response = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/credentials", new
        {
            type = "Certificate",
            certificateBase64 = new string('A', 64 * 1024),
            certificatePassword = "x"
        });

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorDto>();
        Assert.Equal("Plik jest za duży. Maksymalny rozmiar to 64 KB.", body!.Error);
        Assert.Empty(errors.Entries);
        var status = await _client.GetFromJsonAsync<CredentialStatusDto>($"/api/tenants/{tenantId}/credentials/status");
        Assert.False(status!.Exists);
    }

    [Fact]
    public async Task AddPemCertificate_WithoutAuth_Returns401()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/tenants/{Guid.NewGuid()}/credentials",
            new
            {
                type = "Certificate",
                certificatePem = "x",
                privateKeyPem = "y",
                certificatePassword = "z"
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private sealed record ErrorDto([property: JsonPropertyName("error")] string Error);

    private sealed class ErrorLog : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                Entries.Enqueue($"{formatter(state, exception)} {exception}");
        }
        public void Dispose() { }
    }

    private sealed record TenantDto(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("nip")] string Nip);

    private sealed record CredentialStatusDto(
        [property: JsonPropertyName("exists")] bool Exists,
        [property: JsonPropertyName("credentialType")] string? CredentialType,
        [property: JsonPropertyName("updatedAt")] DateTime? UpdatedAt,
        [property: JsonPropertyName("certificateValidTo")] DateTime? CertificateValidTo = null);

    private sealed record TenantCredentialStatusDto(
        [property: JsonPropertyName("tenantId")] Guid TenantId,
        [property: JsonPropertyName("tenantDisplayName")] string TenantDisplayName,
        [property: JsonPropertyName("hasCredential")] bool HasCredential,
        [property: JsonPropertyName("credentialType")] string? CredentialType,
        [property: JsonPropertyName("lastUpdatedAt")] DateTime? LastUpdatedAt);
}
