using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OpenKSeF.Domain.Services;

public static class EncryptionServiceExtensions
{
    private const string EncryptionKeyEnvVar = "ENCRYPTION_KEY";
    private const string EncryptionKeyFileEnvVar = "ENCRYPTION_KEY_FILE";

    public static IServiceCollection AddEncryptionService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IEncryptionService>(sp =>
        {
            var rowKey = sp.GetService<ISystemConfigService>()?.GetValue(SystemConfigKeys.EncryptionKey);

            var key = (rowKey is null ? ReadConfiguredKey(configuration) : DecodeKey(rowKey))
                ?? throw new InvalidOperationException(
                    $"Encryption key is not configured. Set '{EncryptionKeyEnvVar}' or '{EncryptionKeyFileEnvVar}'. " +
                    "Generate one with: openssl rand -base64 32");

            return new AesGcmEncryptionService(key);
        });

        return services;
    }

    public static byte[]? ReadConfiguredKey(IConfiguration configuration)
    {
        var keyBase64 = configuration[EncryptionKeyEnvVar];
        if (string.IsNullOrWhiteSpace(keyBase64))
        {
            var path = configuration[EncryptionKeyFileEnvVar];
            if (string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                keyBase64 = File.ReadAllText(path).Trim();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"'{EncryptionKeyFileEnvVar}' cannot be read.", ex);
            }

            if (keyBase64.Length == 0)
                return null;
        }

        return DecodeKey(keyBase64);
    }

    public static byte[] DecodeKey(string keyBase64)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(keyBase64);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "Encryption key is not valid base64. Generate with: openssl rand -base64 32", ex);
        }

        if (key.Length != 32)
            throw new InvalidOperationException(
                $"Encryption key must decode to exactly 32 bytes (got {key.Length}). " +
                "Generate with: openssl rand -base64 32");

        return key;
    }
}
