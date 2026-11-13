using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Events;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Push;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Tests.Push;

public sealed class RelayPushProviderTests
{
    private static readonly PushNotification Notification = new() { Title = "t", Body = "b" };

    private static RelayPushProvider Relay(HttpStatusCode status, RelayHealth? health = null)
    {
        var config = Substitute.For<ISystemConfigService>();
        config.GetValue(SystemConfigKeys.PushRelayUrl).Returns("https://relay.example.invalid");
        config.GetValue(SystemConfigKeys.PushRelayApiKey).Returns("key");
        config.GetValue(SystemConfigKeys.PushRelayInstanceId).Returns("instance");
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("push-relay").Returns(_ => new HttpClient(new StubHandler(status)));
        return new RelayPushProvider(factory, config, health ?? new RelayHealth(), NullLogger<RelayPushProvider>.Instance);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, PushResult.Sent)]
    [InlineData(HttpStatusCode.Gone, PushResult.InvalidToken)]
    [InlineData(HttpStatusCode.BadRequest, PushResult.Failed)]
    [InlineData(HttpStatusCode.Unauthorized, PushResult.Failed)]
    [InlineData(HttpStatusCode.Forbidden, PushResult.Failed)]
    [InlineData(HttpStatusCode.NotFound, PushResult.Failed)]
    [InlineData(HttpStatusCode.UnprocessableEntity, PushResult.Failed)]
    public async Task MapsRelayStatus(HttpStatusCode status, PushResult expected) =>
        Assert.Equal(expected, await Relay(status).SendAsync("token-123456789", Platform.Android, Notification));

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task TransientStatus_Throws_SoNotificationServiceRetries(HttpStatusCode status) =>
        await Assert.ThrowsAsync<HttpRequestException>(() => Relay(status).SendAsync("token-123456789", Platform.Android, Notification));

    [Fact]
    public async Task Forbidden_IsRememberedUntilTheNextSuccessfulRelaySend()
    {
        var health = new RelayHealth();

        await Relay(HttpStatusCode.Forbidden, health).SendAsync("token-123456789", Platform.Android, Notification);
        Assert.Equal(403, health.LastFailure?.StatusCode);

        await Relay(HttpStatusCode.Gone, health).SendAsync("token-123456789", Platform.Android, Notification);
        Assert.Equal(403, health.LastFailure?.StatusCode);

        await Relay(HttpStatusCode.OK, health).SendAsync("token-123456789", Platform.Android, Notification);
        Assert.Null(health.LastFailure);
    }

    [Fact]
    public async Task TransientFailure_IsRemembered()
    {
        var health = new RelayHealth();

        await Assert.ThrowsAsync<HttpRequestException>(() => Relay(HttpStatusCode.BadGateway, health).SendAsync("token-123456789", Platform.Android, Notification));

        Assert.Equal(502, health.LastFailure?.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.Gone, false)]
    public async Task ThroughNotificationService_OnlyGoneRemovesTheToken(HttpStatusCode status, bool tokenKept)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.Tenants.Add(new Tenant { Id = tenantId, UserId = "user-1", Nip = "1111111111", CreatedAt = now, UpdatedAt = now });
        db.DeviceTokens.Add(new DeviceToken { Id = Guid.NewGuid(), UserId = "user-1", Token = "fcm-token-relay", Platform = Platform.Android, TenantId = tenantId, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();

        await new NotificationService(db, [Relay(status)], Substitute.For<IEmailService>(), NullLogger<NotificationService>.Instance)
            .NotifyNewInvoiceAsync(new NewInvoiceDetectedEvent(tenantId, Guid.NewGuid(), "Vendor", "FV/1", 123.45m, "PLN"));

        Assert.Equal(tokenKept, await db.DeviceTokens.AnyAsync());
    }

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}") });
    }
}
