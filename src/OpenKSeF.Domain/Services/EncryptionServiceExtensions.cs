using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OpenKSeF.Domain.Services;

public static class EncryptionServiceExtensions
{
    private const string EncryptionKeyEnvVar = "ENCRYPTION_KEY";
    private const string EncryptionKeyFileEnvVar = "ENCRYPTION_KEY_FILE";
    internal const string GenerateKeyHint = "Generate with: openssl rand -base64 32";
    internal const string RestoreKeyHint =
        $"Restore the key that encrypted the credentials, in '{EncryptionKeyEnvVar}' or '{EncryptionKeyFileEnvVar}', and restart. " +
        "Generate a new one only if no copy remains.";

    public static IServiceCollection AddEncryptionService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IEncryptionService>(sp =>
        {
            var rowKey = sp.GetService<ISystemConfigService>()?.GetValue(SystemConfigKeys.EncryptionKey);

            var key = (rowKey is null ? ReadConfiguredKey(configuration, RestoreKeyHint) : DecodeKey(rowKey))
                ?? throw new InvalidOperationException($"Encryption key is not configured. {RestoreKeyHint}");

            return new AesGcmEncryptionService(key);
        });

        return services;
    }

    public static byte[]? ReadConfiguredKey(IConfiguration configuration, string fixHint = GenerateKeyHint)
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

        return DecodeKey(keyBase64, fixHint);
    }

    public static byte[] DecodeKey(string keyBase64, string fixHint = GenerateKeyHint)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(keyBase64);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"Encryption key is not valid base64. {fixHint}".TrimEnd(), ex);
        }

        if (key.Length != 32)
            throw new InvalidOperationException($"Encryption key must decode to exactly 32 bytes (got {key.Length}). {fixHint}".TrimEnd());

        return key;
    }
}
