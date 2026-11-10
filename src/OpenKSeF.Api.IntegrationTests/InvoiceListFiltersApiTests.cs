using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Invoicing;
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
        var nip = TestNip.New();
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

    private static InvoiceHeader Due(Guid tenantId, string number, DateOnly? dueDate, DateTime? issueDate = null, bool isPaid = false)
    {
        var invoice = Invoice(tenantId, number, issueDate ?? Utc(1, 10));
        invoice.DueDate = dueDate;
        invoice.IsPaid = isPaid;
        return invoice;
    }

    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static string[] Numbers(PagedResult<InvoiceResponse> result) =>
        result.Items.Select(i => i.KSeFInvoiceNumber).ToArray();

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

    [Fact]
    public async Task Unpaid_ReturnsOnlyUnpaid_EarliestDueFirst_UndatedLast()
    {
        var tenantId = await CreateTenantAsync();
        await SeedAsync(
            Due(tenantId, "UNDATED", null, Utc(3, 1)),
            Due(tenantId, "LATE", D(5, 20)),
            Due(tenantId, "EARLY", D(1, 15)),
            Due(tenantId, "PAID-EARLIEST", D(1, 1), isPaid: true));

        var result = await ListAsync(TenantRoute, tenantId, "isPaid=false");

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(["EARLY", "LATE", "UNDATED"], Numbers(result));
    }

    [Fact]
    public async Task Paid_FollowsIssueDateOrder()
    {
        var tenantId = await CreateTenantAsync();
        await SeedAsync(
            Due(tenantId, "OLD", D(1, 1), Utc(1, 5), isPaid: true),
            Due(tenantId, "NEW", D(9, 1), Utc(2, 5), isPaid: true),
            Due(tenantId, "UNPAID", D(1, 1), Utc(3, 5)));

        var result = await ListAsync(TenantRoute, tenantId, "isPaid=true");

        Assert.Equal(["NEW", "OLD"], Numbers(result));
    }

    [Fact]
    public async Task NoFilter_StaysNewestIssueDateFirst_IgnoringDueDate()
    {
        var tenantId = await CreateTenantAsync();
        await SeedAsync(
            Due(tenantId, "OLD-DUE-SOON", D(1, 1), Utc(1, 5)),
            Due(tenantId, "NEW-UNDATED", null, Utc(3, 5)),
            Due(tenantId, "MID-PAID", D(6, 1), Utc(2, 5), isPaid: true));

        var result = await ListAsync(TenantRoute, tenantId, "");

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(["NEW-UNDATED", "MID-PAID", "OLD-DUE-SOON"], Numbers(result));
    }

    [Fact]
    public async Task Unpaid_Paging_WithEqualDatesAndIssueDates_RepeatsAndMissesNone()
    {
        var tenantId = await CreateTenantAsync();
        await SeedAsync(
        [
            .. Enumerable.Range(1, 5).Select(n => Due(tenantId, $"SAME-{n}", D(4, 1))),
            .. Enumerable.Range(1, 4).Select(n => Due(tenantId, $"NONE-{n}", null)),
            Due(tenantId, "FIRST", D(2, 1)),
            Due(tenantId, "SKIP-PAID", D(2, 1), isPaid: true),
        ]);

        var numbers = new List<string>();
        for (var page = 1; page <= 4; page++)
        {
            var result = await ListAsync(TenantRoute, tenantId, $"isPaid=false&page={page}&pageSize=3");
            Assert.Equal(10, result.TotalCount);
            numbers.AddRange(Numbers(result));
        }

        Assert.Equal(10, numbers.Distinct().Count());
        Assert.Equal("FIRST", numbers[0]);
        Assert.All(numbers[1..6], n => Assert.StartsWith("SAME-", n));
        Assert.All(numbers[6..], n => Assert.StartsWith("NONE-", n));
        Assert.DoesNotContain("SKIP-PAID", numbers);
    }

    [Fact]
    public async Task Unpaid_SameDueDate_NewestIssueFirst()
    {
        var tenantId = await CreateTenantAsync();
        await SeedAsync(Due(tenantId, "OLDER", D(4, 1), Utc(1, 5)), Due(tenantId, "NEWER", D(4, 1), Utc(2, 5)));

        Assert.Equal(["NEWER", "OLDER"], Numbers(await ListAsync(TenantRoute, tenantId, "isPaid=false")));
    }

    [Theory]
    [InlineData(TenantRoute)]
    [InlineData(SummaryRoute)]
    public async Task Response_CarriesDueDateAsPlainDate_AndOverdueByWarsawToday(string route)
    {
        var tenantId = await CreateTenantAsync();
        var today = IssuedInvoice.PolandDate(DateTimeOffset.UtcNow);
        await SeedAsync(
            Due(tenantId, "YESTERDAY", today.AddDays(-1)),
            Due(tenantId, "TODAY", today),
            Due(tenantId, "TOMORROW", today.AddDays(1)),
            Due(tenantId, "PAID-YESTERDAY", today.AddDays(-1), isPaid: true),
            Due(tenantId, "UNDATED", null));

        var response = await _client.GetAsync(string.Format(route, tenantId) + "pageSize=10");
        var items = (await response.Content.ReadFromJsonAsync<JsonObject>())!["items"]!.AsArray().Select(i => i!.AsObject())
            .ToDictionary(i => i["ksefInvoiceNumber"]!.GetValue<string>());

        Assert.Equal(today.AddDays(-1).ToString("yyyy-MM-dd"), items["YESTERDAY"]["dueDate"]!.GetValue<string>());
        Assert.Null(items["UNDATED"]["dueDate"]);
        Assert.True(items["UNDATED"].ContainsKey("dueDate"));
        Assert.True(items["YESTERDAY"]["isOverdue"]!.GetValue<bool>());
        Assert.False(items["TODAY"]["isOverdue"]!.GetValue<bool>());
        Assert.False(items["TOMORROW"]["isOverdue"]!.GetValue<bool>());
        Assert.False(items["PAID-YESTERDAY"]["isOverdue"]!.GetValue<bool>());
        Assert.False(items["UNDATED"]["isOverdue"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Details_CarryDueDate_AndMarkingPaidEndsOverdue()
    {
        var tenantId = await CreateTenantAsync();
        var yesterday = IssuedInvoice.PolandDate(DateTimeOffset.UtcNow).AddDays(-1);
        var invoice = Due(tenantId, "DETAIL", yesterday);
        await SeedAsync(invoice);

        var details = await _client.GetFromJsonAsync<InvoiceResponse>($"/api/tenants/{tenantId}/invoices/by-number/DETAIL");
        Assert.Equal(yesterday, details!.DueDate);
        Assert.True(details.IsOverdue);

        var patch = await _client.PatchAsJsonAsync($"/api/tenants/{tenantId}/invoices/{invoice.Id}/paid", new { isPaid = true });
        var paid = await patch.Content.ReadFromJsonAsync<InvoiceResponse>();
        Assert.Equal(yesterday, paid!.DueDate);
        Assert.False(paid.IsOverdue);
    }

    private sealed record TenantIdDto(Guid Id);
}
