using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenKSeF.Api.Controllers;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;

namespace OpenKSeF.Api.Tests.Controllers;

public class CredentialsControllerSyncTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IEncryptionService _encryption;
    private readonly ITenantSyncService _tenantSyncService;
    private readonly ILogger<CredentialsController> _logger;
    private readonly string _userId = "user-1";

    public CredentialsControllerSyncTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _currentUser = Substitute.For<ICurrentUserService>();
        _encryption = Substitute.For<IEncryptionService>();
        _tenantSyncService = Substitute.For<ITenantSyncService>();
        _logger = Substitute.For<ILogger<CredentialsController>>();
        _currentUser.UserId.Returns(_userId);
    }

    private CredentialsController CreateController() =>
        new(_db, _currentUser, _encryption, _tenantSyncService, _logger);

    [Fact]
    public async Task SyncNow_ReturnsNotFound_WhenTenantNotFound()
    {
        var tenantId = Guid.NewGuid();
        _tenantSyncService.SyncTenantAsync(tenantId, _userId, Arg.Any<CancellationToken>())
            .Returns(new TenantSyncResult(tenantId, "", TenantSyncOutcome.TenantNotFound, ErrorMessage: "Tenant not found."));

        var controller = CreateController();
        var result = await controller.SyncNow(tenantId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task SyncNow_ReturnsConflict_WhenMissingCredential()
    {
        var tenantId = Guid.NewGuid();
        _tenantSyncService.SyncTenantAsync(tenantId, _userId, Arg.Any<CancellationToken>())
            .Returns(new TenantSyncResult(tenantId, "1234567890", TenantSyncOutcome.MissingCredential, ErrorMessage: "Missing credential."));

        var controller = CreateController();
        var result = await controller.SyncNow(tenantId, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task SyncNow_ReturnsBadGateway_WhenSyncFails()
    {
        var tenantId = Guid.NewGuid();
        _tenantSyncService.SyncTenantAsync(tenantId, _userId, Arg.Any<CancellationToken>())
            .Returns(new TenantSyncResult(tenantId, "1234567890", TenantSyncOutcome.Failed, ErrorMessage: "KSeF unavailable."));

        var controller = CreateController();
        var result = await controller.SyncNow(tenantId, CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, status.StatusCode);
    }

    [Fact]
    public async Task SyncNow_ReturnsResult_WhenSyncSucceeds()
    {
        var tenantId = Guid.NewGuid();
        var syncedAt = DateTime.UtcNow;
        _tenantSyncService.SyncTenantAsync(tenantId, _userId, Arg.Any<CancellationToken>())
            .Returns(new TenantSyncResult(
                TenantId: tenantId,
                Nip: "1234567890",
                Outcome: TenantSyncOutcome.Success,
                FetchedInvoices: 5,
                NewInvoices: 2,
                SyncedAtUtc: syncedAt));

        var controller = CreateController();
        var result = await controller.SyncNow(tenantId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<TenantManualSyncResponse>(ok.Value);
        Assert.Equal(tenantId, payload.TenantId);
        Assert.Equal(5, payload.FetchedInvoices);
        Assert.Equal(2, payload.NewInvoices);
        Assert.Equal(syncedAt, payload.SyncedAtUtc);
    }

    public void Dispose() => _db.Dispose();
}
