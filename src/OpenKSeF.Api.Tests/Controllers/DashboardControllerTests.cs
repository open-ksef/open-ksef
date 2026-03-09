using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;
using NSubstitute;
using OpenKSeF.Api.Controllers;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;
using System.Data.Common;

namespace OpenKSeF.Api.Tests.Controllers;

public class DashboardControllerTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly string _userId = "user-1";

    public DashboardControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(options);
        _currentUser = Substitute.For<ICurrentUserService>();
        _currentUser.UserId.Returns(_userId);
    }

    [Fact]
    public void Controller_HasExpectedAttributesAndCanBeConstructed()
    {
        var type = typeof(DashboardController);

        Assert.NotNull(type.GetCustomAttributes(typeof(ApiControllerAttribute), inherit: false).SingleOrDefault());

        var route = type.GetCustomAttributes(typeof(RouteAttribute), inherit: false)
            .Cast<RouteAttribute>()
            .Single();

        Assert.Equal("api/[controller]", route.Template);
        Assert.NotNull(type.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false).SingleOrDefault());

        var controller = new DashboardController(_db, _currentUser);
        Assert.NotNull(controller);
    }

    [Fact]
    public void Get_HasProducesResponseTypeAttribute()
    {
        var method = typeof(DashboardController).GetMethod(nameof(DashboardController.Get));
        Assert.NotNull(method);

        var attribute = method!
            .GetCustomAttributes(typeof(ProducesResponseTypeAttribute), inherit: false)
            .Cast<ProducesResponseTypeAttribute>()
            .SingleOrDefault(a => a.StatusCode == StatusCodes.Status200OK);

        Assert.NotNull(attribute);
        Assert.Equal(typeof(List<TenantDashboardSummaryResponse>), attribute!.Type);
    }

    [Fact]
    public async Task Get_ReturnsAggregatedDashboardForCurrentUserTenantsOnly()
    {
        var now = DateTime.UtcNow;

        var tenantSuccess = AddTenant("1111111111", "Tenant Success", _userId);
        var tenantWarning = AddTenant("2222222222", "Tenant Warning", _userId);
        var tenantError = AddTenant("3333333333", "Tenant Error", _userId);
        var tenantForeign = AddTenant("4444444444", "Tenant Foreign", "other-user");

        _db.SyncStates.AddRange(
            new SyncState
            {
                Id = Guid.NewGuid(),
                TenantId = tenantSuccess.Id,
                LastSyncedAt = now.AddHours(-1),
                LastSuccessfulSync = now.AddHours(-2)
            },
            new SyncState
            {
                Id = Guid.NewGuid(),
                TenantId = tenantWarning.Id,
                LastSyncedAt = now.AddDays(-2),
                LastSuccessfulSync = now.AddDays(-3)
            },
            new SyncState
            {
                Id = Guid.NewGuid(),
                TenantId = tenantForeign.Id,
                LastSyncedAt = now.AddHours(-1),
                LastSuccessfulSync = now.AddHours(-1)
            });

        _db.InvoiceHeaders.AddRange(
            MakeInvoice(tenantSuccess.Id, now.AddDays(-1)),
            MakeInvoice(tenantSuccess.Id, now.AddDays(-6)),
            MakeInvoice(tenantSuccess.Id, now.AddDays(-20)),
            MakeInvoice(tenantSuccess.Id, now.AddDays(-40)),
            MakeInvoice(tenantWarning.Id, now.AddDays(-2)),
            MakeInvoice(tenantWarning.Id, now.AddDays(-10)),
            MakeInvoice(tenantForeign.Id, now.AddDays(-1)));

        await _db.SaveChangesAsync();

        var controller = new DashboardController(_db, _currentUser);
        var result = await controller.Get() as OkObjectResult;

        var summaries = Assert.IsType<List<TenantDashboardSummaryResponse>>(result!.Value);
        Assert.Equal(3, summaries.Count);

        var byId = summaries.ToDictionary(s => s.TenantId);

        Assert.True(byId.ContainsKey(tenantSuccess.Id));
        Assert.Equal(4, byId[tenantSuccess.Id].TotalInvoices);
        Assert.Equal(2, byId[tenantSuccess.Id].InvoicesLast7Days);
        Assert.Equal(3, byId[tenantSuccess.Id].InvoicesLast30Days);
        Assert.Equal("Success", byId[tenantSuccess.Id].SyncStatus);

        Assert.True(byId.ContainsKey(tenantWarning.Id));
        Assert.Equal(2, byId[tenantWarning.Id].TotalInvoices);
        Assert.Equal(1, byId[tenantWarning.Id].InvoicesLast7Days);
        Assert.Equal(2, byId[tenantWarning.Id].InvoicesLast30Days);
        Assert.Equal("Warning", byId[tenantWarning.Id].SyncStatus);

        Assert.True(byId.ContainsKey(tenantError.Id));
        Assert.Equal(0, byId[tenantError.Id].TotalInvoices);
        Assert.Equal(0, byId[tenantError.Id].InvoicesLast7Days);
        Assert.Equal(0, byId[tenantError.Id].InvoicesLast30Days);
        Assert.Equal("Error", byId[tenantError.Id].SyncStatus);

        Assert.False(byId.ContainsKey(tenantForeign.Id));
    }

    [Fact]
    public async Task Get_ReturnsEmptyList_WhenCurrentUserHasNoTenants()
    {
        var controller = new DashboardController(_db, _currentUser);
        var result = await controller.Get() as OkObjectResult;

        var summaries = Assert.IsType<List<TenantDashboardSummaryResponse>>(result!.Value);
        Assert.Empty(summaries);
    }

    [Fact]
    public async Task Get_IsolatesByUserId()
    {
        AddTenant("6666666666", "Tenant A", _userId);
        AddTenant("7777777777", "Tenant B", _userId);
        AddTenant("8888888888", "Tenant Foreign", "other-user");
        await _db.SaveChangesAsync();

        var controller = new DashboardController(_db, _currentUser);
        var result = await controller.Get() as OkObjectResult;

        var summaries = Assert.IsType<List<TenantDashboardSummaryResponse>>(result!.Value);
        Assert.Equal(2, summaries.Count);
        Assert.DoesNotContain(summaries, s => s.Nip == "8888888888");
    }

    [Fact]
    public async Task Get_SyncOlderThanSevenDays_ReturnsErrorStatus()
    {
        var now = DateTime.UtcNow;
        var tenant = AddTenant("9999999999", "Stale Sync Tenant", _userId);

        _db.SyncStates.Add(new SyncState
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            LastSyncedAt = now.AddDays(-8),
            LastSuccessfulSync = now.AddDays(-8)
        });
        await _db.SaveChangesAsync();

        var controller = new DashboardController(_db, _currentUser);
        var result = await controller.Get() as OkObjectResult;

        var summaries = Assert.IsType<List<TenantDashboardSummaryResponse>>(result!.Value);
        var summary = Assert.Single(summaries);
        Assert.Equal("Error", summary.SyncStatus);
    }

    [Fact]
    public async Task Get_ExecutesSingleSelectQuery()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var counter = new SelectCommandCounterInterceptor();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(counter)
            .Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(_userId);

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Nip = "5555555555",
            DisplayName = "Tenant Sqlite",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Tenants.Add(tenant);
        db.SyncStates.Add(new SyncState
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            LastSyncedAt = DateTime.UtcNow.AddHours(-1),
            LastSuccessfulSync = DateTime.UtcNow.AddHours(-1)
        });
        db.InvoiceHeaders.Add(MakeInvoice(tenant.Id, DateTime.UtcNow.AddDays(-1)));
        await db.SaveChangesAsync();

        counter.Reset();

        var controller = new DashboardController(db, currentUser);
        var result = await controller.Get();

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, counter.ExecutedSelectCount);
    }

    private Tenant AddTenant(string nip, string? displayName, string userId)
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Nip = nip,
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Tenants.Add(tenant);
        return tenant;
    }

    private static InvoiceHeader MakeInvoice(Guid tenantId, DateTime issueDate)
    {
        var number = $"INV-{Guid.NewGuid():N}";
        return new InvoiceHeader
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            KSeFInvoiceNumber = number,
            KSeFReferenceNumber = $"{number}-REF",
            VendorName = "Vendor",
            VendorNip = "1234567890",
            AmountGross = 100m,
            Currency = "PLN",
            IssueDate = issueDate,
            FirstSeenAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow
        };
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private sealed class SelectCommandCounterInterceptor : DbCommandInterceptor
    {
        public int ExecutedSelectCount { get; private set; }

        public void Reset() => ExecutedSelectCount = 0;

        public override DbDataReader ReaderExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                ExecutedSelectCount++;
            }

            return base.ReaderExecuted(command, eventData, result);
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                ExecutedSelectCount++;
            }

            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }
}
