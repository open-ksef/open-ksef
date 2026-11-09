using System.Globalization;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Tests.Services;

public class TransferDetailsServiceTests
{
    private const string ValidAccount = "PL61 1090 1014 0000 0712 1981 2874";
    private const string CorrectionMessage = "Faktura korygująca: kwota to różnica. Sprawdź, czy jest coś do zapłaty.";
    private const string TypeMessage = "Nie można ustalić rodzaju faktury. Sprawdź kwotę na fakturze. Aby uzyskać kod QR, uruchom pełną resynchronizację (portal: Firma, zakładka Token KSeF i pobieranie).";
    private const string AccountMessage = "Brak poprawnego numeru rachunku na fakturze.";
    private const string AccountPendingMessage = "Numer rachunku pobieramy jeszcze z KSeF. Sprawdź ponownie za kilka minut.";
    private const string NameOrTitleMessage = "Nie można utworzyć kodu QR: brak czytelnej nazwy sprzedawcy lub numeru faktury.";
    private const string ManualMessage = "Zaliczka/rozliczenie: wpisz kwotę ręcznie.";
    private const string MppMessage = "Sprawdź, czy faktura nie wymaga podzielonej płatności (MPP).";
    private const string NotReadMessage = "Nie odczytano jeszcze rozliczeń z tej faktury: wpisz kwotę ręcznie, sprawdź ją na fakturze. Aby uzyskać kwotę w kodzie QR, uruchom pełną resynchronizację (portal: Firma, zakładka Token KSeF i pobieranie).";
    private const string UnconfirmedMessage = "Faktura zawiera dodatkowe rozliczenia i nie można potwierdzić kwoty do zapłaty: wpisz kwotę ręcznie, sprawdź ją na fakturze.";
    private const string ConfirmedNotice = "Kwota w kodzie QR to kwota do zapłaty z faktury, po uwzględnieniu dodatkowych rozliczeń.";
    private const string PartialMessage = "Faktura opłacona w części: wpisz pozostałą kwotę ręcznie, sprawdź ją na fakturze.";
    private const string FactorNotice = "Płatność na rachunek faktora.";
    private const string FactorAccount = "PL94 0000 0000 1234 5678 9012 3456";

    private const string TenantNip = "1111111111";

    private readonly TransferDetailsService _sut = new();
    private readonly QrCodeService _qr = new();

    private TransferData Data(InvoiceHeader invoice) => _sut.BuildTransferData(invoice, TenantNip);

    private string Text(InvoiceHeader invoice) => _sut.BuildTransferText(invoice, TenantNip);

    [Fact]
    public void BuildTransferData_Title_PrefersInvoiceNumber()
    {
        var invoice = MakeInvoice(invoiceNumber: "FV/2026/03/001");
        var result = Data(invoice);
        Assert.Equal("FV/2026/03/001", result.Title);
    }

    [Fact]
    public void BuildTransferData_Title_FallsBackToKSeFNumber()
    {
        var invoice = MakeInvoice(invoiceNumber: null);
        var result = Data(invoice);
        Assert.Equal("9999999999-20260301-ABC123-FF", result.Title);
    }

    [Fact]
    public void BuildTransferData_RecipientAccount_FromVendorBankAccount()
    {
        var invoice = MakeInvoice(vendorBankAccount: "12345678901234567890123456");
        var result = Data(invoice);
        Assert.Equal("12345678901234567890123456", result.RecipientAccount);
    }

    [Fact]
    public void BuildTransferData_RecipientAccount_NullWhenNoBankAccount()
    {
        var invoice = MakeInvoice(vendorBankAccount: null);
        var result = Data(invoice);
        Assert.Null(result.RecipientAccount);
    }

    [Fact]
    public void BuildTransferText_IncludesBankAccountWhenPresent()
    {
        var invoice = MakeInvoice(vendorBankAccount: "12345678901234567890123456");
        var result = Text(invoice);
        Assert.Contains("Nr rachunku: 12345678901234567890123456", result);
    }

