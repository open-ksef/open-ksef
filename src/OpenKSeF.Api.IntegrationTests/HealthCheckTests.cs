using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Api.Push;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Push;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public class HealthCheckTests : IDisposable
{
    private readonly OpenKSeFWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public HealthCheckTests(TestcontainersFixture fixture)
    {
        _factory = new OpenKSeFWebApplicationFactory(fixture);
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_StaleSync_ReportsDegraded_WithStatus200()
    {
        var now = DateTime.UtcNow;
        var tenant = new Tenant { Id = Guid.NewGuid(), UserId = $"health-{Guid.NewGuid()}", Nip = "1111111111", CreatedAt = now.AddDays(-30), UpdatedAt = now };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Tenants.Add(tenant);
            db.KSeFCredentials.Add(new KSeFCredential { Id = Guid.NewGuid(), TenantId = tenant.Id, EncryptedToken = "x", CreatedAt = now.AddDays(-30), UpdatedAt = now });
            db.SyncStates.Add(new SyncState { Id = Guid.NewGuid(), TenantId = tenant.Id, LastSyncedAt = now.AddDays(-2) });
            await db.SaveChangesAsync();
        }

        try
        {
            var response = await _client.GetAsync("/health");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Degraded", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Tenants.Where(t => t.Id == tenant.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Swagger_ReturnsNotFound_InProduction()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void SensitiveDataLogging_IsOff_ByDefault()
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>();

        Assert.False(options.FindExtension<CoreOptionsExtension>()!.IsSensitiveDataLoggingEnabled);
    }

    [Fact]
    public void PushProviders_AreRelayFcmApns_Once_AndUserPushIsSignalR()
    {
        Assert.Equal(
            [typeof(RelayPushProvider), typeof(FcmPushProvider), typeof(ApnsPushProvider)],
            _factory.Services.GetServices<IPushProvider>().Select(p => p.GetType()));
        Assert.IsType<SignalRPushProvider>(_factory.Services.GetRequiredService<IUserPushProvider>());
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
