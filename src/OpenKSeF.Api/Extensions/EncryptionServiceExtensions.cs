using System.Security.Cryptography;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Extensions;

public static class EncryptionServiceExtensions
{
    private const string EncryptionKeyEnvVar = "ENCRYPTION_KEY";

    public static IServiceCollection AddEncryptionService(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddSingleton<IEncryptionService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<AesGcmEncryptionService>>();
            var systemConfig = sp.GetService<ISystemConfigService>();

            var keyBase64 = systemConfig?.GetValue(SystemConfigKeys.EncryptionKey)
                            ?? configuration[EncryptionKeyEnvVar];

            byte[] key;
            if (string.IsNullOrEmpty(keyBase64))
            {
                if (!environment.IsDevelopment())
                    throw new InvalidOperationException(
                        $"Encryption key is not configured. Run the admin setup wizard or set '{EncryptionKeyEnvVar}'. " +
                        "Generate one with: openssl rand -base64 32");

                key = RandomNumberGenerator.GetBytes(32);
                logger.LogWarning(
                    "Encryption key not set — using ephemeral key. " +
                    "Run the admin setup wizard at /admin-setup or set {EnvVar}",
                    EncryptionKeyEnvVar);
            }
            else
            {
                try
                {
                    key = Convert.FromBase64String(keyBase64);
                }
                catch (FormatException ex)
                {
                    throw new InvalidOperationException(
                        $"Encryption key is not valid base64. Generate with: openssl rand -base64 32", ex);
                }

                if (key.Length != 32)
                    throw new InvalidOperationException(
                        $"Encryption key must decode to exactly 32 bytes (got {key.Length}). " +
                        "Generate with: openssl rand -base64 32");
            }

            return new AesGcmEncryptionService(key);
        });

        return services;
    }
}
