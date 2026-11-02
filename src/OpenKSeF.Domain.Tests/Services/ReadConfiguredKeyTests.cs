using Microsoft.Extensions.Configuration;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Tests.Services;

public class ReadConfiguredKeyTests : IDisposable
{
    private const string ValidKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string UnpaddedKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY";

    private readonly string _keyFile = Path.GetTempFileName();

    public void Dispose() => File.Delete(_keyFile);

    private static IConfiguration Config(string? key = null, string? file = null) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ENCRYPTION_KEY"] = key, ["ENCRYPTION_KEY_FILE"] = file })
        .Build();

    [Fact]
    public void ReadsEnv()
    {
        Assert.Equal(Convert.FromBase64String(ValidKey), EncryptionServiceExtensions.ReadConfiguredKey(Config(ValidKey)));
    }

    [Fact]
    public void ReadsFile_WithTrailingNewline()
    {
        File.WriteAllText(_keyFile, ValidKey + "\n");

        Assert.Equal(Convert.FromBase64String(ValidKey), EncryptionServiceExtensions.ReadConfiguredKey(Config(file: _keyFile)));
    }

    [Fact]
    public void EmptyEnv_FallsBackToFile()
    {
        File.WriteAllText(_keyFile, ValidKey);

        Assert.Equal(Convert.FromBase64String(ValidKey), EncryptionServiceExtensions.ReadConfiguredKey(Config("", _keyFile)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Empty_IsUnset(string? key)
    {
        Assert.Null(EncryptionServiceExtensions.ReadConfiguredKey(Config(key)));
    }

    [Fact]
    public void EmptyFile_IsUnset()
    {
        File.WriteAllText(_keyFile, "\n");

        Assert.Null(EncryptionServiceExtensions.ReadConfiguredKey(Config(file: _keyFile)));
    }

    [Theory]
    [InlineData(UnpaddedKey)]
    [InlineData("generate_with_openssl_rand")]
    [InlineData("c2hvcnQ=")]
    public void Malformed_Throws_WithoutTheValue(string key)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => EncryptionServiceExtensions.ReadConfiguredKey(Config(key)));

        Assert.DoesNotContain(key, ex.ToString());
    }

    [Fact]
    public void MissingFile_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            EncryptionServiceExtensions.ReadConfiguredKey(Config(file: _keyFile + ".missing")));
    }
}
