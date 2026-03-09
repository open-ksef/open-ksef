using System.Security.Cryptography;

namespace OpenKSeF.Domain.Services;

public sealed class AesGcmEncryptionService : IEncryptionService
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int KeySizeBytes = 32;

    private readonly byte[] _key;

    public AesGcmEncryptionService(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeySizeBytes)
            throw new ArgumentException($"Encryption key must be exactly {KeySizeBytes} bytes.", nameof(key));

        _key = key;
    }

    public string Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSizeBytes];

        using var aes = new AesGcm(_key, TagSizeBytes);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Layout: [nonce (12)] [tag (16)] [ciphertext (N)]
        var result = new byte[NonceSizeBytes + TagSizeBytes + ciphertext.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, NonceSizeBytes);
        ciphertext.CopyTo(result, NonceSizeBytes + TagSizeBytes);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);

        byte[] combined;
        try
        {
            combined = Convert.FromBase64String(ciphertext);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Invalid ciphertext format.", ex);
        }

        if (combined.Length < NonceSizeBytes + TagSizeBytes)
            throw new CryptographicException("Ciphertext is too short to contain nonce and tag.");

        var nonce = combined.AsSpan(0, NonceSizeBytes);
        var tag = combined.AsSpan(NonceSizeBytes, TagSizeBytes);
        var encryptedData = combined.AsSpan(NonceSizeBytes + TagSizeBytes);

        var plaintext = new byte[encryptedData.Length];

        using var aes = new AesGcm(_key, TagSizeBytes);
        aes.Decrypt(nonce, encryptedData, tag, plaintext);

        return System.Text.Encoding.UTF8.GetString(plaintext);
    }
}
