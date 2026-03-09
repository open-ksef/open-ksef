using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Tests.Services;

public class NipValidatorTests
{
    [Theory]
    [InlineData("5261040828")]
    [InlineData("526-104-08-28")]
    [InlineData("1234567890")]
    public void Should_ReturnTrue_When_NipIsValid(string nip)
    {
        Assert.True(NipValidator.IsValid(nip));
    }

    [Theory]
    [InlineData("123456789")]    // Too short
    [InlineData("12345678901")]  // Too long
    [InlineData("abcdefghij")]   // Letters
    public void Should_ReturnFalse_When_NipIsInvalid(string nip)
    {
        Assert.False(NipValidator.IsValid(nip));
    }

    [Fact]
    public void Should_ThrowArgumentException_When_NipIsNull()
    {
        Assert.ThrowsAny<ArgumentException>(() => NipValidator.IsValid(null!));
    }

    [Fact]
    public void Should_ThrowArgumentException_When_NipIsEmpty()
    {
        Assert.ThrowsAny<ArgumentException>(() => NipValidator.IsValid(""));
    }

    [Theory]
    [InlineData("526-104-08-28", "5261040828")]
    [InlineData("5261040828", "5261040828")]
    [InlineData("123-456-78-90", "1234567890")]
    public void Should_StripDashes_When_Normalizing(string input, string expected)
    {
        Assert.Equal(expected, NipValidator.Normalize(input));
    }
}
