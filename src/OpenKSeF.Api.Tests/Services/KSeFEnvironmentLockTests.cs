using KSeF.Client.DI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Tests.Services;

public class KSeFEnvironmentLockTests
{
    [Fact]
    public async Task IssuedInvoice_LocksEnvironment_LikeSyncedInvoice()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var provider = new ServiceCollection()
            .AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(databaseName))
            .BuildServiceProvider();
        var service = new SystemSettingsService(
            Substitute.For<IHttpClientFactory>(),
            new ConfigurationBuilder().Build(),
            Substitute.For<ISystemConfigService>(),
            new KSeFClientOptions(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SystemSettingsService>.Instance);

        Assert.False((await service.GetKSeFEnvironmentLockStatusAsync()).HasInvoices);

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.IssuedInvoices.Add(IssuedInvoice.Draft(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 11, 2), new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc)));
            await db.SaveChangesAsync();
        }

        Assert.True((await service.GetKSeFEnvironmentLockStatusAsync()).HasInvoices);
    }
}
