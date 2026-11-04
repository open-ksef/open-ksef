using System.Globalization;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Tests.Services;

public class TransferDetailsServiceTests
{
    private const string ValidAccount = "PL61 1090 1014 0000 0712 1981 2874";
    private const string CorrectionMessage = "Faktura korygująca: kwota to różnica. Sprawdź, czy jest coś do zapłaty.";
    private const string TypeMessage = "Nie można ustalić rodzaju faktury. Sprawdź kwotę na fakturze. Aby uzyskać kod QR, uruchom pełną resynchronizację (portal: Firma, zakładka Token KSeF i pobieranie).";
    private const string AccountMessage = "Brak poprawnego numeru rachunku na fakturze.";
    private const string NameOrTitleMessage = "Nie można utworzyć kodu QR: brak czytelnej nazwy sprzedawcy lub numeru faktury.";
    private const string ManualMessage = "Zaliczka/rozliczenie: wpisz kwotę ręcznie.";
    private const string MppMessage = "Sprawdź, czy faktura nie wymaga podzielonej płatności (MPP).";

    private readonly TransferDetailsService _sut = new();
    private readonly QrCodeService _qr = new();

    [Fact]
    public void BuildTransferData_Title_PrefersInvoiceNumber()
    {
        var invoice = MakeInvoice(invoiceNumber: "FV/2026/03/001");
        var result = _sut.BuildTransferData(invoice);
        Assert.Equal("FV/2026/03/001", result.Title);
    }

    [Fact]
    public void BuildTransferData_Title_FallsBackToKSeFNumber()
    {
        var invoice = MakeInvoice(invoiceNumber: null);
        var result = _sut.BuildTransferData(invoice);
        Assert.Equal("9999999999-20260301-ABC123-FF", result.Title);
    }

    [Fact]
    public void BuildTransferData_RecipientAccount_FromVendorBankAccount()
    {
        var invoice = MakeInvoice(vendorBankAccount: "12345678901234567890123456");
        var result = _sut.BuildTransferData(invoice);
        Assert.Equal("12345678901234567890123456", result.RecipientAccount);
    }

    [Fact]
    public void BuildTransferData_RecipientAccount_NullWhenNoBankAccount()
    {
        var invoice = MakeInvoice(vendorBankAccount: null);
        var result = _sut.BuildTransferData(invoice);
        Assert.Null(result.RecipientAccount);
    }

    [Fact]
    public void BuildTransferText_IncludesBankAccountWhenPresent()
    {
        var invoice = MakeInvoice(vendorBankAccount: "12345678901234567890123456");
        var result = _sut.BuildTransferText(invoice);
        Assert.Contains("Nr rachunku: 12345678901234567890123456", result);
    }

    [Fact]
    public void BuildTransferText_OmitsBankAccountWhenNull()
    {
        var invoice = MakeInvoice(vendorBankAccount: null);
        var result = _sut.BuildTransferText(invoice);
        Assert.DoesNotContain("Nr rachunku", result);
    }