    [Fact]
    public void BuildTransferText_OmitsBankAccountWhenNull()
    {
        var invoice = MakeInvoice(vendorBankAccount: null);
        var result = Text(invoice);
        Assert.DoesNotContain("Nr rachunku", result);
    }

    [Fact]
    public void BuildTransferText_Title_UsesShortInvoiceNumber()
    {
        var invoice = MakeInvoice(invoiceNumber: "FV/2026/001");
        var result = Text(invoice);
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
            Assert.Contains("\nKwota: 1\u00A0234,56 PLN\n", Text(invoice));
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

        var data = Data(invoice);

        Assert.True(data.QrAvailable);
        Assert.Null(data.QrMessage);
        Assert.Equal("1234567890|PL|61109010140000071219812874|123456|Odbiorca 1|FV 1/2026|||", _qr.BuildZbpPayload(data));
    }

    [Fact]
    public void Qr_LongSellerName_CutTo20InPayload()
    {
        var invoice = MakeInvoice(invoiceNumber: "FV 1/2026", vendorBankAccount: ValidAccount, amountGross: 1234.56m, vendorName: "Firma Testowa Spółka z o.o.");

        var payload = _qr.BuildZbpPayload(Data(invoice));

        Assert.Equal("1234567890|PL|61109010140000071219812874|123456|Firma Testowa Spolka|FV 1/2026|||", payload);
    }

    [Fact]
    public void Qr_Gross12_AmountField001200()
    {
        var invoice = MakeInvoice(vendorBankAccount: ValidAccount, amountGross: 12m);

        Assert.Equal("001200", _qr.BuildZbpPayload(Data(invoice)).Split('|')[3]);
    }

    [Theory]
    [InlineData("Vat")]
    [InlineData("Upr")]
    [InlineData("VatPef")]
    [InlineData("VatPefSp")]
    [InlineData("VatRr")]
    public void Qr_RegularTypes_EncodeGross(string invoiceType)
    {
        var data = Data(MakeInvoice(vendorBankAccount: ValidAccount, invoiceType: invoiceType));

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
        var data = Data(MakeInvoice(vendorBankAccount: ValidAccount, invoiceType: invoiceType));

        Assert.True(data.QrAvailable);
        Assert.Equal(ManualMessage, data.QrMessage);
        Assert.Equal("000000", _qr.BuildZbpPayload(data).Split('|')[3]);
    }

