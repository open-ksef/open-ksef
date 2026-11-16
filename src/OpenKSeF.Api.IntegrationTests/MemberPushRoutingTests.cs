using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using OpenKSeF.Api.IntegrationTests.Infrastructure;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Events;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.IntegrationTests;

/// <summary>REQ-0037 push routing per member on real Postgres: opt-out, removal, device scope.</summary>
[Collection(TestcontainersCollection.Name)]
public sealed class MemberPushRoutingTests(TestcontainersFixture fixture) : IAsyncLifetime
{
    private readonly string _database = $"member_push_{Guid.NewGuid():N}";
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private string _connection = null!;

    public async Task InitializeAsync()
    {
        await using (var admin = new NpgsqlConnection(fixture.PostgresConnectionString))
        {
            await admin.OpenAsync();
            await new NpgsqlCommand($"CREATE DATABASE {_database}", admin).ExecuteNonQueryAsync();
        }

        _connection = new NpgsqlConnectionStringBuilder(fixture.PostgresConnectionString) { Database = _database }.ConnectionString;
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var now = DateTime.UtcNow;
        db.Tenants.Add(new Tenant { Id = _tenantId, UserId = "owner-a", Nip = "1111111111", CreatedAt = now, UpdatedAt = now });
        db.Tenants.Add(new Tenant { Id = _otherTenantId, UserId = "member-b", Nip = "1111111111", CreatedAt = now, UpdatedAt = now });
        db.TenantMembers.AddRange(
            new TenantMember { TenantId = _tenantId, UserId = "owner-a", Role = TenantRole.Owner, CreatedAt = now },
            new TenantMember { TenantId = _tenantId, UserId = "member-b", Role = TenantRole.Accountant, CreatedAt = now },
            new TenantMember { TenantId = _tenantId, UserId = "member-c", Role = TenantRole.Employee, CreatedAt = now },
            new TenantMember { TenantId = _otherTenantId, UserId = "member-b", Role = TenantRole.Owner, CreatedAt = now });
        db.DeviceTokens.AddRange(
            Device("owner-a", "phone-a", null, now),
            Device("member-b", "phone-b", _tenantId, now),
            Device("member-b", "phone-b-other-company", _otherTenantId, now),
            Device("member-c", "phone-c", null, now),
            Device("stranger", "phone-stranger", null, now));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using (var own = new NpgsqlConnection(_connection))
            NpgsqlConnection.ClearPool(own);
        await using var admin = new NpgsqlConnection(fixture.PostgresConnectionString);
        await admin.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS {_database} WITH (FORCE)", admin).ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task NewInvoice_OptedOutAndRemovedMembersGetNothing_DevicesKept()
    {
        Assert.Equal(["phone-a", "phone-b", "phone-c"], (await NotifyAsync()).Order());

        await using (var db = NewContext())
        {
            await db.TenantMembers.Where(m => m.TenantId == _tenantId && m.UserId == "member-b")
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.NotifyNewInvoices, false));
            await db.TenantMembers.Where(m => m.TenantId == _tenantId && m.UserId == "member-c").ExecuteDeleteAsync();
        }

        var pushed = await NotifyAsync();

        Assert.Equal(["phone-a"], pushed);
        await using var check = NewContext();
        Assert.Equal(5, await check.DeviceTokens.CountAsync());
    }

    [Fact]
    public async Task AccessGranted_GoesToUsersAllCompaniesAndSameCompanyDevicesOnly()
    {
        var provider = new RecordingPushProvider();
        await using var db = NewContext();

        await new NotificationService(db, [provider], Substitute.For<IEmailService>(), NullLogger<NotificationService>.Instance)
            .NotifyAccessGrantedAsync("member-b", _tenantId, "Firma Testowa");

        Assert.Equal(["phone-b"], provider.Tokens);
    }

    private async Task<List<string>> NotifyAsync()
    {
        var provider = new RecordingPushProvider();
        await using var db = NewContext();
        var sut = new NotificationService(db, [provider], Substitute.For<IEmailService>(), NullLogger<NotificationService>.Instance);

        Assert.True(await sut.NotifyNewInvoiceAsync(new NewInvoiceDetectedEvent(_tenantId, Guid.NewGuid(), "Dostawca", "FV/1", 10m, "PLN")));
        return provider.Tokens;
    }

    private static DeviceToken Device(string userId, string token, Guid? tenantId, DateTime now) =>
        new() { Id = Guid.NewGuid(), UserId = userId, Token = token, Platform = Platform.Android, TenantId = tenantId, CreatedAt = now, UpdatedAt = now };

    private ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_connection).Options);

    private sealed class RecordingPushProvider : IPushProvider
    {
        public List<string> Tokens { get; } = [];

        public Task<PushResult> SendAsync(string deviceToken, Platform platform, PushNotification notification)
        {
            Tokens.Add(deviceToken);
            return Task.FromResult(PushResult.Sent);
        }
    }
}
