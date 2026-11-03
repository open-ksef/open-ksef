using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Models;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class InvoiceListFiltersApiTests : IDisposable
{
    private const string TenantRoute = "/api/tenants/{0}/invoices?";
    private const string SummaryRoute = "/api/invoices?tenantId={0}&";

    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public InvoiceListFiltersApiTests(TestcontainersFixture fixture)
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
        var response = await _client.PostAsJsonAsync("/api/tenants", new { nip, displayName = "Invoice list test" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TenantIdDto>())!.Id;
    }

    private async Task SeedAsync(params InvoiceHeader[] invoices)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.InvoiceHeaders.AddRange(invoices);
        await db.SaveChangesAsync();
    }

    private static InvoiceHeader Invoice(Guid tenantId, string number, DateTime issueDate, string vendor = "Vendor", string nip = "1112223344") =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            KSeFInvoiceNumber = number,
            KSeFReferenceNumber = number,
            VendorName = vendor,
            VendorNip = nip,
            IssueDate = issueDate,
            FirstSeenAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow
        };

    private static DateTime Utc(int month, int day, int hour = 0, int minute = 0) =>
        new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    private async Task<PagedResult<InvoiceResponse>> ListAsync(string route, Guid tenantId, string query)
    {
        var response = await _client.GetAsync(string.Format(route, tenantId) + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<InvoiceResponse>>())!;
    }

    [Theory]
    [InlineData(TenantRoute)]
    [InlineData(SummaryRoute)]
    public async Task DateOnlyFilters_IncludeBothBoundaryDays(string route)
    {
        var tenantId = await CreateTenantAsync();
        await SeedAsync(
            Invoice(tenantId, "DAY-BEFORE", Utc(1, 9)),
            Invoice(tenantId, "FIRST-DAY", Utc(1, 10)),
            Invoice(tenantId, "LAST-DAY", Utc(1, 31)),
            Invoice(tenantId, "LAST-DAY-EVENING", Utc(1, 31, 22, 30)),
            Invoice(tenantId, "DAY-AFTER", Utc(2, 1)));

        var result = await ListAsync(route, tenantId, "dateFrom=2026-01-10&dateTo=2026-01-31");

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(
            new[] { "FIRST-DAY", "LAST-DAY", "LAST-DAY-EVENING" },
            result.Items.Select(i => i.KSeFInvoiceNumber).Order().ToArray());
    }

    [Theory]
    [InlineData(TenantRoute)]
    [InlineData(SummaryRoute)]
    public async Task DateFilters_AtCalendarLimits_Return200(string route)
    {
        var tenantId = await CreateTenantAsync();
        await SeedAsync(Invoice(tenantId, "ANY", Utc(1, 10)));

        var result = await ListAsync(route, tenantId, "dateFrom=0001-01-01&dateTo=9999-12-31");

        Assert.Equal(1, result.TotalCount);
    }

    [Theory]
    [InlineData(TenantRoute)]
    [InlineData(SummaryRoute)]
    public async Task Paging_WithEqualIssueDates_FollowsIdOrder(string route)
    {
        var tenantId = await CreateTenantAsync();
        var invoices = Enumerable.Range(1, 7).Select(n => Invoice(tenantId, $"SAME-{n}", Utc(1, 10))).ToArray();
        await SeedAsync(invoices);

        var paged = new List<Guid>();
        for (var page = 1; page <= 3; page++)
            paged.AddRange((await ListAsync(route, tenantId, $"page={page}&pageSize=3")).Items.Select(i => i.Id));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var expected = await db.InvoiceHeaders
            .Where(i => i.TenantId == tenantId)
            .OrderBy(i => i.Id)
            .Select(i => i.Id)
            .ToListAsync();
        Assert.Equal(expected, paged);
    }

    [Theory]
    [InlineData("abc", "ABC")]
    [InlineData("  aBc  ", "ABC")]
    [InlineData("łódź", "LODZ")]
    [InlineData("ŁÓDŹ", "LODZ")]
    [InlineData("456789", "NIP")]
    [InlineData("%", null)]
    [InlineData("nothing-matches", null)]
    public async Task FilterByVendor_IgnoresCaseAndSurroundingSpaces(string term, string? expectedNumber)
    {
        var tenantId = await CreateTenantAsync();
        await SeedAsync(
            Invoice(tenantId, "ABC", Utc(1, 10), vendor: "ABC Sp. z o.o."),
            Invoice(tenantId, "LODZ", Utc(1, 10), vendor: "ŁÓDŹ Transport"),
            Invoice(tenantId, "NIP", Utc(1, 10), nip: "1234567890"));

        var result = await ListAsync(TenantRoute, tenantId, $"filterByVendor={Uri.EscapeDataString(term)}");

        Assert.Equal(
            expectedNumber is null ? Array.Empty<string>() : new[] { expectedNumber },
            result.Items.Select(i => i.KSeFInvoiceNumber).ToArray());
    }

    private sealed record TenantIdDto(Guid Id);
}