    [Fact]
    public void BuildTransferText_Title_UsesShortInvoiceNumber()
    {
        var invoice = MakeInvoice(invoiceNumber: "FV/2026/001");
        var result = _sut.BuildTransferText(invoice);
        Assert.Contains("Tytul: FV/2026/001", result);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("pl-PL")]
    public void BuildTransferText_Amount_PolishUnderAnyServerCulture(string culture)
    {
        var invoice = MakeInvoice(amountGross: 1234.56m);
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            Assert.Contains("\nKwota: 1\u00A0234,56 PLN\n", _sut.BuildTransferText(invoice));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Qr_VatInvoice_PayloadMatchesReq0001Example()
    {
        var invoice = MakeInvoice(invoiceNumber: "FV 1/2026", vendorBankAccount: ValidAccount, amountGross: 1234.56m, vendorName: "Odbiorca 1");

        var data = _sut.BuildTransferData(invoice);

        Assert.True(data.QrAvailable);
        Assert.Null(data.QrMessage);
        Assert.Equal("1234567890|PL|61109010140000071219812874|123456|Odbiorca 1|FV 1/2026|||", _qr.BuildZbpPayload(data));
    }

    [Fact]
    public void Qr_LongSellerName_CutTo20InPayload()
    {
        var invoice = MakeInvoice(invoiceNumber: "FV 1/2026", vendorBankAccount: ValidAccount, amountGross: 1234.56m, vendorName: "Firma Testowa Spółka z o.o.");

        var payload = _qr.BuildZbpPayload(_sut.BuildTransferData(invoice));

        Assert.Equal("1234567890|PL|61109010140000071219812874|123456|Firma Testowa Spolka|FV 1/2026|||", payload);
    }

    [Fact]
    public void Qr_Gross12_AmountField001200()
    {
        var invoice = MakeInvoice(vendorBankAccount: ValidAccount, amountGross: 12m);

        Assert.Equal("001200", _qr.BuildZbpPayload(_sut.BuildTransferData(invoice)).Split('|')[3]);
    }

    [Theory]
    [InlineData("Vat")]
    [InlineData("Upr")]
    [InlineData("VatPef")]
    [InlineData("VatPefSp")]
    [InlineData("VatRr")]
    public void Qr_RegularTypes_EncodeGross(string invoiceType)
    {
        var data = _sut.BuildTransferData(MakeInvoice(vendorBankAccount: ValidAccount, invoiceType: invoiceType));

        Assert.True(data.QrAvailable);
        Assert.Null(data.QrMessage);
        Assert.Equal("012345", _qr.BuildZbpPayload(data).Split('|')[3]);
    }

    [Theory]
    [InlineData("Zal")]
    [InlineData("Roz")]
    [InlineData("zal")]
    public void Qr_ZalRoz_ManualAmount(string invoiceType)
    {
        var data = _sut.BuildTransferData(MakeInvoice(vendorBankAccount: ValidAccount, invoiceType: invoiceType));

        Assert.True(data.QrAvailable);
        Assert.Equal(ManualMessage, data.QrMessage);
        Assert.Equal("000000", _qr.BuildZbpPayload(data).Split('|')[3]);
    }

    [Fact]
    public void Qr_Eur_NoQr_TextKeepsCurrencyAndRawAccount()
    {
        var invoice = MakeInvoice(vendorBankAccount: ValidAccount, currency: "EUR");

        var data = _sut.BuildTransferData(invoice);
        var text = _sut.BuildTransferText(invoice);

        Assert.False(data.QrAvailable);
        Assert.Equal("Faktura w walucie EUR: kod QR obsługuje tylko przelew w PLN.", data.QrMessage);
        Assert.Contains("Kwota: 123,45 EUR", text);
        Assert.Contains("Nr rachunku: " + ValidAccount, text);
    }

    [Theory]
    [InlineData("Kor", 100)]
    [InlineData("Kor", 0)]
    [InlineData("Kor", -100)]
    [InlineData("KorZal", 100)]
    [InlineData("KorRoz", 100)]
    [InlineData("KorPef", 100)]
    [InlineData("KorVatRr", 100)]
    [InlineData("kor", 100)]
    public void Qr_Correction_NoQr(string invoiceType, decimal gross)
    {
        var data = _sut.BuildTransferData(MakeInvoice(vendorBankAccount: ValidAccount, invoiceType: invoiceType, amountGross: gross));

        Assert.False(data.QrAvailable);
        Assert.Equal(CorrectionMessage, data.QrMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Qr_TypeNullOrBlank_NoQr_CopyStillWorks(string? invoiceType)
    {
        var invoice = MakeInvoice(vendorBankAccount: ValidAccount, invoiceType: invoiceType);

        var data = _sut.BuildTransferData(invoice);

        Assert.False(data.QrAvailable);
        Assert.Equal(TypeMessage, data.QrMessage);
        Assert.Contains("Nr rachunku: " + ValidAccount, _sut.BuildTransferText(invoice));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Qr_GrossNotPositive_NoQr(decimal gross)
    {
        var data = _sut.BuildTransferData(MakeInvoice(vendorBankAccount: ValidAccount, amountGross: gross));

        Assert.False(data.QrAvailable);
        Assert.Equal("Brak kwoty do zapłaty.", data.QrMessage);
    }

    [Theory]
    [InlineData("Zal")]
    [InlineData("Roz")]
    public void Qr_ZalRozGrossZero_NoQr(string invoiceType)
    {
        var data = _sut.BuildTransferData(MakeInvoice(vendorBankAccount: ValidAccount, invoiceType: invoiceType, amountGross: 0m));

        Assert.False(data.QrAvailable);
        Assert.Equal("Brak kwoty do zapłaty.", data.QrMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("PL62 1090 1014 0000 0712 1981 2874")]
    [InlineData("DE61109010140000071219812874")]
    public void Qr_NoOrInvalidAccount_NoQr_CopyStillWorks(string? account)
    {
        var invoice = MakeInvoice(vendorBankAccount: account);

        var data = _sut.BuildTransferData(invoice);

        Assert.False(data.QrAvailable);
        Assert.Equal(AccountMessage, data.QrMessage);
        Assert.Contains("Kwota: 123,45 PLN", _sut.BuildTransferText(invoice));
        if (!string.IsNullOrEmpty(account))
            Assert.Contains("Nr rachunku: " + account, _sut.BuildTransferText(invoice));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("ООО Ромашка")]
    public void Qr_NameEmptyAfterSanitizing_NoQr_CopyStillWorks(string vendorName)
    {
        var invoice = MakeInvoice(vendorBankAccount: ValidAccount, vendorName: vendorName);

        var data = _sut.BuildTransferData(invoice);

        Assert.False(data.QrAvailable);
        Assert.Equal(NameOrTitleMessage, data.QrMessage);
        Assert.Contains("Nr rachunku: " + ValidAccount, _sut.BuildTransferText(invoice));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("№№№")]
    public void Qr_TitleEmptyAfterSanitizing_NoQr_CopyStillWorks(string invoiceNumber)
    {
        var invoice = MakeInvoice(invoiceNumber: invoiceNumber, vendorBankAccount: ValidAccount);

        var data = _sut.BuildTransferData(invoice);

        Assert.False(data.QrAvailable);
        Assert.Equal(NameOrTitleMessage, data.QrMessage);
        Assert.Contains("Nr rachunku: " + ValidAccount, _sut.BuildTransferText(invoice));
    }

    [Fact]
    public void Qr_Gross15000_NoMppNotice()
    {
        var data = _sut.BuildTransferData(MakeInvoice(vendorBankAccount: ValidAccount, amountGross: 15000m));

        Assert.True(data.QrAvailable);
        Assert.Null(data.QrMessage);
    }

    [Fact]
    public void Qr_Gross20000_QrWithMppNotice()
    {
        var data = _sut.BuildTransferData(MakeInvoice(vendorBankAccount: ValidAccount, amountGross: 20000m));

        Assert.True(data.QrAvailable);
        Assert.Equal(MppMessage, data.QrMessage);
        Assert.Equal("2000000", _qr.BuildZbpPayload(data).Split('|')[3]);
    }

    [Fact]
    public void Qr_ZalGross20000_BothMessages()
    {
        var data = _sut.BuildTransferData(MakeInvoice(vendorBankAccount: ValidAccount, invoiceType: "Zal", amountGross: 20000m));

        Assert.True(data.QrAvailable);
        Assert.Equal(ManualMessage + " " + MppMessage, data.QrMessage);
        Assert.Equal("000000", _qr.BuildZbpPayload(data).Split('|')[3]);
    }

    [Fact]
    public void Qr_BlockedAboveMppThreshold_NoMppNotice()
    {
        var data = _sut.BuildTransferData(MakeInvoice(vendorBankAccount: null, amountGross: 20000m));

        Assert.Equal(AccountMessage, data.QrMessage);
    }

    [Fact]
    public void Qr_FirstMatchWins_CurrencyBeforeCorrectionAndAccount()
    {
        var data = _sut.BuildTransferData(MakeInvoice(vendorBankAccount: null, invoiceType: "Kor", currency: "EUR"));

        Assert.Equal("Faktura w walucie EUR: kod QR obsługuje tylko przelew w PLN.", data.QrMessage);
    }

    [Fact]
    public void Qr_FirstMatchWins_AccountBeforeNameOrTitle()
    {
        var data = _sut.BuildTransferData(MakeInvoice(vendorBankAccount: null, vendorName: ""));

        Assert.Equal(AccountMessage, data.QrMessage);
    }

    private static InvoiceHeader MakeInvoice(
        string? invoiceNumber = "FV/2026/03/001",
        string? vendorBankAccount = null,
        string? invoiceType = "Vat",
        decimal amountGross = 123.45m,
        string currency = "PLN",
        string vendorName = "Test Vendor Sp. z o.o.") =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            KSeFInvoiceNumber = "9999999999-20260301-ABC123-FF",
            KSeFReferenceNumber = "ref-123",
            InvoiceNumber = invoiceNumber,
            VendorName = vendorName,
            VendorNip = "1234567890",
            AmountGross = amountGross,
            Currency = currency,
            IssueDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            VendorBankAccount = vendorBankAccount,
            InvoiceType = invoiceType
        };
}
