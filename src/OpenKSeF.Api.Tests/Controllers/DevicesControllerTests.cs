using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using OpenKSeF.Api.Controllers;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Tests.Controllers;

public class DevicesControllerTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly INotificationService _notificationService;
    private readonly string _userId = "user-1";

    public DevicesControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);

        _currentUser = Substitute.For<ICurrentUserService>();
        _currentUser.UserId.Returns(_userId);

        _notificationService = Substitute.For<INotificationService>();
    }

    private DevicesController CreateController() =>
        new(_db, _currentUser, _notificationService);

    [Fact]
    public async Task ListDevices_ReturnsOnlyCurrentUserDevices()
    {
        var now = DateTime.UtcNow;
        _db.DeviceTokens.AddRange(
            new DeviceToken { Id = Guid.NewGuid(), UserId = _userId, Token = "tok1", Platform = Platform.Android, CreatedAt = now, UpdatedAt = now },
            new DeviceToken { Id = Guid.NewGuid(), UserId = _userId, Token = "tok2", Platform = Platform.iOS, CreatedAt = now, UpdatedAt = now },
            new DeviceToken { Id = Guid.NewGuid(), UserId = "other-user", Token = "tok3", Platform = Platform.Android, CreatedAt = now, UpdatedAt = now }
        );
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.ListDevices() as OkObjectResult;

        var devices = Assert.IsAssignableFrom<IEnumerable<DeviceTokenResponse>>(result!.Value);
        Assert.Equal(2, devices.Count());
        Assert.All(devices, d => Assert.NotEqual("tok3", d.Token));
    }

    [Fact]
    public async Task ListDevices_NoDevices_ReturnsEmptyList()
    {
        var controller = CreateController();
        var result = await controller.ListDevices() as OkObjectResult;

        var devices = Assert.IsAssignableFrom<IEnumerable<DeviceTokenResponse>>(result!.Value);
        Assert.Empty(devices);
    }

    [Fact]
    public async Task Register_CreatesNewDeviceToken()
    {
        var controller = CreateController();
        var request = new RegisterDeviceRequest("new-token-abc", Platform.Android, null);

        var result = await controller.Register(request);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, await _db.DeviceTokens.CountAsync());

        var device = await _db.DeviceTokens.SingleAsync();
        Assert.Equal("new-token-abc", device.Token);
        Assert.Equal(Platform.Android, device.Platform);
        Assert.Equal(_userId, device.UserId);
        Assert.Null(device.TenantId);
    }

    [Fact]
    public async Task Register_UpsertsExistingToken()
    {
        var now = DateTime.UtcNow;
        _db.DeviceTokens.Add(new DeviceToken
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Token = "existing-token",
            Platform = Platform.Android,
            TenantId = null,
            CreatedAt = now,
            UpdatedAt = now
        });
        await _db.SaveChangesAsync();

        var tenantId = Guid.NewGuid();
        _db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            UserId = _userId,
            Nip = "1234567890",
            CreatedAt = now
        });
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var request = new RegisterDeviceRequest("existing-token", Platform.iOS, tenantId);

        var result = await controller.Register(request);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, await _db.DeviceTokens.CountAsync());

        var device = await _db.DeviceTokens.SingleAsync();
        Assert.Equal(Platform.iOS, device.Platform);
        Assert.Equal(tenantId, device.TenantId);
        Assert.True(device.UpdatedAt > now);
    }

    [Fact]
    public async Task Register_WithInvalidTenantId_ReturnsBadRequest()
    {
        var controller = CreateController();
        var request = new RegisterDeviceRequest("some-token", Platform.Android, Guid.NewGuid());

        var result = await controller.Register(request);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, await _db.DeviceTokens.CountAsync());
    }

    [Fact]
    public async Task Register_WithOtherUserTenant_ReturnsBadRequest()
    {
        var now = DateTime.UtcNow;
        var otherTenantId = Guid.NewGuid();
        _db.Tenants.Add(new Tenant
        {
            Id = otherTenantId,
            UserId = "other-user",
            Nip = "9999999999",
            CreatedAt = now
        });
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var request = new RegisterDeviceRequest("some-token", Platform.Android, otherTenantId);

        var result = await controller.Register(request);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Unregister_RemovesExistingToken()
    {
        var now = DateTime.UtcNow;
        _db.DeviceTokens.Add(new DeviceToken
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Token = "token-to-remove",
            Platform = Platform.Android,
            CreatedAt = now,
            UpdatedAt = now
        });
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.Unregister("token-to-remove");

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(0, await _db.DeviceTokens.CountAsync());
    }

    [Fact]
    public async Task Unregister_NonexistentToken_ReturnsNotFound()
    {
        var controller = CreateController();
        var result = await controller.Unregister("nonexistent-token");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Register_CallsSendConfirmationAsync()
    {
        var controller = CreateController();
        var request = new RegisterDeviceRequest("confirm-token", Platform.Android, null);

        await controller.Register(request);

        _ = _notificationService.Received(1).SendConfirmationAsync("confirm-token");
    }

    [Fact]
    public async Task TestNotification_ExistingDevice_ReturnsSuccess()
    {
        var now = DateTime.UtcNow;
        _db.DeviceTokens.Add(new DeviceToken
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Token = "test-push-token",
            Platform = Platform.Android,
            CreatedAt = now,
            UpdatedAt = now
        });
        await _db.SaveChangesAsync();

        _notificationService.SendTestNotificationAsync("test-push-token")
            .Returns(true);

        var controller = CreateController();
        var result = await controller.TestNotification("test-push-token") as OkObjectResult;

        Assert.NotNull(result);
    }

    [Fact]
    public async Task TestNotification_NonexistentDevice_ReturnsNotFound()
    {
        var controller = CreateController();
        var result = await controller.TestNotification("no-such-token");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task TestNotification_OtherUserDevice_ReturnsNotFound()
    {
        var now = DateTime.UtcNow;
        _db.DeviceTokens.Add(new DeviceToken
        {
            Id = Guid.NewGuid(),
            UserId = "other-user",
            Token = "other-user-token",
            Platform = Platform.iOS,
            CreatedAt = now,
            UpdatedAt = now
        });
        await _db.SaveChangesAsync();

        var controller = CreateController();
        var result = await controller.TestNotification("other-user-token");

        Assert.IsType<NotFoundResult>(result);
    }

    public void Dispose() => _db.Dispose();
}
