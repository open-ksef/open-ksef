using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Sync;
using OpenKSeF.Sync.Issuing;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public sealed class DashboardApiTests : IAsyncLifetime
{
    private readonly TestcontainersFixture _fixture;
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _tenantIds = [];

    public DashboardApiTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
        _factory = new OpenKSeFWebApplicationFactory(fixture);
        _client = _factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Tenants.Where(t => _tenantIds.Contains(t.Id)).ExecuteDeleteAsync();
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Dashboard_TellsNoCredential_NeverSynced_Failed_AndHealthyApart()
    {
        _client.SetBearerToken(await _fixture.GetAccessTokenAsync());
        var noCredential = await CreateTenantAsync();
        var neverSynced = await CreateTenantAsync(withCredential: true);
        var failed = await CreateTenantAsync(withCredential: true, state: new SyncState { LastSuccessfulSync = DateTime.UtcNow.AddDays(-3), LastErrorCategory = SyncFailure.InvalidToken, LastErrorAt = DateTime.UtcNow.AddHours(-1) });
        var unavailable = await CreateTenantAsync(withCredential: true, state: new SyncState { LastErrorCategory = SyncFailure.KsefUnavailable, LastErrorAt = DateTime.UtcNow });
        var healthy = await CreateTenantAsync(withCredential: true, state: new SyncState { LastSyncedAt = DateTime.UtcNow, LastSuccessfulSync = DateTime.UtcNow });
        var replaced = await CreateTenantAsync(withCredential: true, credentialUpdatedAt: DateTime.UtcNow, state: new SyncState { LastSuccessfulSync = DateTime.UtcNow.AddDays(-3), LastErrorCategory = SyncFailure.InvalidToken, LastErrorAt = DateTime.UtcNow.AddHours(-1) });
        var deleted = await CreateTenantAsync(state: new SyncState { LastErrorCategory = SyncFailure.InvalidToken, LastErrorAt = DateTime.UtcNow.AddHours(-1) });

        var cards = (await _client.GetFromJsonAsync<List<CardDto>>("/api/dashboard"))!.ToDictionary(c => c.TenantId);

        Assert.Equal(("NoCredential", null, null), (cards[noCredential].SyncProblem, cards[noCredential].LastSyncErrorCategory, cards[noCredential].LastSyncErrorMessage));
        Assert.Equal(("NeverSynced", null), (cards[neverSynced].SyncProblem, cards[neverSynced].LastSyncErrorCategory));
        Assert.Equal(("Failed", "InvalidToken", IssuedInvoiceSendProcessor.InvalidTokenMessage), (cards[failed].SyncProblem, cards[failed].LastSyncErrorCategory, cards[failed].LastSyncErrorMessage));
        Assert.NotNull(cards[failed].LastSyncErrorAt);
        Assert.Equal(("Failed", "KsefUnavailable", SyncFailure.Message(SyncFailure.KsefUnavailable)), (cards[unavailable].SyncProblem, cards[unavailable].LastSyncErrorCategory, cards[unavailable].LastSyncErrorMessage));
        Assert.True(cards[failed].LastSyncErrorCredentialCause);
        Assert.False(cards[unavailable].LastSyncErrorCredentialCause);
        Assert.False(cards[noCredential].LastSyncErrorCredentialCause);
        Assert.Equal((null, "Success"), (cards[healthy].SyncProblem, cards[healthy].SyncStatus));
        Assert.Equal((null, null, null, false), (cards[replaced].SyncProblem, cards[replaced].LastSyncErrorCategory, cards[replaced].LastSyncErrorMessage, cards[replaced].LastSyncErrorCredentialCause));
        Assert.Equal(("NoCredential", null, false), (cards[deleted].SyncProblem, cards[deleted].LastSyncErrorCategory, cards[deleted].LastSyncErrorCredentialCause));
    }

    [Fact]
    public async Task Dashboard_StillParsesIntoTheShapeBeforeTheReasonFields()
    {
        _client.SetBearerToken(await _fixture.GetAccessTokenAsync());
        var tenantId = await CreateTenantAsync(withCredential: true, state: new SyncState { LastErrorCategory = SyncFailure.RateLimited, LastErrorAt = DateTime.UtcNow });

        var json = await _client.GetStringAsync("/api/dashboard");
        var old = JsonSerializer.Deserialize<List<OldCardDto>>(json, JsonSerializerOptions.Web)!;

        Assert.Equal("Error", old.Single(c => c.TenantId == tenantId).SyncStatus);
    }

    private async Task<Guid> CreateTenantAsync(bool withCredential = false, SyncState? state = null, DateTime? credentialUpdatedAt = null)
    {
        var response = await _client.PostAsJsonAsync("/api/tenants", new { nip = TestNip.New(), displayName = "Dashboard test" });
        response.EnsureSuccessStatusCode();
        var tenantId = (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
        _tenantIds.Add(tenantId);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        if (withCredential)
            db.KSeFCredentials.Add(new KSeFCredential { Id = Guid.NewGuid(), TenantId = tenantId, EncryptedToken = "encrypted", CreatedAt = now.AddDays(-30), UpdatedAt = credentialUpdatedAt ?? now.AddDays(-30) });
        if (state is not null)
        {
            state.Id = Guid.NewGuid();
            state.TenantId = tenantId;
            db.SyncStates.Add(state);
        }

        await db.SaveChangesAsync();
        return tenantId;
    }

    private sealed record IdDto(Guid Id);

    private sealed record CardDto(Guid TenantId, string SyncStatus, string? SyncProblem, string? LastSyncErrorCategory, string? LastSyncErrorMessage, DateTime? LastSyncErrorAt, bool LastSyncErrorCredentialCause);

    private sealed record OldCardDto(Guid TenantId, string Nip, string? DisplayName, DateTime? LastSyncedAt, DateTime? LastSuccessfulSync, int TotalInvoices, int InvoicesLast7Days, int InvoicesLast30Days, string SyncStatus);
}
