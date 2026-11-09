using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenKSeF.Api.Hubs;
using OpenKSeF.Api.Push;
using OpenKSeF.Domain.Models;
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
        var config = Substitute.For<ISystemConfigService>();
        config.GetValue(SystemConfigKeys.PushRelayUrl).Returns(relayUrl);
        config.GetValue(SystemConfigKeys.PushRelayApiKey).Returns(relayKey);
        config.GetValue(SystemConfigKeys.FirebaseCredentialsJson).Returns(firebaseJson);
        var proxy = Substitute.For<IClientProxy>();
        var hub = Substitute.For<IHubContext<NotificationHub>>();
        hub.Clients.User("user-1").Returns(proxy);
        var provider = new SignalRPushProvider(hub, config, NullLogger<SignalRPushProvider>.Instance);

        var sent = await provider.SendToUserAsync("user-1", new PushNotification { Title = "T", Body = "B" });

        Assert.True(sent);
        var args = (object?[])proxy.ReceivedCalls().Single().GetArguments()[1]!;
        var frame = JsonSerializer.SerializeToElement(args[0], new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(expected, frame.GetProperty("nativePush").GetBoolean());
    }
}
