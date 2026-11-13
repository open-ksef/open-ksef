using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenKSeF.Api.Hubs;
using OpenKSeF.Api.Push;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Push;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Tests.Push;

public class SignalRPushProviderTests
{
    [Theory]
    [InlineData(null, null, null, false)]
    [InlineData("https://relay.test", "key", null, true)]
    [InlineData("https://relay.test", null, null, false)]
    [InlineData(null, "key", null, false)]
    [InlineData(null, null, "{}", true)]
    [InlineData("https://relay.test", "key", "{}", true)]
    public async Task SendToUserAsync_NativePushReflectsRelayOrFirebaseConfig(
        string? relayUrl, string? relayKey, string? firebaseJson, bool expected)
    {
        var config = Config(relayUrl, relayKey, firebaseJson);

        Assert.Equal(expected, await NativePush(config, new RelayHealth()));
    }

    [Fact]
    public async Task RejectedFirebaseCredentials_WithoutRelay_NativePushFalseFromTheNextEvent()
    {
        var config = Config(null, null, $"{{\"not-a-service-account\":\"{Guid.NewGuid()}\"}}");
        var health = new RelayHealth();

        Assert.True(await NativePush(config, health));
        await new FcmPushProvider(config, NullLogger<FcmPushProvider>.Instance)
            .SendAsync("token", Platform.Android, new PushNotification { Title = "T", Body = "B" });

        Assert.False(await NativePush(config, health));
    }

    [Fact]
    public async Task RelayFailure_NativePushFalseUntilARelaySendSucceeds()
    {
        var config = Config("https://relay.test", "key", null);
        var health = new RelayHealth();

        health.Failed(403);
        Assert.False(await NativePush(config, health));

        health.Succeeded();
        Assert.True(await NativePush(config, health));
    }

    private static ISystemConfigService Config(string? relayUrl, string? relayKey, string? firebaseJson)
    {
        var config = Substitute.For<ISystemConfigService>();
        config.GetValue(SystemConfigKeys.PushRelayUrl).Returns(relayUrl);
        config.GetValue(SystemConfigKeys.PushRelayApiKey).Returns(relayKey);
        config.GetValue(SystemConfigKeys.FirebaseCredentialsJson).Returns(firebaseJson);
        return config;
    }

    private static async Task<bool> NativePush(ISystemConfigService config, RelayHealth health)
    {
        var proxy = Substitute.For<IClientProxy>();
        var hub = Substitute.For<IHubContext<NotificationHub>>();
        hub.Clients.User("user-1").Returns(proxy);
        var provider = new SignalRPushProvider(hub, config, health, NullLogger<SignalRPushProvider>.Instance);

        Assert.True(await provider.SendToUserAsync("user-1", new PushNotification { Title = "T", Body = "B" }));

        var args = (object?[])proxy.ReceivedCalls().Single().GetArguments()[1]!;
        var frame = JsonSerializer.SerializeToElement(args[0], new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return frame.GetProperty("nativePush").GetBoolean();
    }
}
