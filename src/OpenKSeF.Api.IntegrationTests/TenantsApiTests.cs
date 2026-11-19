using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Invoicing;

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

        var nip = TestNip.New();
        var response = await _client.PostAsJsonAsync("/api/tenants", new
        {
            nip,
            displayName = "Integration Test Co",
            notificationEmail = "test@example.com"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var tenant = await response.Content.ReadFromJsonAsync<TenantDto>();
        Assert.NotNull(tenant);
        Assert.Equal(nip, tenant.Nip);
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
    public async Task CreateTenant_ChecksumInvalidNip_Returns400()
    {
        await AuthenticateAsync();

        var response = await _client.PostAsJsonAsync("/api/tenants", new
        {
            nip = "1111111112",
            displayName = "Bad Checksum Co"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("""{"error":"Niepoprawny NIP. Wpisz 10 cyfr z poprawną sumą kontrolną."}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreateTenant_ParallelPostsOfOneNip_OneUser_OneCreated_RestConflict()
    {
        await AuthenticateAsync();
        var nip = TestNip.New();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => _client.PostAsJsonAsync("/api/tenants", new { nip, displayName = $"Parallel {i}" })));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Tenants.CountAsync(t => t.Nip == nip));
    }

    [Fact]
    public async Task CreateTenant_ParallelPostsOfOneNip_TwoUsers_OneCreated_RestConflict()
    {
        await AuthenticateAsync();
        using var other = _factory.CreateClient();
        other.SetBearerToken(await _fixture.GetAccessTokenAsync("otheruser"));
        var nip = TestNip.New();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => (i % 2 == 0 ? _client : other).PostAsJsonAsync("/api/tenants", new { nip, displayName = $"Parallel {i}" })));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Tenants.CountAsync(t => t.Nip == nip));
    }

    [Fact]
    public async Task CreateTenant_NipOfAnotherUsersCompany_409WithOnlyError_NothingCreated()
    {
        await AuthenticateAsync();
        var nip = TestNip.New();
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/tenants", new { nip, displayName = "Firma A" })).StatusCode);
        using var other = _factory.CreateClient();
        other.SetBearerToken(await _fixture.GetAccessTokenAsync("otheruser"));

        var response = await other.PostAsJsonAsync("/api/tenants", new { nip, displayName = "Firma B" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("""{"error":"Firma z tym NIP-em jest już dodana na tym serwerze OpenKSeF. Poproś osobę, która nią zarządza, o zaproszenie do firmy. Jeśli nie wiesz, kto to jest, napisz do administratora serwera. Jeśli masz token KSeF z uprawnieniem Zarządzanie uprawnieniami dla tego NIP-u, możesz przejąć firmę w portalu OpenKSeF (Przejmij firmę)."}""", await response.Content.ReadAsStringAsync());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Tenants.CountAsync(t => t.Nip == nip));
    }

    [Fact]
    public async Task CreateTenant_RetainedDeletedCompany_DoesNotBlockNip()
    {
        await AuthenticateAsync();
        var nip = TestNip.New();
        var retainedId = (await (await _client.PostAsJsonAsync("/api/tenants", new { nip, displayName = "Retained" })).Content.ReadFromJsonAsync<TenantDto>())!.Id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlAsync($"""UPDATE "Tenants" SET "UserId" = 'deleted:' || "Id" WHERE "Id" = {retainedId}""");
            await db.TenantMembers.Where(m => m.TenantId == retainedId).ExecuteDeleteAsync();
        }
        using var other = _factory.CreateClient();
        other.SetBearerToken(await _fixture.GetAccessTokenAsync("otheruser"));

        var response = await other.PostAsJsonAsync("/api/tenants", new { nip, displayName = "Nowa" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotEqual(retainedId, (await response.Content.ReadFromJsonAsync<TenantDto>())!.Id);
    }

    [Fact]
    public async Task ExistingDuplicates_AfterRestart_Unchanged_StillUsable_ThirdUserGets409()
    {
        var nip = TestNip.New();
        var ownerIds = new[]
        {
            TestcontainersFixture.AccessTokenClaims(await _fixture.GetAccessTokenAsync()).GetProperty("sub").GetString()!,
            TestcontainersFixture.AccessTokenClaims(await _fixture.GetAccessTokenAsync("otheruser")).GetProperty("sub").GetString()!,
        };
        var tenantIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var now = DateTime.UtcNow;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (var i = 0; i < 2; i++)
            {
                db.Tenants.Add(new Tenant { Id = tenantIds[i], UserId = ownerIds[i], Nip = nip, DisplayName = $"Duplikat {i}", CreatedAt = now, UpdatedAt = now });
                db.TenantMembers.Add(new TenantMember { TenantId = tenantIds[i], UserId = ownerIds[i], Role = TenantRole.Owner, CreatedAt = now });
                db.TenantMembers.Add(new TenantMember { TenantId = tenantIds[i], UserId = ownerIds[1 - i], Role = TenantRole.Accountant, CreatedAt = now });
                db.KSeFCredentials.Add(new KSeFCredential { Id = Guid.NewGuid(), TenantId = tenantIds[i], EncryptedToken = "encrypted", CreatedAt = now, UpdatedAt = now });
                db.InvoiceHeaders.Add(new InvoiceHeader
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantIds[i],
                    KSeFInvoiceNumber = $"DUP-{Guid.NewGuid():N}",
                    KSeFReferenceNumber = "DUP",
                    VendorName = "Dostawca",
                    VendorNip = "1111111111",
                    Direction = InvoiceDirection.Received,
                    IssueDate = now,
                    FirstSeenAt = now,
                    LastUpdatedAt = now,
                    IsPaid = true,
                    PaidAt = now.AddHours(-i),
                    PaidByUserId = ownerIds[i],
                });
                var issued = IssuedInvoice.Draft(Guid.NewGuid(), tenantIds[i], new DateOnly(2026, 11, 3), now);
                issued.SetBuyer(BuyerKind.Company, "1111111111", "Nabywca", "ul. Testowa 1", "00-001 Warszawa");
                db.IssuedInvoices.Add(issued);
            }
            await db.SaveChangesAsync();
        }
        var before = await SnapshotAsync(_factory, tenantIds);

        await using var restarted = new OpenKSeFWebApplicationFactory(_fixture);
        using var third = restarted.CreateClient();
        var thirdEmail = $"tenants-third-{Guid.NewGuid():N}@openksef.test";
        await _fixture.CreateUserAsync(thirdEmail);
        third.SetBearerToken(await _fixture.GetAccessTokenAsync(thirdEmail));

        var conflict = await third.PostAsJsonAsync("/api/tenants", new { nip, displayName = "Trzecia" });

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(TenantNipGuard.TakenMessage, (await conflict.Content.ReadFromJsonAsync<ErrorDto>())!.Error);
        foreach (var (user, tenantId) in new[] { ("testuser", tenantIds[0]), ("otheruser", tenantIds[1]) })
        {
            using var owner = restarted.CreateClient();
            owner.SetBearerToken(await _fixture.GetAccessTokenAsync(user));
            Assert.Equal(tenantIds.Order(), (await owner.GetFromJsonAsync<List<TenantDto>>("/api/tenants"))!.Where(t => t.Nip == nip).Select(t => t.Id).Order());
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/tenants/{tenantId}/invoices")).StatusCode);
        }
        Assert.Equal(before, await SnapshotAsync(restarted, tenantIds));
    }

    private static async Task<List<string>> SnapshotAsync(OpenKSeFWebApplicationFactory factory, Guid[] tenantIds)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = new List<string>();
        foreach (var (table, key) in new[] { ("Tenants", "Id"), ("TenantMembers", "TenantId"), ("KSeFCredentials", "TenantId"), ("InvoiceHeaders", "TenantId"), ("IssuedInvoices", "TenantId") })
            rows.AddRange(await db.Database.SqlQueryRaw<string>($$"""SELECT row_to_json(t)::text AS "Value" FROM "{{table}}" t WHERE t."{{key}}" = ANY({0}) ORDER BY 1""", tenantIds).ToListAsync());
        return rows;
    }

    [Fact]
    public async Task CreateTenant_DuplicateNip_Returns409()
    {
        await AuthenticateAsync();
        var nip = TestNip.New();

        var first = await _client.PostAsJsonAsync("/api/tenants", new { nip, displayName = "First Co" });
        var second = await _client.PostAsJsonAsync("/api/tenants", new { nip, displayName = "Second Co" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("""{"error":"Masz już dostęp do firmy z tym NIP-em: First Co."}""", await second.Content.ReadAsStringAsync());

        var tenantId = (await first.Content.ReadFromJsonAsync<TenantDto>())!.Id;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var creator = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.UserId).SingleAsync();
        var owner = Assert.Single(await db.TenantMembers.Where(m => m.TenantId == tenantId).ToListAsync());
        Assert.Equal(creator, owner.UserId);
        Assert.Equal(TenantRole.Owner, owner.Role);
    }

    [Fact]
    public async Task GetTenant_AfterCreate_ReturnsTenant()
    {
        await AuthenticateAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/tenants", new
        {
            nip = TestNip.New(),
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
            nip = TestNip.New(),
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
    [InlineData("""{"displayName":"Renamed"}""", "keep@example.com")]
    [InlineData("""{"displayName":"Renamed","notificationEmail":null}""", null)]
    [InlineData("""{"displayName":"Renamed","notificationEmail":""}""", null)]
    [InlineData("""{"displayName":"Renamed","notificationEmail":"new@example.com"}""", "new@example.com")]
    public async Task UpdateTenant_NotificationEmail_AbsentKeepsOtherwiseApplies(string body, string? expectedEmail)
    {
        await AuthenticateAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/tenants", new
        {
            nip = TestNip.New(),
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

    [Theory]
    [InlineData("   ", null)]
    [InlineData(" jan@example.com ", "jan@example.com")]
    public async Task CreateAndUpdateTenant_NotificationEmail_StoredTrimmedOrNull(string email, string? expected)
    {
        await AuthenticateAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/tenants", new { nip = TestNip.New(), notificationEmail = email });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<TenantDto>();
        Assert.NotNull(created);
        Assert.Equal(expected, (await _client.GetFromJsonAsync<TenantDto>($"/api/tenants/{created.Id}"))!.NotificationEmail);

        var reset = await _client.PutAsJsonAsync($"/api/tenants/{created.Id}", new { notificationEmail = "other@example.com" });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var update = await _client.PutAsJsonAsync($"/api/tenants/{created.Id}", new { notificationEmail = email });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(expected, (await _client.GetFromJsonAsync<TenantDto>($"/api/tenants/{created.Id}"))!.NotificationEmail);
    }

    [Theory]
    [InlineData(" not-an-email ")]
    [InlineData(" @example.com")]
    [InlineData("jan@ ")]
    public async Task CreateAndUpdateTenant_EmailInvalidAfterTrim_Returns400(string email)
    {
        await AuthenticateAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/tenants", new { nip = TestNip.New(), notificationEmail = email });
        Assert.Equal(HttpStatusCode.BadRequest, createResponse.StatusCode);

        var valid = await _client.PostAsJsonAsync("/api/tenants", new { nip = TestNip.New(), notificationEmail = "keep@example.com" });
        var created = await valid.Content.ReadFromJsonAsync<TenantDto>();
        var update = await _client.PutAsJsonAsync($"/api/tenants/{created!.Id}", new { notificationEmail = email });
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
    }

    [Fact]
    public async Task UpdateTenant_InvalidNotificationEmail_Returns400AndKeepsValue()
    {
        await AuthenticateAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/tenants", new
        {
            nip = TestNip.New(),
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

    [Fact]
    public async Task MyNotifications_Toggle_ReflectedInTenantResponse()
    {
        await AuthenticateAsync();
        var created = await (await _client.PostAsJsonAsync("/api/tenants", new { nip = TestNip.New(), displayName = "Notify Co" }))
            .Content.ReadFromJsonAsync<TenantDto>();
        Assert.True(created!.NotificationsEnabled);

        var off = await _client.PutAsJsonAsync($"/api/tenants/{created.Id}/members/me/notifications", new { enabled = false });

        Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);
        Assert.False((await _client.GetFromJsonAsync<TenantDto>($"/api/tenants/{created.Id}"))!.NotificationsEnabled);
        Assert.False((await _client.GetFromJsonAsync<List<TenantDto>>("/api/tenants"))!.Single(t => t.Id == created.Id).NotificationsEnabled);

        await _client.PutAsJsonAsync($"/api/tenants/{created.Id}/members/me/notifications", new { enabled = true });
        Assert.True((await _client.GetFromJsonAsync<TenantDto>($"/api/tenants/{created.Id}"))!.NotificationsEnabled);
    }

    [Fact]
    public async Task MyNotifications_NonMember_Returns404()
    {
        await AuthenticateAsync();
        var created = await (await _client.PostAsJsonAsync("/api/tenants", new { nip = TestNip.New() })).Content.ReadFromJsonAsync<TenantDto>();
        _client.SetBearerToken(await _fixture.GetAccessTokenAsync("otheruser"));

        var response = await _client.PutAsJsonAsync($"/api/tenants/{created!.Id}/members/me/notifications", new { enabled = false });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedCall_CreatesProfileOnce_WithNormalizedEmail()
    {
        var token = await _fixture.GetAccessTokenAsync();
        var userId = TestcontainersFixture.AccessTokenClaims(token).GetProperty("sub").GetString();
        _client.SetBearerToken(token);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/tenants")).StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var profile = await db.UserProfiles.AsNoTracking().SingleAsync(p => p.UserId == userId);

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/tenants")).StatusCode);

        Assert.Equal(("testuser@openksef.test", "Test User"), (profile.Email, profile.DisplayName));
        Assert.Equal(TestcontainersFixture.AccessTokenClaims(token).GetProperty("email_verified").GetBoolean(), profile.EmailVerified);
        Assert.Equal(profile.UpdatedAt, (await db.UserProfiles.AsNoTracking().SingleAsync(p => p.UserId == profile.UserId)).UpdatedAt);
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
        DateTime CreatedAt,
        bool NotificationsEnabled = false);

    private sealed record ErrorDto(string Error);
}