    [Fact]
    public void Qr_Eur_NoQr_TextKeepsCurrencyAndRawAccount()
    {
        var invoice = MakeInvoice(vendorBankAccount: ValidAccount, currency: "EUR");

        var data = Data(invoice);
        var text = Text(invoice);

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
        var data = Data(MakeInvoice(vendorBankAccount: ValidAccount, invoiceType: invoiceType, amountGross: gross));

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

        var data = Data(invoice);

        Assert.False(data.QrAvailable);
        Assert.Equal(TypeMessage, data.QrMessage);
        Assert.Contains("Nr rachunku: " + ValidAccount, Text(invoice));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Qr_GrossNotPositive_NoQr(decimal gross)
    {
        var data = Data(MakeInvoice(vendorBankAccount: ValidAccount, amountGross: gross));

        Assert.False(data.QrAvailable);
        Assert.Equal("Brak kwoty do zapłaty.", data.QrMessage);
    }

    [Theory]
    [InlineData("Zal")]
    [InlineData("Roz")]
    public void Qr_ZalRozGrossZero_NoQr(string invoiceType)
    {
        var data = Data(MakeInvoice(vendorBankAccount: ValidAccount, invoiceType: invoiceType, amountGross: 0m));

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

        var data = Data(invoice);

        Assert.False(data.QrAvailable);
        Assert.Equal(AccountMessage, data.QrMessage);
        Assert.Contains("Kwota: 123,45 PLN", Text(invoice));
        if (!string.IsNullOrEmpty(account))
            Assert.Contains("Nr rachunku: " + account, Text(invoice));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("ООО Ромашка")]
    public void Qr_NameEmptyAfterSanitizing_NoQr_CopyStillWorks(string vendorName)
    {
        var invoice = MakeInvoice(vendorBankAccount: ValidAccount, vendorName: vendorName);

        var data = Data(invoice);

        Assert.False(data.QrAvailable);
        Assert.Equal(NameOrTitleMessage, data.QrMessage);
        Assert.Contains("Nr rachunku: " + ValidAccount, Text(invoice));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("№№№")]
    public void Qr_TitleEmptyAfterSanitizing_NoQr_CopyStillWorks(string invoiceNumber)
    {
        var invoice = MakeInvoice(invoiceNumber: invoiceNumber, vendorBankAccount: ValidAccount);

        var data = Data(invoice);

        Assert.False(data.QrAvailable);
        Assert.Equal(NameOrTitleMessage, data.QrMessage);
        Assert.Contains("Nr rachunku: " + ValidAccount, Text(invoice));
    }

    [Fact]
    public void Qr_Gross15000_NoMppNotice()
    {
        var data = Data(MakeInvoice(vendorBankAccount: ValidAccount, amountGross: 15000m));

        Assert.True(data.QrAvailable);
        Assert.Null(data.QrMessage);
    }

    [Fact]
    public void Qr_Gross20000_NotReparsed_QrWithNotReadAndMppNotice()
    {
        var data = Data(MakeInvoice(vendorBankAccount: ValidAccount, amountGross: 20000m, settlement: null));

        Assert.True(data.QrAvailable);
        Assert.Equal(NotReadMessage + " " + MppMessage, data.QrMessage);
        Assert.Equal("000000", _qr.BuildZbpPayload(data).Split('|')[3]);
    }

    [Fact]
    public void Qr_Gross20000_SplitPaymentKnownNo_NoMppNotice()
    {
        var data = Data(MakeInvoice(vendorBankAccount: ValidAccount, amountGross: 20000m));

        Assert.True(data.QrAvailable);
        Assert.Null(data.QrMessage);
        Assert.Equal("2000000", _qr.BuildZbpPayload(data).Split('|')[3]);
    }

    [Fact]
    public void Qr_ZalGross20000_NotReparsed_AllMessages()
    {
        var data = Data(MakeInvoice(vendorBankAccount: ValidAccount, invoiceType: "Zal", amountGross: 20000m, settlement: null));

        Assert.True(data.QrAvailable);
        Assert.Equal(ManualMessage + " " + NotReadMessage + " " + MppMessage, data.QrMessage);
        Assert.Equal("000000", _qr.BuildZbpPayload(data).Split('|')[3]);
    }

    [Fact]
    public void Qr_BlockedAboveMppThreshold_NoMppNotice()
    {
        var data = Data(MakeInvoice(vendorBankAccount: null, amountGross: 20000m));

        Assert.Equal(AccountMessage, data.QrMessage);
    }

    [Fact]
    public void Qr_FirstMatchWins_CurrencyBeforeCorrectionAndAccount()
    {
        var data = Data(MakeInvoice(vendorBankAccount: null, invoiceType: "Kor", currency: "EUR"));

        Assert.Equal("Faktura w walucie EUR: kod QR obsługuje tylko przelew w PLN.", data.QrMessage);
    }

    [Fact]
    public void Qr_FirstMatchWins_AccountBeforeNameOrTitle()
    {
        var data = Data(MakeInvoice(vendorBankAccount: null, vendorName: ""));

        Assert.Equal(AccountMessage, data.QrMessage);
    }

    [Fact]
    public void Qr_XmlNotFetchedYet_SaysAccountIsComing()
    {
        var invoice = MakeInvoice(vendorBankAccount: null);
        invoice.XmlFetchedAt = null;

        var data = Data(invoice);

        Assert.False(data.QrAvailable);
        Assert.Equal(AccountPendingMessage, data.QrMessage);
    }

    [Fact]
    public void Qr_DatedRowWithoutMarker_XmlWasParsed_SaysNoAccount()
    {
        var invoice = MakeInvoice(vendorBankAccount: null);
        invoice.XmlFetchedAt = null;
        invoice.DueDate = new DateOnly(2026, 11, 20);

        Assert.Equal(AccountMessage, Data(invoice).QrMessage);
    }

    [Fact]
    public void Qr_XmlFetched_WithoutAccount_SaysNoAccount()
    {
        Assert.Equal(AccountMessage, Data(MakeInvoice(vendorBankAccount: null)).QrMessage);
    }

    [Theory]
    [InlineData(800, 1000, "080000")]
    [InlineData(1050, 1000, "105000")]
    [InlineData(850, 1000, "085000")]
    public void Qr_ConfirmedAmountDue_EncodesDoZaplaty_WithNotice(int due, int gross, string field)
    {
        var invoice = MakeInvoice(vendorBankAccount: ValidAccount, amountGross: gross, settlement: SettlementState.Confirmed);
        invoice.SettlementAmount = due;

        var data = Data(invoice);

        Assert.True(data.QrAvailable);
        Assert.Equal(due, data.Amount);
        Assert.Equal(ConfirmedNotice, data.QrMessage);
        Assert.Equal(field, _qr.BuildZbpPayload(data).Split('|')[3]);
        Assert.Contains($"\nKwota: {((decimal)due).ToString("N2", CultureInfo.GetCultureInfo("pl-PL"))} PLN\n", Text(invoice));
    }

    [Fact]
    public void Qr_ConfirmedEqualToGross_NoNotice()
    {
        var invoice = MakeInvoice(vendorBankAccount: ValidAccount, amountGross: 1000m, settlement: SettlementState.Confirmed);
        invoice.SettlementAmount = 1000m;

        Assert.Null(Data(invoice).QrMessage);
    }

    [Fact]
    public void Qr_Overpaid_NoQr_MessageWithAmount_AmountZero_CopyWorks()
    {
        var invoice = MakeInvoice(vendorBankAccount: ValidAccount, amountGross: 1000m, settlement: SettlementState.Overpaid);
        invoice.SettlementAmount = 300m;

        var data = Data(invoice);

        Assert.False(data.QrAvailable);
        Assert.Equal("Faktura wykazuje nadpłatę 300,00 PLN do rozliczenia lub zwrotu: nie ma kwoty do zapłaty.", data.QrMessage);
        Assert.Equal(0m, data.Amount);
        Assert.Contains("Kwota: 0,00 PLN", Text(invoice));
        Assert.Contains("Nr rachunku: " + ValidAccount, Text(invoice));
    }

    [Fact]
    public void Qr_OverpaidEur_StopsAtCurrency_AmountZero()
    {
        var invoice = MakeInvoice(vendorBankAccount: ValidAccount, currency: "EUR", settlement: SettlementState.Overpaid);
        invoice.SettlementAmount = 300m;

        var data = Data(invoice);

        Assert.False(data.QrAvailable);
        Assert.Equal("Faktura w walucie EUR: kod QR obsługuje tylko przelew w PLN.", data.QrMessage);
        Assert.Equal(0m, data.Amount);
        Assert.Contains("Kwota: 0,00 EUR", Text(invoice));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Qr_NothingDue_NoQr_Row4(int due)
    {
        var invoice = MakeInvoice(vendorBankAccount: ValidAccount, amountGross: 1000m, settlement: SettlementState.NothingDue);
        invoice.SettlementAmount = due;

        var data = Data(invoice);

        Assert.False(data.QrAvailable);
        Assert.Equal("Brak kwoty do zapłaty.", data.QrMessage);
        Assert.Equal(0m, data.Amount);
    }

    [Theory]
    [InlineData(800)]
    [InlineData(null)]
    public void Qr_Unconfirmed_ManualAmount_MessageAndFlaggedText(int? due)
    {
        var invoice = MakeInvoice(vendorBankAccount: ValidAccount, amountGross: 1000m, settlement: SettlementState.Unconfirmed);
        invoice.SettlementAmount = due;

        var data = Data(invoice);

        Assert.True(data.QrAvailable);
        Assert.Equal(UnconfirmedMessage, data.QrMessage);
        Assert.Equal(1000m, data.Amount);
        Assert.Equal("000000", _qr.BuildZbpPayload(data).Split('|')[3]);
        Assert.Contains("\nKwota brutto z faktury: 1 000,00 PLN (sprawdź kwotę do zapłaty na fakturze)\n", Text(invoice));
    }

    [Fact]
    public void Qr_NotRead_ManualAmount_NeverEncodesGross()
    {
        var invoice = MakeInvoice(vendorBankAccount: ValidAccount, amountGross: 1000m, settlement: null);

        var data = Data(invoice);

        Assert.True(data.QrAvailable);
        Assert.Equal(NotReadMessage, data.QrMessage);
        Assert.Equal("000000", _qr.BuildZbpPayload(data).Split('|')[3]);
        Assert.Contains("\nKwota brutto z faktury: 1 000,00 PLN (sprawdź kwotę do zapłaty na fakturze)\n", Text(invoice));
    }

    [Theory]
    [InlineData("Zal")]
    [InlineData("Roz")]
    public void Qr_ZalRozConfirmed_ManualAmount_TextShowsDoZaplaty(string invoiceType)
    {
        var invoice = MakeInvoice(vendorBankAccount: ValidAccount, invoiceType: invoiceType, amountGross: 1000m, settlement: SettlementState.Confirmed);
        invoice.SettlementAmount = 800m;

        var data = Data(invoice);

        Assert.True(data.QrAvailable);
        Assert.Equal(ManualMessage, data.QrMessage);
        Assert.Equal("000000", _qr.BuildZbpPayload(data).Split('|')[3]);
        Assert.Contains("Kwota: 800,00 PLN", Text(invoice));
    }

    [Fact]
    public void Qr_SplitPayment_NoQr_MessageWithVatNipNumber_NoThresholdNotice()
    {
        var invoice = MakeInvoice(invoiceNumber: "FV 7/2026", vendorBankAccount: ValidAccount, amountGross: 20000m);
        invoice.AmountVat = 3739.84m;
        invoice.SplitPayment = true;

        var data = Data(invoice);

        Assert.False(data.QrAvailable);
        Assert.Equal(
            "Faktura z mechanizmem podzielonej płatności: zapłać przelewem MPP w banku. Kwota VAT: 3 739,84 PLN, NIP sprzedawcy: 1234567890, numer faktury: FV 7/2026.",
            data.QrMessage);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void Qr_PaidAtIssue_NoQr_DatePolishUnderAnyServerCulture(string culture)
    {
        var invoice = MakeInvoice(vendorBankAccount: ValidAccount);
        invoice.SellerPaidOn = new DateOnly(2026, 3, 5);
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var data = Data(invoice);

            Assert.False(data.QrAvailable);
            Assert.Equal("Sprzedawca oznaczył fakturę jako zapłaconą (05.03.2026).", data.QrMessage);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Qr_PaidInFullInParts_NoQr()
    {
        var invoice = MakeInvoice(vendorBankAccount: ValidAccount);
        invoice.PartialPayment = 2;

        var data = Data(invoice);

        Assert.False(data.QrAvailable);
        Assert.Equal("Sprzedawca oznaczył fakturę jako zapłaconą w całości, w częściach.", data.QrMessage);
    }

    [Theory]
    [InlineData(SettlementState.None, null)]
    [InlineData(SettlementState.Confirmed, 800)]
    public void Qr_PaidInPart_ManualAmount_EvenWhenConfirmed(SettlementState settlement, int? due)
    {
        var invoice = MakeInvoice(vendorBankAccount: ValidAccount, amountGross: 1000m, settlement: settlement);
        invoice.SettlementAmount = due;
        invoice.PartialPayment = 1;

        var data = Data(invoice);

        Assert.True(data.QrAvailable);
        Assert.Equal(PartialMessage, data.QrMessage);
        Assert.Equal("000000", _qr.BuildZbpPayload(data).Split('|')[3]);
    }

    [Fact]
    public void Qr_Factor_PaysFactorAccountNipName_WithNotice()
    {
        var invoice = MakeInvoice(invoiceNumber: "FV 1/2026", vendorBankAccount: ValidAccount, amountGross: 1234.56m);
        invoice.FactorBankAccount = FactorAccount;
        invoice.FactorNip = "1111111111";
        invoice.FactorName = "Faktor Testowy SA";

        var data = Data(invoice);

        Assert.True(data.QrAvailable);
        Assert.Equal(FactorNotice, data.QrMessage);
        Assert.Equal($"1111111111|PL|{QrCodeService.SanitizeAccount(FactorAccount)}|123456|Faktor Testowy SA|FV 1/2026|||", _qr.BuildZbpPayload(data));
        Assert.Contains("Odbiorca: Faktor Testowy SA\nNIP: 1111111111\nNr rachunku: " + FactorAccount, Text(invoice));
    }

    [Fact]
    public void Qr_FactorAccountInvalid_NoQr_AccountMessage()
    {
        var invoice = MakeInvoice(vendorBankAccount: ValidAccount);
        invoice.FactorBankAccount = "PL62 1090 1014 0000 0712 1981 2874";
        invoice.FactorNip = "1111111111";
        invoice.FactorName = "Faktor Testowy SA";

        var data = Data(invoice);

        Assert.False(data.QrAvailable);
        Assert.Equal(AccountMessage, data.QrMessage);
    }

    [Fact]
    public void Qr_SellerIsTenant_NoQrNoMessage_TextReturned()
    {
        var invoice = MakeInvoice(vendorBankAccount: ValidAccount);

        var data = _sut.BuildTransferData(invoice, "1234567890");

        Assert.False(data.QrAvailable);
        Assert.Null(data.QrMessage);
        Assert.Contains("Nr rachunku: " + ValidAccount, _sut.BuildTransferText(invoice, " 1234567890 "));
    }

    [Fact]
    public void Qr_SellerIsOtherCompany_Unchanged()
    {
        var data = Data(MakeInvoice(vendorBankAccount: ValidAccount));

        Assert.True(data.QrAvailable);
        Assert.Null(data.QrMessage);
    }

    [Fact]
    public void Qr_TitleOver32_CutWithNoticeShowingFullNumber()
    {
        const string number = "FAKTURA/SPRZEDAZ/2026/03/ODDZIAL-WARSZAWA/000123";
        var data = Data(MakeInvoice(invoiceNumber: number, vendorBankAccount: ValidAccount));

        Assert.True(data.QrAvailable);
        Assert.Equal("FAKTURA/SPRZEDAZ/2026/03/ODDZIAL", _qr.BuildZbpPayload(data).Split('|')[5]);
        Assert.Equal($"Numer faktury nie mieści się w tytule przelewu (32 znaki) i został skrócony. Sprawdź tytuł w banku. Pełny numer: {number}.", data.QrMessage);
    }

    [Fact]
    public void Qr_TitleExactly32_NoNotice()
    {
        var data = Data(MakeInvoice(invoiceNumber: " FAKTURA/SPRZEDAZ/2026/03/ODDZIAL ", vendorBankAccount: ValidAccount));

        Assert.Null(data.QrMessage);
        Assert.Equal("FAKTURA/SPRZEDAZ/2026/03/ODDZIAL", _qr.BuildZbpPayload(data).Split('|')[5]);
    }

    [Fact]
    public void Qr_EdgeSpacesAfterSanitizingAndCut_Trimmed()
    {
        var data = Data(MakeInvoice(invoiceNumber: " FV 1/2026 ", vendorBankAccount: ValidAccount, vendorName: "Firma Testowa Spolk Druga"));

        var fields = _qr.BuildZbpPayload(data).Split('|');
        Assert.Equal("Firma Testowa Spolk", fields[4]);
        Assert.Equal("FV 1/2026", fields[5]);
    }

    private static InvoiceHeader MakeInvoice(
        string? invoiceNumber = "FV/2026/03/001",
        string? vendorBankAccount = null,
        string? invoiceType = "Vat",
        decimal amountGross = 123.45m,
        string currency = "PLN",
        string vendorName = "Test Vendor Sp. z o.o.",
        SettlementState? settlement = SettlementState.None) =>
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
            InvoiceType = invoiceType,
            Settlement = settlement,
            XmlFetchedAt = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)
        };
}
