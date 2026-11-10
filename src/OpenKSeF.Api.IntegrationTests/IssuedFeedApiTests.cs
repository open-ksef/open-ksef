using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Domain.Models;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class IssuedFeedApiTests : IDisposable
{
    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public IssuedFeedApiTests(TestcontainersFixture fixture)
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
        var response = await _client.PostAsJsonAsync("/api/tenants", new { nip, displayName = "Issued feed test" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TenantIdDto>())!.Id;
    }

    private async Task WithDbAsync(Func<ApplicationDbContext, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private static DateTime Utc(int month, int day) => new(2026, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static InvoiceHeader Synced(
        Guid tenantId,
        string number,
        DateTime issueDate,
        InvoiceDirection direction = InvoiceDirection.Issued,
        DateTime? seenAt = null,
        string currency = "PLN",
        string? buyerName = "Nabywca",
        string? buyerForeignId = null) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            KSeFInvoiceNumber = number,
            KSeFReferenceNumber = number,
            InvoiceNumber = $"FV {number}",
            VendorName = "Sprzedawca",
            VendorNip = "1112223344",
            BuyerName = buyerName,
            BuyerForeignId = buyerForeignId,
            BuyerIdType = buyerForeignId is null ? "Nip" : "VatUe",
            AmountNet = 100,
            AmountVat = 23,
            AmountGross = 123,
            Currency = currency,
            IssueDate = issueDate,
            Direction = direction,
            FirstSeenAt = seenAt ?? DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow
        };

    private static IssuedInvoice Own(Guid tenantId, DateOnly issueDate, DateTime createdAt) =>
        IssuedInvoice.Draft(Guid.NewGuid(), tenantId, issueDate, createdAt);

    private static async Task SaveAcceptedAsync(ApplicationDbContext db, IssuedInvoice invoice, string ksefNumber)
    {
        db.IssuedInvoices.Add(invoice);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "IssuedInvoices" SET "Status" = 'Accepted', "KsefNumber" = {ksefNumber} WHERE "Id" = {invoice.Id}""");
    }

    private async Task<PagedResult<IssuedFeedItemResponse>> FeedAsync(Guid tenantId, string query = "")
    {
        var response = await _client.GetAsync($"/api/tenants/{tenantId}/issued-invoices/feed?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<IssuedFeedItemResponse>>())!;
    }

    private async Task<PagedResult<InvoiceResponse>> ListAsync(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<InvoiceResponse>>())!;
    }

    private async Task<JsonNode> FeedJsonAsync(Guid tenantId)
    {
        var response = await _client.GetAsync($"/api/tenants/{tenantId}/issued-invoices/feed");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }

    [Fact]
    public async Task Feed_IssuedFetchIncomplete_FollowsTheSyncState_AndKeepsThePageShape()
    {
        var tenantId = await CreateTenantAsync();
        Assert.False((await FeedJsonAsync(tenantId))["issuedFetchIncomplete"]!.GetValue<bool>());

        var lastRun = DateTime.UtcNow;
        var stateId = Guid.NewGuid();
        await WithDbAsync(async db =>
        {
            db.SyncStates.Add(new SyncState { Id = stateId, TenantId = tenantId, LastSyncedAt = lastRun, LastSuccessfulSync = lastRun });
            await db.SaveChangesAsync();
        });

        var incomplete = await FeedJsonAsync(tenantId);
        Assert.True(incomplete["issuedFetchIncomplete"]!.GetValue<bool>());
        Assert.Equal(0, incomplete["totalCount"]!.GetValue<int>());
        Assert.Empty(incomplete["items"]!.AsArray());

        await WithDbAsync(async db =>
        {
            (await db.SyncStates.SingleAsync(s => s.Id == stateId)).IssuedContinuationAt = lastRun.AddMinutes(-2);
            await db.SaveChangesAsync();
        });

        Assert.False((await FeedJsonAsync(tenantId))["issuedFetchIncomplete"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Feed_PagesAcrossBothTables_WithoutGapsOrDuplicates_EvenOnTiedTimestamps()
    {
        var tenantId = await CreateTenantAsync();
        var sameBatch = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        await WithDbAsync(async db =>
        {
            for (var n = 0; n < 7; n++)
                db.InvoiceHeaders.Add(Synced(tenantId, $"TIED-{n}", Utc(10, 5), seenAt: sameBatch));
            db.InvoiceHeaders.Add(Synced(tenantId, "NEWEST", Utc(10, 20)));
            db.InvoiceHeaders.Add(Synced(tenantId, "OLDEST", Utc(9, 1)));
            db.IssuedInvoices.Add(Own(tenantId, new DateOnly(2026, 10, 10), DateTime.UtcNow));
            db.IssuedInvoices.Add(Own(tenantId, new DateOnly(2026, 10, 5), sameBatch));
            db.IssuedInvoices.Add(Own(tenantId, new DateOnly(2026, 8, 1), DateTime.UtcNow));
            await db.SaveChangesAsync();
        });

        var seen = new List<IssuedFeedItemResponse>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await FeedAsync(tenantId, $"page={page}&pageSize=5");
            Assert.Equal(12, result.TotalCount);
            seen.AddRange(result.Items);
        }

        Assert.Equal(12, seen.Count);
        Assert.Equal(12, seen.Select(i => i.Id).Distinct().Count());
        Assert.Equal(seen.OrderByDescending(i => i.IssueDate).Select(i => i.Id), seen.Select(i => i.Id));
        Assert.Equal("NEWEST", seen[0].KsefNumber);
        Assert.Equal("OpenKSeF", seen[^1].Source);
        Assert.Empty((await FeedAsync(tenantId, "page=4&pageSize=5")).Items);
    }

    [Fact]
    public async Task Feed_DedupsByKsefNumber_OwnRowWins_InItemsAndCount()
    {
        var tenantId = await CreateTenantAsync();
        await WithDbAsync(async db =>
        {
            db.InvoiceHeaders.Add(Synced(tenantId, "KSEF-BOTH", Utc(10, 5)));
            db.InvoiceHeaders.Add(Synced(tenantId, "KSEF-OTHER-TOOL", Utc(10, 6)));
            await SaveAcceptedAsync(db, Own(tenantId, new DateOnly(2026, 10, 5), DateTime.UtcNow), "KSEF-BOTH");
        });

        var result = await FeedAsync(tenantId);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("OpenKSeF", result.Items.Single(i => i.KsefNumber == "KSEF-BOTH").Source);
        Assert.Equal("External", result.Items.Single(i => i.KsefNumber == "KSEF-OTHER-TOOL").Source);
    }

    [Fact]
    public async Task Feed_ExternalRowCarriesOwnCurrencyAndBuyerId()
    {
        var tenantId = await CreateTenantAsync();
        await WithDbAsync(async db =>
        {
            db.InvoiceHeaders.Add(Synced(tenantId, "KSEF-GBP", Utc(10, 5), currency: "GBP", buyerForeignId: "DE123456789"));
            await db.SaveChangesAsync();
        });

        var item = (await FeedAsync(tenantId)).Items.Single();

        Assert.Equal("External", item.Source);
        Assert.Equal("Accepted", item.Status);
        Assert.Equal("GBP", item.Currency);
        Assert.Equal("VatUe", item.BuyerIdType);
        Assert.Equal("DE123456789", item.BuyerForeignId);
        Assert.Null(item.BuyerNip);
        Assert.Null(item.DueDate);
        Assert.Equal(new DateOnly(2026, 10, 5), item.IssueDate);
        Assert.False(item.NeedsAttention);
    }

    [Fact]
    public async Task Feed_StatusMonthAndQueryFilters_ApplyToBothSources()
    {
        var tenantId = await CreateTenantAsync();
        await WithDbAsync(async db =>
        {
            db.InvoiceHeaders.Add(Synced(tenantId, "KSEF-OCT", Utc(10, 5), buyerName: "Acme"));
            db.InvoiceHeaders.Add(Synced(tenantId, "KSEF-NOV", Utc(11, 5), buyerName: "Other"));
            db.IssuedInvoices.Add(Own(tenantId, new DateOnly(2026, 10, 7), DateTime.UtcNow));
            await SaveAcceptedAsync(db, Own(tenantId, new DateOnly(2026, 10, 8), DateTime.UtcNow), "KSEF-OWN");
        });

        Assert.Equal(4, (await FeedAsync(tenantId)).TotalCount);

        var drafts = await FeedAsync(tenantId, "status=Draft");
        Assert.Equal("Draft", drafts.Items.Single().Status);
        Assert.Equal(1, drafts.TotalCount);

        var accepted = await FeedAsync(tenantId, "status=Accepted");
        Assert.Equal(3, accepted.TotalCount);
        Assert.All(accepted.Items, i => Assert.Equal("Accepted", i.Status));

        Assert.Empty((await FeedAsync(tenantId, "status=Rejected")).Items);

        var october = await FeedAsync(tenantId, "month=2026-10");
        Assert.Equal(3, october.TotalCount);
        Assert.DoesNotContain(october.Items, i => i.KsefNumber == "KSEF-NOV");

        var search = await FeedAsync(tenantId, "q=acme");
        Assert.Equal("KSEF-OCT", search.Items.Single().KsefNumber);

        var bad = await _client.GetAsync($"/api/tenants/{tenantId}/issued-invoices/feed?status=Nope");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var badMonth = await _client.GetAsync($"/api/tenants/{tenantId}/issued-invoices/feed?month=oct");
        Assert.Equal(HttpStatusCode.BadRequest, badMonth.StatusCode);
    }

    [Fact]
    public async Task IssuedInvoicesList_StaysOwnRowsOnly()
    {
        var tenantId = await CreateTenantAsync();
        await WithDbAsync(async db =>
        {
            db.InvoiceHeaders.Add(Synced(tenantId, "KSEF-EXT", Utc(10, 5)));
            db.IssuedInvoices.Add(Own(tenantId, new DateOnly(2026, 10, 7), DateTime.UtcNow));
            await db.SaveChangesAsync();
        });

        var response = await _client.GetAsync($"/api/tenants/{tenantId}/issued-invoices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<PagedResult<IssuedInvoiceListItemResponse>>())!;
        Assert.Equal(1, result.TotalCount);
        Assert.Null(result.Items.Single().KsefNumber);
    }

    [Fact]
    public async Task Feed_RequiresAuth_UnknownTenantIs404()
    {
        var response = await _client.GetAsync($"/api/tenants/{Guid.NewGuid()}/issued-invoices/feed");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        _client.SetBearerToken(await _fixture.GetAccessTokenAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/tenants/{Guid.NewGuid()}/issued-invoices/feed")).StatusCode);
    }

    [Fact]
    public async Task Feed_AnotherUsersRealTenant_Is404()
    {
        _client.SetBearerToken(await _fixture.GetAccessTokenAsync());
        var otherTenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await WithDbAsync(async db =>
        {
            db.Tenants.Add(new Tenant { Id = otherTenantId, UserId = "other-user", Nip = "5222222222", CreatedAt = now, UpdatedAt = now });
            db.InvoiceHeaders.Add(Synced(otherTenantId, "KSEF-OTHER-USER", Utc(10, 5)));
            await db.SaveChangesAsync();
        });

        var response = await _client.GetAsync($"/api/tenants/{otherTenantId}/issued-invoices/feed");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Feed_AnotherTenantsOwnRowWithSameKsefNumber_DoesNotHideTheRow()
    {
        var tenantId = await CreateTenantAsync();
        var otherTenantId = await CreateTenantAsync();
        await WithDbAsync(async db =>
        {
            db.InvoiceHeaders.Add(Synced(tenantId, "KSEF-SHARED", Utc(10, 5)));
            await SaveAcceptedAsync(db, Own(otherTenantId, new DateOnly(2026, 10, 5), DateTime.UtcNow), "KSEF-SHARED");
        });

        var result = await FeedAsync(tenantId);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("External", result.Items.Single().Source);
    }

    [Fact]
    public async Task Feed_QueryMatchesKsefNumberOfOwnRow()
    {
        var tenantId = await CreateTenantAsync();
        await WithDbAsync(db => SaveAcceptedAsync(db, Own(tenantId, new DateOnly(2026, 10, 5), DateTime.UtcNow), "KSEF-OWN-SEARCH"));

        var result = await FeedAsync(tenantId, "q=own-search");

        Assert.Equal("OpenKSeF", result.Items.Single().Source);
    }

    [Fact]
    public async Task Feed_PageIsCappedAtTenThousandRows()
    {
        var tenantId = await CreateTenantAsync();

        var result = await FeedAsync(tenantId, $"page={int.MaxValue}&pageSize=100");

        Assert.Equal(100, result.Page);
    }

    [Fact]
    public async Task ReceivedReaders_NeverSeeIssuedRows_AsCalledByCurrentMobileAndPortal()
    {
        var tenantId = await CreateTenantAsync();
        var recent = DateTime.UtcNow.AddDays(-3);

        async Task<(string[] Mobile, string[] Summary, int Unpaid, JsonNode Dashboard)> ReadAllAsync()
        {
            var mobile = await ListAsync($"/api/tenants/{tenantId}/invoices?page=1&pageSize=20");
            var summary = await ListAsync($"/api/invoices?tenantId={tenantId}");
            var unpaid = await ListAsync($"/api/tenants/{tenantId}/invoices?isPaid=false");
            var dashboard = await _client.GetFromJsonAsync<JsonArray>("/api/dashboard");
            return (
                mobile.Items.Select(i => i.KSeFInvoiceNumber).ToArray(),
                summary.Items.Select(i => i.KSeFInvoiceNumber).ToArray(),
                unpaid.TotalCount,
                dashboard!.Single(d => d!["tenantId"]!.GetValue<Guid>() == tenantId)!);
        }

        await WithDbAsync(async db =>
        {
            db.InvoiceHeaders.Add(Synced(tenantId, "KSEF-BOUGHT", recent, InvoiceDirection.Received));
            await db.SaveChangesAsync();
        });
        var before = await ReadAllAsync();

        await WithDbAsync(async db =>
        {
            db.InvoiceHeaders.Add(Synced(tenantId, "KSEF-SOLD-1", recent));
            db.InvoiceHeaders.Add(Synced(tenantId, "KSEF-SOLD-2", recent));
            await db.SaveChangesAsync();
        });
        var after = await ReadAllAsync();

        Assert.Equal(["KSEF-BOUGHT"], after.Mobile);
        Assert.Equal(before.Mobile, after.Mobile);
        Assert.Equal(before.Summary, after.Summary);
        Assert.Equal(before.Unpaid, after.Unpaid);
        Assert.Equal(before.Dashboard.ToJsonString(), after.Dashboard.ToJsonString());

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/invoices/by-number/KSEF-SOLD-1")).StatusCode);

        var mobileJson = (await _client.GetFromJsonAsync<JsonObject>($"/api/tenants/{tenantId}/invoices?page=1&pageSize=20"))!;
        Assert.Equal("Received", mobileJson["items"]![0]!["direction"]!.GetValue<string>());
    }

    [Fact]
    public async Task IssuedRow_PaidAndTransferAre409_NothingChanges_NeverOverdue_DetailsStillOpen()
    {
        var tenantId = await CreateTenantAsync();
        var sold = Synced(tenantId, "KSEF-SOLD-DETAIL", DateTime.UtcNow.AddDays(-40));
        sold.DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        sold.VendorBankAccount = "PL61109010140000071219812874";
        await WithDbAsync(async db =>
        {
            db.InvoiceHeaders.Add(sold);
            await db.SaveChangesAsync();
        });

        var transfer = await _client.GetAsync($"/api/tenants/{tenantId}/invoices/{sold.Id}/transfer");
        Assert.Equal(HttpStatusCode.Conflict, transfer.StatusCode);

        var paid = await _client.PatchAsJsonAsync($"/api/tenants/{tenantId}/invoices/{sold.Id}/paid", new { isPaid = true });
        Assert.Equal(HttpStatusCode.Conflict, paid.StatusCode);

        await WithDbAsync(async db =>
        {
            var row = await db.InvoiceHeaders.AsNoTracking().SingleAsync(i => i.Id == sold.Id);
            Assert.False(row.IsPaid);
            Assert.Null(row.PaidAt);
        });

        var details = await _client.GetFromJsonAsync<JsonObject>($"/api/tenants/{tenantId}/invoices/by-number/KSEF-SOLD-DETAIL");
        Assert.Equal("Issued", details!["direction"]!.GetValue<string>());
        Assert.False(details["isOverdue"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ReceivedRow_OverdueStaysAsToday()
    {
        var tenantId = await CreateTenantAsync();
        var bought = Synced(tenantId, "KSEF-BOUGHT-LATE", DateTime.UtcNow.AddDays(-40), InvoiceDirection.Received);
        bought.DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        await WithDbAsync(async db =>
        {
            db.InvoiceHeaders.Add(bought);
            await db.SaveChangesAsync();
        });

        var details = await _client.GetFromJsonAsync<JsonObject>($"/api/tenants/{tenantId}/invoices/{bought.Id}");

        Assert.True(details!["isOverdue"]!.GetValue<bool>());
    }

    private sealed record TenantIdDto(Guid Id);
}
