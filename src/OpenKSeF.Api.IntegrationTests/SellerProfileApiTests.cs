using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class SellerProfileApiTests : IDisposable
{
    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SellerProfileApiTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
        _factory = new OpenKSeFWebApplicationFactory(fixture);
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<Guid> CreateTenantAsync()
    {
        _client.SetBearerToken(await _fixture.GetAccessTokenAsync());
        var nip = Random.Shared.NextInt64(1_000_000_000, 9_999_999_999).ToString();
        var response = await _client.PostAsJsonAsync("/api/tenants", new { nip, displayName = "Seller profile test" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TenantIdDto>())!.Id;
    }

    private static SellerProfileRequest Request(
        string? bankAccount = "61 1090 1014 0000 0712 1981 2874",
        string? pattern = "FV/{nr}/{MM}/{RRRR}",
        int? nextNumber = null,
        string? nextNumberPeriod = null) =>
        new("OpenKSeF Test A sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", bankAccount, false, pattern, nextNumber, nextNumberPeriod);

    private async Task<ValidationEnvelope> AssertBadRequestAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ValidationEnvelope>())!;
    }

    [Fact]
    public async Task Get_WithoutProfile_ReturnsDefaultsIncomplete()
    {
        var tenantId = await CreateTenantAsync();

        var profile = await _client.GetFromJsonAsync<SellerProfileResponse>($"/api/tenants/{tenantId}/seller-profile");

        Assert.NotNull(profile);
        Assert.False(profile.IsComplete);
        Assert.Null(profile.LegalName);
        Assert.Equal("FV/{nr}/{MM}/{RRRR}", profile.NumberingPattern);
        Assert.Equal(1, profile.NextNumber);
        Assert.Matches(@"^\d{4}-\d{2}$", profile.NextNumberPeriod);
        Assert.Matches(@"^FV/1/\d{2}/\d{4}$", profile.NextNumberPreview);
        Assert.Equal(10, profile.Nip.Length);
        Assert.Equal("test", profile.KsefEnvironment);
    }

    [Fact]
    public async Task Put_StoresNrbDigitsOnly_AndCompletesProfile()
    {
        var tenantId = await CreateTenantAsync();

        var response = await _client.PutAsJsonAsync($"/api/tenants/{tenantId}/seller-profile", Request());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = (await response.Content.ReadFromJsonAsync<SellerProfileResponse>())!;
        Assert.Equal("61109010140000071219812874", profile.BankAccount);
        Assert.True(profile.IsComplete);

        var reloaded = await _client.GetFromJsonAsync<SellerProfileResponse>($"/api/tenants/{tenantId}/seller-profile");
        Assert.Equal("61109010140000071219812874", reloaded!.BankAccount);
        Assert.Equal("OpenKSeF Test A sp. z o.o.", reloaded.LegalName);
    }

    [Fact]
    public async Task Put_WrongNrbCheckDigit_Returns400()
    {
        var tenantId = await CreateTenantAsync();

        var envelope = await AssertBadRequestAsync(await _client.PutAsJsonAsync(
            $"/api/tenants/{tenantId}/seller-profile", Request(bankAccount: "61 1090 1014 0000 0712 1981 2875")));

        var error = Assert.Single(envelope.Errors);
        Assert.Equal(("bankAccount", "Niepoprawny numer rachunku"), (error.Field, error.Message));
    }

    [Theory]
    [InlineData("FV/{MM}/{RRRR}")]
    [InlineData("")]
    public async Task Put_PatternWithoutNumberToken_Returns400(string pattern)
    {
        var tenantId = await CreateTenantAsync();

        var envelope = await AssertBadRequestAsync(await _client.PutAsJsonAsync(
            $"/api/tenants/{tenantId}/seller-profile", Request(pattern: pattern)));

        Assert.Contains(envelope.Errors, e => e.Field == "numberingPattern");
    }

    [Theory]
    [InlineData("FV/{nr}/{MM}/{RRRR}", "2026", "nextNumberPeriod")]
    [InlineData("{nr}/{RRRR}", "2026-11", "nextNumberPeriod")]
    public async Task Put_PeriodNotMatchingPattern_Returns400(string pattern, string period, string field)
    {
        var tenantId = await CreateTenantAsync();

        var envelope = await AssertBadRequestAsync(await _client.PutAsJsonAsync(
            $"/api/tenants/{tenantId}/seller-profile", Request(pattern: pattern, nextNumber: 5, nextNumberPeriod: period)));

        Assert.Contains(envelope.Errors, e => e.Field == field);
    }

    [Fact]
    public async Task Put_NextNumberZero_Returns400()
    {
        var tenantId = await CreateTenantAsync();

        var envelope = await AssertBadRequestAsync(await _client.PutAsJsonAsync(
            $"/api/tenants/{tenantId}/seller-profile", Request(nextNumber: 0, nextNumberPeriod: "2026-11")));

        Assert.Contains(envelope.Errors, e => e.Field == "nextNumber");
    }

    [Theory]
    [InlineData("FV/{nr}/{MM}/{RRRR}", 57, "2026-11")]
    [InlineData("{nr}/{RRRR}", 120, "2026")]
    public async Task Put_NextNumber_IsUsedByNextAllocationForThatPeriod(string pattern, int next, string period)
    {
        var tenantId = await CreateTenantAsync();

        var response = await _client.PutAsJsonAsync(
            $"/api/tenants/{tenantId}/seller-profile", Request(pattern: pattern, nextNumber: next, nextNumberPeriod: period));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISellerProfileService>();
        Assert.Equal(next, await service.AllocateNumberAsync(tenantId, period));
        Assert.Equal(next + 1, await service.AllocateNumberAsync(tenantId, period));
        Assert.Equal(1, await service.AllocateNumberAsync(tenantId, "1999-01"));
    }

    [Fact]
    public async Task CrossTenant_GetAndPut_Return404()
    {
        await CreateTenantAsync();
        var otherTenantId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Tenants.Add(new Tenant { Id = otherTenantId, UserId = "someone-else", Nip = "5261040828", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/tenants/{otherTenantId}/seller-profile")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PutAsJsonAsync($"/api/tenants/{otherTenantId}/seller-profile", Request())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/tenants/{Guid.NewGuid()}/seller-profile")).StatusCode);

        using var verify = _factory.Services.CreateScope();
        Assert.False(await verify.ServiceProvider.GetRequiredService<ApplicationDbContext>().SellerProfiles.AnyAsync(p => p.TenantId == otherTenantId));
    }

    private sealed record TenantIdDto(Guid Id);
}
