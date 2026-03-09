using System.Security.Cryptography;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Tests.Services;

public class AesGcmEncryptionServiceTests
{
    private static byte[] GenerateKey() => RandomNumberGenerator.GetBytes(32);

    private static AesGcmEncryptionService CreateService(byte[]? key = null)
        => new(key ?? GenerateKey());

    [Fact]
    public void Should_RoundTripSuccessfully_When_PlaintextProvided()
    {
        var service = CreateService();
        const string plaintext = "my-secret-ksef-token-12345";

        var encrypted = service.Encrypt(plaintext);
        var decrypted = service.Decrypt(encrypted);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Should_RoundTripSuccessfully_When_PlaintextContainsUnicode()
    {
        var service = CreateService();
        const string plaintext = "token-with-polish-chars: \u0105\u0107\u0119\u0142\u00f3\u015b\u017c\u017a\u0144";

        var encrypted = service.Encrypt(plaintext);
        var decrypted = service.Decrypt(encrypted);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Should_RoundTripSuccessfully_When_PlaintextIsEmpty()
    {
        var service = CreateService();

        var encrypted = service.Encrypt(string.Empty);
        var decrypted = service.Decrypt(encrypted);

        Assert.Equal(string.Empty, decrypted);
    }

    [Fact]
    public void Should_ProduceDifferentCiphertexts_When_SamePlaintextEncryptedTwice()
    {
        var service = CreateService();
        const string plaintext = "same-token";

        var encrypted1 = service.Encrypt(plaintext);
        var encrypted2 = service.Encrypt(plaintext);

        Assert.NotEqual(encrypted1, encrypted2);
    }

    [Fact]
    public void Should_DecryptWithSameKey_When_EncryptedWithThatKey()
    {
        var key = GenerateKey();
        var encryptor = new AesGcmEncryptionService(key);
        var decryptor = new AesGcmEncryptionService(key);
        const string plaintext = "cross-instance-test";

        var encrypted = encryptor.Encrypt(plaintext);
        var decrypted = decryptor.Decrypt(encrypted);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Should_ThrowCryptographicException_When_DecryptedWithWrongKey()
    {
        var service1 = CreateService();
        var service2 = CreateService();

        var encrypted = service1.Encrypt("secret");

        Assert.ThrowsAny<CryptographicException>(() => service2.Decrypt(encrypted));
    }

    [Fact]
    public void Should_ThrowCryptographicException_When_CiphertextIsTampered()
    {
        var service = CreateService();
        var encrypted = service.Encrypt("secret");

        var bytes = Convert.FromBase64String(encrypted);
        bytes[^1] ^= 0xFF; // Flip last byte
        var tampered = Convert.ToBase64String(bytes);

        Assert.ThrowsAny<CryptographicException>(() => service.Decrypt(tampered));
    }

    [Fact]
    public void Should_ThrowCryptographicException_When_CiphertextIsInvalidBase64()
    {
        var service = CreateService();

        Assert.Throws<CryptographicException>(() => service.Decrypt("not-valid-base64!!!"));
    }

    [Fact]
    public void Should_ThrowCryptographicException_When_CiphertextIsTooShort()
    {
        var service = CreateService();
        var tooShort = Convert.ToBase64String(new byte[10]);

        Assert.Throws<CryptographicException>(() => service.Decrypt(tooShort));
    }

    [Fact]
    public void Should_ThrowArgumentException_When_KeyIsWrongSize()
    {
        Assert.Throws<ArgumentException>(() => new AesGcmEncryptionService(new byte[16]));
        Assert.Throws<ArgumentException>(() => new AesGcmEncryptionService(new byte[64]));
    }

    [Fact]
    public void Should_ThrowArgumentNullException_When_KeyIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new AesGcmEncryptionService(null!));
    }

    [Fact]
    public void Should_ThrowArgumentNullException_When_PlaintextIsNull()
    {
        var service = CreateService();
        Assert.Throws<ArgumentNullException>(() => service.Encrypt(null!));
    }

    [Fact]
    public void Should_ThrowArgumentNullException_When_CiphertextIsNull()
    {
        var service = CreateService();
        Assert.Throws<ArgumentNullException>(() => service.Decrypt(null!));
    }
}
