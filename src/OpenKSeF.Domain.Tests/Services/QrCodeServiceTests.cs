using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Tests.Services;

public class QrCodeServiceTests
{
    private readonly QrCodeService _sut = new();

    [Fact]
    public void BuildZbpPayload_ProducesEightSegments()
    {
        var data = MakeTransferData();
        var payload = _sut.BuildZbpPayload(data);

        var segments = payload.Split('|');
        Assert.Equal(9, segments.Length);
        Assert.Empty(segments[0]);
        Assert.Equal("PL", segments[1]);
    }

    [Fact]
    public void BuildZbpPayload_WithAccount_Includes26DigitNrb()
    {
        var data = MakeTransferData(account: "12345678901234567890123456");
        var payload = _sut.BuildZbpPayload(data);

        var segments = payload.Split('|');
        Assert.Equal("12345678901234567890123456", segments[2]);
    }

    [Fact]
    public void BuildZbpPayload_WithoutAccount_EmptySegment()
    {
        var data = MakeTransferData(account: null);
        var payload = _sut.BuildZbpPayload(data);

        var segments = payload.Split('|');
        Assert.Empty(segments[2]);
    }

    [Fact]
    public void BuildZbpPayload_AmountInGrosze()
    {
        var data = MakeTransferData(amount: 12.34m);
        var payload = _sut.BuildZbpPayload(data);

        var segments = payload.Split('|');
        Assert.Equal("1234", segments[3]);
    }

    [Fact]
    public void BuildZbpPayload_WholeAmount_InGrosze()
    {
        var data = MakeTransferData(amount: 500m);
        var payload = _sut.BuildZbpPayload(data);

        var segments = payload.Split('|');
        Assert.Equal("50000", segments[3]);
    }

    [Fact]
    public void BuildZbpPayload_NameTruncatedTo20()
    {
        var data = MakeTransferData(name: "ABCDEFGHIJ KLMNOPQRST Extra");
        var payload = _sut.BuildZbpPayload(data);

        var segments = payload.Split('|');
        Assert.Equal(20, segments[4].Length);
    }

    [Fact]
    public void BuildZbpPayload_TitleTruncatedTo32()
    {
        var data = MakeTransferData(title: "Faktura 12345678901234567890123456789");
        var payload = _sut.BuildZbpPayload(data);

        var segments = payload.Split('|');
        Assert.Equal(32, segments[5].Length);
    }

    [Fact]
    public void BuildZbpPayload_PolishDiacritics_Transliterated()
    {
        var data = MakeTransferData(name: "Łódź Sp. z o.o.");
        var payload = _sut.BuildZbpPayload(data);

        var segments = payload.Split('|');
        Assert.Equal("Lodz Sp. z o.o.", segments[4]);
    }

    [Fact]
    public void BuildZbpPayload_TrailingSegmentsEmpty()
    {
        var data = MakeTransferData();
        var payload = _sut.BuildZbpPayload(data);

        var segments = payload.Split('|');
        Assert.Empty(segments[6]);
        Assert.Empty(segments[7]);
        Assert.Empty(segments[8]);
    }

    [Fact]
    public void BuildZbpPayload_NoTrailingPipe()
    {
        var data = MakeTransferData();
        var payload = _sut.BuildZbpPayload(data);

        Assert.DoesNotMatch(".*\\|\\|\\|\\|$", payload);
        Assert.EndsWith("|||", payload);
    }

    [Fact]
    public void BuildZbpPayload_MatchesExpectedExample()
    {
        var data = new TransferData
        {
            RecipientName = "ABC SP ZOO",
            RecipientAccount = "12345678901234567890123456",
            Amount = 12m,
            Currency = "PLN",
            Title = "Faktura 12/2026"
        };

        var payload = _sut.BuildZbpPayload(data);
        Assert.Equal("|PL|12345678901234567890123456|1200|ABC SP ZOO|Faktura 122026|||", payload);
    }

    [Fact]
    public void GenerateTransferQr_ProducesValidPng()
    {
        var data = MakeTransferData();
        var bytes = _sut.GenerateTransferQr(data);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal(0x50, bytes[1]);
        Assert.Equal(0x4E, bytes[2]);
        Assert.Equal(0x47, bytes[3]);
    }

    // --- internal helper tests ---

    [Theory]
    [InlineData(0, "0")]
    [InlineData(-5, "0")]
    [InlineData(1, "100")]
    [InlineData(12, "1200")]
    [InlineData(12.34, "1234")]
    [InlineData(12.345, "1235")]
    [InlineData(0.01, "1")]
    [InlineData(9999.99, "999999")]
    public void AmountToGrosze_ConvertsCorrectly(decimal input, string expected)
    {
        Assert.Equal(expected, QrCodeService.AmountToGrosze(input));
    }

    [Theory]
    [InlineData("Łódź", 20, "Lodz")]
    [InlineData("Żółć", 20, "Zolc")]
    [InlineData("ĄĆĘŁŃÓŚŹŻ", 20, "ACELNOSZZ")]
    [InlineData("Normal Text", 20, "Normal Text")]
    [InlineData("Special!@#$%^&*()", 20, "Special")]
    [InlineData("Dots.and-dashes ok", 20, "Dots.and-dashes ok")]
    [InlineData("ABCDEFGHIJ KLMNOPQRSTUVWXYZ", 20, "ABCDEFGHIJ KLMNOPQRS")]
    [InlineData("", 20, "")]
    public void Sanitize_AppliesRulesCorrectly(string input, int maxLen, string expected)
    {
        Assert.Equal(expected, QrCodeService.Sanitize(input, maxLen));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("123", "")]
    [InlineData("12345678901234567890123456", "12345678901234567890123456")]
    [InlineData("1234 5678 9012 3456 7890 1234 56", "12345678901234567890123456")]
    [InlineData("PL12345678901234567890123456", "12345678901234567890123456")]
    [InlineData("PL123456789012345678901234", "")]
    public void SanitizeAccount_ValidatesCorrectly(string? input, string expected)
    {
        Assert.Equal(expected, QrCodeService.SanitizeAccount(input));
    }

    private static TransferData MakeTransferData(
        string name = "Test Vendor",
        string? account = null,
        decimal amount = 100m,
        string title = "Faktura FV-001") =>
        new()
        {
            RecipientName = name,
            RecipientAccount = account,
            RecipientNip = "5261040828",
            Amount = amount,
            Currency = "PLN",
            Title = title
        };
}
