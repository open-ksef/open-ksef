using System.Net;
using System.Net.Http.Json;
using System.Text;
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

    [Theory]
    [InlineData("6100000011", false)]
    [InlineData("6100000012", true)]
    public async Task DeleteTenant_WithDraftOrIssuedInvoice_Returns409_AndKeepsTenant(string nip, bool issue)
    {
        await AuthenticateAsync();
        var created = await (await _client.PostAsJsonAsync("/api/tenants", new { nip, displayName = "Delete Blocked Co" }))
            .Content.ReadFromJsonAsync<TenantDto>();
        (await _client.PutAsJsonAsync($"/api/tenants/{created!.Id}/seller-profile", new
        {
            legalName = "Delete Blocked Co",
            addressLine1 = "ul. Prosta 1",
            bankAccount = "61109010140000071219812874",
            numberingPattern = "FV/{nr}/{MM}/{RRRR}"
        })).EnsureSuccessStatusCode();
        var draftResponse = await _client.PostAsJsonAsync($"/api/tenants/{created.Id}/issued-invoices", new
        {
            buyer = new { kind = "Company", nip = "5261040828", name = "Nabywca", addressLine1 = "ul. Krótka 2" },
            lines = new[] { new { name = "Usługa", unit = "szt.", quantity = 1, unitNetPrice = 100.00m, vatRate = "23" } }
        });
        draftResponse.EnsureSuccessStatusCode();
        var draft = await draftResponse.Content.ReadFromJsonAsync<IdDto>();
        if (issue)
            Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync($"/api/tenants/{created.Id}/issued-invoices/{draft!.Id}/issue", null)).StatusCode);

        var blocked = await _client.DeleteAsync($"/api/tenants/{created.Id}");

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("Nie można usunąć firmy", (await blocked.Content.ReadFromJsonAsync<ConflictDto>())!.Message);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/tenants/{created.Id}")).StatusCode);

        if (!issue)
        {
            (await _client.DeleteAsync($"/api/tenants/{created.Id}/issued-invoices/{draft!.Id}")).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/tenants/{created.Id}")).StatusCode);
        }
    }

    [Theory]
    [InlineData("6100000001", """{"displayName":"Renamed"}""", "keep@example.com")]
    [InlineData("6100000002", """{"displayName":"Renamed","notificationEmail":null}""", null)]
    [InlineData("6100000003", """{"displayName":"Renamed","notificationEmail":""}""", null)]
    [InlineData("6100000004", """{"displayName":"Renamed","notificationEmail":"new@example.com"}""", "new@example.com")]
    public async Task UpdateTenant_NotificationEmail_AbsentKeepsOtherwiseApplies(string nip, string body, string? expectedEmail)
    {
        await AuthenticateAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/tenants", new
        {
            nip,
            displayName = "Update Test Co",
            notificationEmail = "keep@example.com"
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<TenantDto>();
        Assert.NotNull(created);

        var updateResponse = await _client.PutAsync(
            $"/api/tenants/{created.Id}",
            new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var fetched = await _client.GetFromJsonAsync<TenantDto>($"/api/tenants/{created.Id}");
        Assert.NotNull(fetched);
        Assert.Equal("Renamed", fetched.DisplayName);
        Assert.Equal(expectedEmail, fetched.NotificationEmail);
    }

    [Fact]
    public async Task UpdateTenant_InvalidNotificationEmail_Returns400AndKeepsValue()
    {
        await AuthenticateAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/tenants", new
        {
            nip = "6100000005",
            displayName = "Invalid Email Co",
            notificationEmail = "keep@example.com"
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<TenantDto>();
        Assert.NotNull(created);

        var updateResponse = await _client.PutAsJsonAsync($"/api/tenants/{created.Id}", new
        {
            displayName = "Renamed",
            notificationEmail = "not-an-email"
        });
        Assert.Equal(HttpStatusCode.BadRequest, updateResponse.StatusCode);

        var fetched = await _client.GetFromJsonAsync<TenantDto>($"/api/tenants/{created.Id}");
        Assert.NotNull(fetched);
        Assert.Equal("keep@example.com", fetched.NotificationEmail);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private sealed record ConflictDto(string Message);

    private sealed record IdDto(Guid Id);

    private sealed record TenantDto(
        Guid Id,
        string Nip,
        string? DisplayName,
        string? NotificationEmail,
        DateTime CreatedAt);
}
