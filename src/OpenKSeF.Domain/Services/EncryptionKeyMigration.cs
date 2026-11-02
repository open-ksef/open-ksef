using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.Data;

namespace OpenKSeF.Domain.Services;

/// <summary>Moves the AES key out of SystemConfigs and rotates it. While the row exists, all ciphertext is under the row key.</summary>
public static class EncryptionKeyMigration
{
    private const string NewKeyEnvVar = "ENCRYPTION_KEY_NEW";
    private const string FixHint =
        "Set ENCRYPTION_KEY or ENCRYPTION_KEY_FILE to a key from 'openssl rand -base64 32' and restart.";

    public static async Task AdoptConfiguredKeyAsync(
        ApplicationDbContext db,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken ct = default)
    {
        try
        {
            var row = await db.SystemConfigs.FirstOrDefaultAsync(c => c.Key == SystemConfigKeys.EncryptionKey, ct);
            if (row is null)
            {
                await CheckConfiguredKeyAsync(db, configuration, logger, ct);
                return;
            }

            var configuredKey = TryReadConfiguredKey(configuration);
            if (configuredKey is null)
            {
                logger.LogError(
                    "The encryption key is still stored in the database, and the configured key is missing or invalid. {FixHint}",
                    FixHint);
                return;
            }

            var count = await ReEncryptTrackedAsync(db, EncryptionServiceExtensions.DecodeKey(row.Value), configuredKey, ct);
            db.SystemConfigs.Remove(row);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Encryption key moved out of the database, {Count} credentials re-encrypted", count);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation("Encryption key was moved out of the database by another host");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex,
                "Moving the encryption key out of the database failed, nothing changed. " +
                "If a credential is named here, delete or re-enter it in the portal, then restart.");
        }
    }

    public static async Task<int> RotateAsync(
        ApplicationDbContext db,
        byte[] currentKey,
        byte[] newKey,
        CancellationToken ct = default)
    {
        var count = await ReEncryptTrackedAsync(db, currentKey, newKey, ct);
        await db.SaveChangesAsync(ct);
        return count;
    }

    public static async Task<int> RunRotateCommandAsync(
        ApplicationDbContext db,
        IConfiguration configuration,
        TextWriter output,
        CancellationToken ct = default)
    {
        var newKeyBase64 = configuration[NewKeyEnvVar];
        if (string.IsNullOrWhiteSpace(newKeyBase64))
        {
            await output.WriteLineAsync($"Refused: {NewKeyEnvVar} is not set.");
            return 1;
        }

        byte[]? currentKey;
        byte[] newKey;
        try
        {
            currentKey = EncryptionServiceExtensions.ReadConfiguredKey(configuration);
            newKey = EncryptionServiceExtensions.DecodeKey(newKeyBase64);
        }
        catch (InvalidOperationException ex)
        {
            await output.WriteLineAsync($"Refused: {ex.Message}");
            return 1;
        }

        if (currentKey is null)
        {
            await output.WriteLineAsync("Refused: ENCRYPTION_KEY is not set.");
            return 1;
        }

        if (currentKey.SequenceEqual(newKey))
        {
            await output.WriteLineAsync($"Refused: {NewKeyEnvVar} equals the current key.");
            return 1;
        }

        if (await db.SystemConfigs.AnyAsync(c => c.Key == SystemConfigKeys.EncryptionKey, ct))
        {
            await output.WriteLineAsync("Refused: the key is still in the database. Start the app once to adopt it.");
            return 1;
        }

        try
        {
            var count = await RotateAsync(db, currentKey, newKey, ct);
            await output.WriteLineAsync($"Re-encrypted {count} credentials.");
            return 0;
        }
        catch (CryptographicException ex)
        {
            await output.WriteLineAsync($"Failed, nothing written: {ex.Message}");
            return 1;
        }
    }

    private static async Task CheckConfiguredKeyAsync(
        ApplicationDbContext db,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken ct)
    {
        var initialized = await db.SystemConfigs.AsNoTracking()
            .Where(c => c.Key == SystemConfigKeys.IsInitialized)
            .Select(c => c.Value)
            .FirstOrDefaultAsync(ct);
        if (!string.Equals(initialized, "true", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("System not initialized, encryption key check skipped");
            return;
        }

        var configuredKey = TryReadConfiguredKey(configuration);
        if (configuredKey is null)
        {
            logger.LogCritical(
                "The configured encryption key is missing or invalid. Adding credentials, sync and send fail. {FixHint}",
                FixHint);
            return;
        }

        var encryption = new AesGcmEncryptionService(configuredKey);
        var credentials = await db.KSeFCredentials.AsNoTracking().ToListAsync(ct);
        var failedIds = credentials
            .Where(c => !CanDecrypt(encryption, c.EncryptedToken) || !CanDecrypt(encryption, c.EncryptedCertificateData))
            .Select(c => c.Id)
            .ToList();
        if (failedIds.Count > 0)
            logger.LogCritical(
                "The configured encryption key does not decrypt {Count} credentials: {CredentialIds}. Restore the key that encrypted them.",
                failedIds.Count, failedIds);
    }

    private static byte[]? TryReadConfiguredKey(IConfiguration configuration)
    {
        try
        {
            return EncryptionServiceExtensions.ReadConfiguredKey(configuration);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool CanDecrypt(AesGcmEncryptionService encryption, string? ciphertext)
    {
        if (ciphertext is null)
            return true;

        try
        {
            encryption.Decrypt(ciphertext);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static async Task<int> ReEncryptTrackedAsync(
        ApplicationDbContext db,
        byte[] currentKey,
        byte[] newKey,
        CancellationToken ct)
    {
        var current = new AesGcmEncryptionService(currentKey);
        var next = new AesGcmEncryptionService(newKey);
        var count = 0;

        foreach (var credential in await db.KSeFCredentials.ToListAsync(ct))
        {
            var token = ReEncrypt(credential.EncryptedToken, credential.Id, current, next);
            var certificate = ReEncrypt(credential.EncryptedCertificateData, credential.Id, current, next);
            if (token != credential.EncryptedToken || certificate != credential.EncryptedCertificateData)
                count++;

            credential.EncryptedToken = token;
            credential.EncryptedCertificateData = certificate;
        }

        return count;
    }

    private static string? ReEncrypt(
        string? ciphertext,
        Guid credentialId,
        AesGcmEncryptionService current,
        AesGcmEncryptionService next)
    {
        if (ciphertext is null)
            return null;

        string plaintext;
        try
        {
            plaintext = current.Decrypt(ciphertext);
        }
        catch (CryptographicException)
        {
            // Already under the new key: a re-run after a partial rotation, or a host that lost the adoption race.
            if (CanDecrypt(next, ciphertext))
                return ciphertext;

            throw new CryptographicException($"Credential {credentialId} cannot be decrypted.");
        }

        var result = next.Encrypt(plaintext);
        if (next.Decrypt(result) != plaintext)
            throw new CryptographicException($"Credential {credentialId} failed re-encryption verification.");

        return result;
    }
}
