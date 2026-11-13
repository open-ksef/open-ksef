using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;

namespace OpenKSeF.Api.IntegrationTests;

[Collection(TestcontainersCollection.Name)]
public sealed class UpsertRaceTests(TestcontainersFixture fixture)
{
    [Fact]
    public async Task ParallelRegisterOfOneNewToken_BothSucceed_OneRow()
    {
        var barrier = new InsertBarrier<DeviceToken>();
        await using var factory = new OpenKSeFWebApplicationFactory(fixture).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.ConfigureDbContext<ApplicationDbContext>(o => o.AddInterceptors(barrier))));
        var bearer = await fixture.GetAccessTokenAsync("plainuser");
        var token = $"race-{Guid.NewGuid():N}";

        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            using var client = factory.CreateClient();
            client.SetBearerToken(bearer);
            return await client.PostAsJsonAsync("/api/devices/register", new { token, platform = 0 });
        }));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(2, barrier.Waits);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.DeviceTokens.CountAsync(d => d.Token == token));
    }

    [Fact]
    public async Task ParallelSaveOfFirstCredential_BothSucceed_OneRow()
    {
        var barrier = new InsertBarrier<KSeFCredential>();
        await using var factory = new OpenKSeFWebApplicationFactory(fixture).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.ConfigureDbContext<ApplicationDbContext>(o => o.AddInterceptors(barrier))));
        var bearer = await fixture.GetAccessTokenAsync("plainuser");
        Guid tenantId;
        using (var client = factory.CreateClient())
        {
            client.SetBearerToken(bearer);
            var created = await client.PostAsJsonAsync("/api/tenants", new { nip = TestNip.New(), displayName = $"Race {Guid.NewGuid():N}" });
            created.EnsureSuccessStatusCode();
            tenantId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        var responses = await Task.WhenAll(new[] { "token-a", "token-b" }.Select(async token =>
        {
            using var client = factory.CreateClient();
            client.SetBearerToken(bearer);
            return await client.PostAsJsonAsync($"/api/tenants/{tenantId}/credentials", new { type = "Token", token });
        }));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(2, barrier.Waits);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.KSeFCredentials.CountAsync(c => c.TenantId == tenantId));
    }

    private sealed class InsertBarrier<TEntity> : SaveChangesInterceptor where TEntity : class
    {
        private readonly Barrier _barrier = new(2);
        private int _waits;

        public int Waits => _waits;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<TEntity>().Any(e => e.State == EntityState.Added))
            {
                Interlocked.Increment(ref _waits);
                await Task.Run(() => _barrier.SignalAndWait(TimeSpan.FromSeconds(10)), cancellationToken);
            }

            return result;
        }
    }
}
