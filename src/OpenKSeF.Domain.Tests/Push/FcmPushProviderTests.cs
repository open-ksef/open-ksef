using System.Security.Cryptography;
using System.Text.Json;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Push;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Tests.Push;

public sealed class FcmPushProviderTests : IDisposable
{
    public void Dispose() => FirebaseApp.DefaultInstance?.Delete();

    [Fact]
    public async Task FirstPush_InitializesFirebaseFromSystemConfig_DbRowWinsOverEnv()
    {
        using var provider = new ServiceCollection()
            .AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()), ServiceLifetime.Transient)
            .BuildServiceProvider();
        var config = new SystemConfigService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Firebase:CredentialsJson"] = ServiceAccountJson("env-project"),
            }).Build(),
            NullLogger<SystemConfigService>.Instance);
        await config.SetValueAsync(SystemConfigKeys.FirebaseCredentialsJson, ServiceAccountJson("db-project"));
        var fcm = new FcmPushProvider(config, NullLogger<FcmPushProvider>.Instance);

        Assert.Null(FirebaseApp.DefaultInstance);
        Assert.True(fcm.TryInitializeFirebase());

        var credential = (ServiceAccountCredential)FirebaseApp.DefaultInstance!.Options.Credential.UnderlyingCredential;
        Assert.Equal("db-project", credential.ProjectId);
    }

    [Fact]
    public async Task BadCredentialsJson_LogsOncePerProcess()
    {
        var config = Substitute.For<ISystemConfigService>();
        config.GetValue(SystemConfigKeys.FirebaseCredentialsJson).Returns($"{{\"not-a-service-account\":\"{Guid.NewGuid()}\"}}");
        var logger = Substitute.For<ILogger<FcmPushProvider>>();
        var notification = new PushNotification { Title = "t", Body = "b" };

        for (var i = 0; i < 3; i++)
            Assert.Equal(PushResult.Skipped, await new FcmPushProvider(config, logger).SendAsync("token", Platform.Android, notification));

        Assert.Single(logger.ReceivedCalls(), c => c.GetMethodInfo().Name == nameof(ILogger.Log) && (LogLevel)c.GetArguments()[0]! == LogLevel.Warning);
        Assert.Null(FirebaseApp.DefaultInstance);
    }

    private static string ServiceAccountJson(string projectId)
    {
        using var rsa = RSA.Create(2048);
        return JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["type"] = "service_account",
            ["project_id"] = projectId,
            ["private_key_id"] = "test-key",
            ["private_key"] = rsa.ExportPkcs8PrivateKeyPem(),
            ["client_email"] = $"push@{projectId}.iam.gserviceaccount.com",
            ["client_id"] = "1",
            ["token_uri"] = "http://127.0.0.1:1/token",
        });
    }
}
