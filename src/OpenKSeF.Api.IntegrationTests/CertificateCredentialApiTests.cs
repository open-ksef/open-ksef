using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using OpenKSeF.Api.IntegrationTests.Infrastructure;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class CertificateCredentialApiTests : IDisposable
{
    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _client;

    private static readonly string TestCertPath =
        Path.Combine(AppContext.BaseDirectory, "TestData", "TestOpenKSeF.crt");
    private static readonly string TestKeyPath =
        Path.Combine(AppContext.BaseDirectory, "TestData", "TestOpenKSeF.key");
    private const string TestKeyPassword = "test!@#123TESTaaaa";
    private const string TestNip = "2684494832";

    public CertificateCredentialApiTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
        _factory = new OpenKSeFWebApplicationFactory(fixture);
        _client = _factory.CreateClient();
    }

    private async Task AuthenticateAsync()
    {
        var token = await _fixture.GetAccessTokenAsync();
        _client.SetBearerToken(token);
    }

    private async Task<Guid> CreateTestTenantAsync(string nip = TestNip)
    {
        var response = await _client.PostAsJsonAsync("/api/tenants", new
        {
            nip,
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

        var certPem = await File.ReadAllTextAsync(TestCertPath);
        var keyPem = await File.ReadAllTextAsync(TestKeyPath);

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
        var tenantId = await CreateTestTenantAsync("3542187690");

        var certPem = await File.ReadAllTextAsync(TestCertPath);
        var keyPem = await File.ReadAllTextAsync(TestKeyPath);

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
    public async Task AddPemCertificate_ListCredentials_ShowsCertificateType()
    {
        await AuthenticateAsync();
        var tenantId = await CreateTestTenantAsync("4618793205");

        var certPem = await File.ReadAllTextAsync(TestCertPath);
        var keyPem = await File.ReadAllTextAsync(TestKeyPath);

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
        var tenantId = await CreateTestTenantAsync("1234563218");

        var certPem = await File.ReadAllTextAsync(TestCertPath);
        var keyPem = await File.ReadAllTextAsync(TestKeyPath);

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
        var tenantId = await CreateTestTenantAsync("5252344078");

        var certPem = await File.ReadAllTextAsync(TestCertPath);
        var keyPem = await File.ReadAllTextAsync(TestKeyPath);

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
        var tenantId = await CreateTestTenantAsync("8943217650");

        var certPem = await File.ReadAllTextAsync(TestCertPath);

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
        var tenantId = await CreateTestTenantAsync("1928374650");

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
        var tenantId = await CreateTestTenantAsync("5270103391");

        var tokenResponse = await _client.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/credentials",
            new { type = "Token", token = "fake-token-value" });
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);

        var statusBefore = await _client.GetAsync(
            $"/api/tenants/{tenantId}/credentials/status");
        var before = await statusBefore.Content.ReadFromJsonAsync<CredentialStatusDto>();
        Assert.Equal("Token", before!.CredentialType);

        var certPem = await File.ReadAllTextAsync(TestCertPath);
        var keyPem = await File.ReadAllTextAsync(TestKeyPath);
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
        var tenantId = await CreateTestTenantAsync("9442190348");

        var certPem = await File.ReadAllTextAsync(TestCertPath);
        var keyPem = await File.ReadAllTextAsync(TestKeyPath);
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

    private sealed record TenantDto(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("nip")] string Nip);

    private sealed record CredentialStatusDto(
        [property: JsonPropertyName("exists")] bool Exists,
        [property: JsonPropertyName("credentialType")] string? CredentialType,
        [property: JsonPropertyName("updatedAt")] DateTime? UpdatedAt);

    private sealed record TenantCredentialStatusDto(
        [property: JsonPropertyName("tenantId")] Guid TenantId,
        [property: JsonPropertyName("tenantDisplayName")] string TenantDisplayName,
        [property: JsonPropertyName("hasCredential")] bool HasCredential,
        [property: JsonPropertyName("credentialType")] string? CredentialType,
        [property: JsonPropertyName("lastUpdatedAt")] DateTime? LastUpdatedAt);
}
