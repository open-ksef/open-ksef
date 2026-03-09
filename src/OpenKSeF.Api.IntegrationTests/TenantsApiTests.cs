using System.Net;
using System.Net.Http.Json;
using OpenKSeF.Api.IntegrationTests.Infrastructure;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class TenantsApiTests : IDisposable
{
    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TenantsApiTests(TestcontainersFixture fixture)
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

    [Fact]
    public async Task ListTenants_Empty_ReturnsEmptyArray()
    {
        await AuthenticateAsync();

        var response = await _client.GetAsync("/api/tenants");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tenants = await response.Content.ReadFromJsonAsync<List<TenantDto>>();
        Assert.NotNull(tenants);
    }

    [Fact]
    public async Task CreateTenant_ValidNip_Returns201()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync("/api/tenants", new
        {
            nip = "1111111111",
            displayName = "Integration Test Co",
            notificationEmail = "test@example.com"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var tenant = await response.Content.ReadFromJsonAsync<TenantDto>();
        Assert.NotNull(tenant);
        Assert.Equal("1111111111", tenant.Nip);
        Assert.Equal("Integration Test Co", tenant.DisplayName);
    }

    [Fact]
    public async Task CreateTenant_InvalidNip_Returns400()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync("/api/tenants", new
        {
            nip = "123",
            displayName = "Bad NIP Co"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetTenant_AfterCreate_ReturnsTenant()
    {
        await AuthenticateAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/tenants", new
        {
            nip = "5261040828",
            displayName = "Get Test Co",
            notificationEmail = "get@example.com"
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<TenantDto>();
        Assert.NotNull(created);

        var getResponse = await _client.GetAsync($"/api/tenants/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var fetched = await getResponse.Content.ReadFromJsonAsync<TenantDto>();
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal("Get Test Co", fetched.DisplayName);
    }

    [Fact]
    public async Task DeleteTenant_AfterCreate_Returns204()
    {
        await AuthenticateAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/tenants", new
        {
            nip = "7811904565",
            displayName = "Delete Test Co"
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<TenantDto>();
        Assert.NotNull(created);

        var deleteResponse = await _client.DeleteAsync($"/api/tenants/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await _client.GetAsync($"/api/tenants/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private sealed record TenantDto(
        Guid Id,
        string Nip,
        string? DisplayName,
        string? NotificationEmail,
        DateTime CreatedAt);
}
