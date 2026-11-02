using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Tests.Extensions;

public class EncryptionServiceExtensionsTests
{
    private const string ValidKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string OtherKey = "ZmVkY2JhOTg3NjU0MzIxMGZlZGNiYTk4NzY1NDMyMTA=";

    [Fact]
    public void Resolve_Throws_WhenNoKeyInDbOrConfig()
    {
        var provider = BuildProvider(dbKey: null, envKey: null);

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IEncryptionService>());
        Assert.Contains("ENCRYPTION_KEY", ex.Message);
    }

    [Fact]
    public void Resolve_UsesEnvKey_WhenDbHasNone()
    {
        var provider = BuildProvider(dbKey: null, envKey: ValidKey);

        var encryption = provider.GetRequiredService<IEncryptionService>();

        Assert.Equal("secret", encryption.Decrypt(encryption.Encrypt("secret")));
    }

    [Fact]
    public void Resolve_UsesDbKey()
    {
        var provider = BuildProvider(dbKey: ValidKey, envKey: null);

        Assert.IsType<AesGcmEncryptionService>(provider.GetRequiredService<IEncryptionService>());
    }

    [Fact]
    public void Resolve_RowBeatsEnv()
    {
        var ciphertext = new AesGcmEncryptionService(Convert.FromBase64String(ValidKey)).Encrypt("secret");
        var provider = BuildProvider(dbKey: ValidKey, envKey: OtherKey);

        Assert.Equal("secret", provider.GetRequiredService<IEncryptionService>().Decrypt(ciphertext));
    }

    private static ServiceProvider BuildProvider(string? dbKey, string? envKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ENCRYPTION_KEY"] = envKey })
            .Build();
        var systemConfig = Substitute.For<ISystemConfigService>();
        systemConfig.GetValue(SystemConfigKeys.EncryptionKey).Returns(dbKey);

        var services = new ServiceCollection();
        services.AddSingleton(systemConfig);
        services.AddEncryptionService(configuration);
        return services.BuildServiceProvider();
    }
}
