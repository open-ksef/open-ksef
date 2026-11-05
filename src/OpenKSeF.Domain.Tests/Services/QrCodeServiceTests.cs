using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;
using QRCoder;

namespace OpenKSeF.Domain.Tests.Services;

public class QrCodeServiceTests
{
    private const string ValidNrb = "61109010140000071219812874";

    private readonly QrCodeService _sut = new();

    [Fact]
    public void BuildZbpPayload_ProducesEightSegments()
    {
        var data = MakeTransferData();
        var payload = _sut.BuildZbpPayload(data);

        var segments = payload.Split('|');
        Assert.Equal(9, segments.Length);
        Assert.Equal("5261040828", segments[0]);
        Assert.Equal("PL", segments[1]);
    }

    [Fact]
    public void BuildZbpPayload_WithAccount_Includes26DigitNrb()
    {
        var data = MakeTransferData(account: ValidNrb);
        var payload = _sut.BuildZbpPayload(data);

        var segments = payload.Split('|');
        Assert.Equal(ValidNrb, segments[2]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345678901234567890123456")]
    public void BuildZbpPayload_WithoutValidAccount_Throws(string? account)
    {
        var data = MakeTransferData(account: account);

        Assert.Throws<InvalidOperationException>(() => _sut.BuildZbpPayload(data));
    }

    [Fact]
    public void BuildZbpPayload_AmountInGrosze()
    {
        var data = MakeTransferData(amount: 12.34m);
        var payload = _sut.BuildZbpPayload(data);

        var segments = payload.Split('|');
        Assert.Equal("001234", segments[3]);
    }

    [Fact]
    public void BuildZbpPayload_WholeAmount_InGrosze()
    {
        var data = MakeTransferData(amount: 500m);
        var payload = _sut.BuildZbpPayload(data);

        var segments = payload.Split('|');
        Assert.Equal("050000", segments[3]);
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
        var data = MakeTransferData(title: "123456789012345678901234567890123");
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

    [Theory]
    [InlineData(false, "1234567890|PL|92124012340001567890123456|001200|Odbiorca 1|FV 1234/34/2012|||")]
    [InlineData(true, "1234567890|PL|92124012340001567890123456|000000|Odbiorca 1|FV 1234/34/2012|||")]
    public void BuildZbpPayload_MatchesZbpSpecExamples(bool manualAmount, string expected)
    {
        var data = new TransferData
        {
            RecipientNip = "1234567890",
            RecipientName = "Odbiorca 1",
            RecipientAccount = "92124012340001567890123456",
            Amount = 12m,
            Currency = "PLN",
            Title = "FV 1234/34/2012",
            ManualAmount = manualAmount
        };

        Assert.Equal(expected, _sut.BuildZbpPayload(data));
    }

    [Fact]
    public void BuildZbpPayload_MatchesReq0001Example()
    {
        var data = new TransferData
        {
            RecipientNip = "1234567890",
            RecipientName = "Odbiorca 1",
            RecipientAccount = "PL61 1090 1014 0000 0712 1981 2874",
            Amount = 1234.56m,
            Currency = "PLN",
            Title = "FV 1/2026"
        };

        Assert.Equal("1234567890|PL|61109010140000071219812874|123456|Odbiorca 1|FV 1/2026|||", _sut.BuildZbpPayload(data));
    }

    [Theory]
    [InlineData("")]
    [InlineData("123456789")]
    [InlineData("12345678901")]
    [InlineData("PL12345678")]
    public void BuildZbpPayload_NipNotTenDigits_EmptyField(string nip)
    {
        var data = MakeTransferData();
        data.RecipientNip = nip;

        Assert.Empty(_sut.BuildZbpPayload(data).Split('|')[0]);
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

    [Fact]
    public void GenerateTransferQr_UsesErrorCorrectionL()
    {
        var data = MakeTransferData();

        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(_sut.BuildZbpPayload(data), QRCodeGenerator.ECCLevel.L);
        using var png = new PngByteQRCode(qrData);

        Assert.Equal(png.GetGraphic(20), _sut.GenerateTransferQr(data));
    }

    // --- internal helper tests ---

    [Theory]
    [InlineData(0, "000000")]
    [InlineData(-5, "000000")]
    [InlineData(1, "000100")]
    [InlineData(12, "001200")]
    [InlineData(12.34, "001234")]
    [InlineData(12.345, "001235")]
    [InlineData(0.01, "000001")]
    [InlineData(9999.99, "999999")]
    [InlineData(1234567.89, "123456789")]
    public void AmountToGrosze_ConvertsCorrectly(decimal input, string expected)
    {
        Assert.Equal(expected, QrCodeService.AmountToGrosze(input));
    }

    [Theory]
    [InlineData("Łódź", 20, "Lodz")]
    [InlineData("Żółć", 20, "Zolc")]
    [InlineData("ĄĆĘŁŃÓŚŹŻ", 20, "ACELNOSZZ")]
    [InlineData("Normal Text", 20, "Normal Text")]
    [InlineData("Special!@#$%^&*()", 20, "Special@#&*")]
    [InlineData("FV_1,2\\3", 20, "FV_1,2\\3")]
    [InlineData("A|B", 20, "AB")]
    [InlineData("Dots.and-dashes ok", 20, "Dots.and-dashes ok")]
    [InlineData("15445/04/2026", 32, "15445/04/2026")]
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
    [InlineData("12345678901234567890123456", "")]
    [InlineData("1234 5678 9012 3456 7890 1234 56", "")]
    [InlineData("PL12345678901234567890123456", "")]
    [InlineData("PL123456789012345678901234", "")]
    [InlineData("DE61109010140000071219812874", "")]
    [InlineData("6110901014000007121981287X", "")]
    [InlineData("61109010140000071219812875", "")]
    [InlineData("61109010140000071219812874", "61109010140000071219812874")]
    [InlineData("92124012340001567890123456", "92124012340001567890123456")]
    [InlineData("61 1090 1014 0000 0712 1981 2874", "61109010140000071219812874")]
    [InlineData("61-1090-1014-0000-0712-1981-2874", "61109010140000071219812874")]
    [InlineData("PL61 1090 1014 0000 0712 1981 2874", "61109010140000071219812874")]
    [InlineData("pl61109010140000071219812874", "61109010140000071219812874")]
    [InlineData("61\u00A01090\u00A01014\u00A00000\u00A00712\u00A01981\u00A02874", "61109010140000071219812874")]
    public void SanitizeAccount_ValidatesCorrectly(string? input, string expected)
    {
        Assert.Equal(expected, QrCodeService.SanitizeAccount(input));
    }

    private static TransferData MakeTransferData(
        string name = "Test Vendor",
        string? account = ValidNrb,
        decimal amount = 100m,
        string title = "FV-001") =>
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
