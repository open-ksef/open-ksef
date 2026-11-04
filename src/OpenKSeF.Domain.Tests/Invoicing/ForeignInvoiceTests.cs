using OpenKSeF.Domain.Invoicing;

namespace OpenKSeF.Domain.Tests.Invoicing;

public class ForeignInvoiceTests
{
    private const string ValidNrb = "61109010140000071219812874";
    private static readonly DateOnly Today = new(2026, 11, 3);
    private static readonly DateTime Now = new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);

    private static IssuedInvoice Draft(string? country = "GB", string? foreignId = null, string currency = "GBP", decimal? rate = 5.2m, VatRate line = VatRate.NotTaxable)
    {
        var invoice = IssuedInvoice.Draft(Guid.NewGuid(), Guid.NewGuid(), Today, Now);
        invoice.SetBuyer(BuyerKind.Foreign, null, "Example Buyer Ltd", "1 Test Street", null);
        invoice.SetForeignBuyer(country, foreignId);
        invoice.SetCurrency(currency, rate, null);
        invoice.AddLine("Consulting", "h", 1, 100m, line);
        return invoice;
    }

    private static SellerProfile Profile(string? bankAccount = ValidNrb)
    {
        var profile = new SellerProfile(Guid.NewGuid());
        profile.Update("Sprzedawca", "ul. Prosta 1", "00-001 Warszawa", bankAccount, false, InvoiceNumbering.DefaultPattern);
        return profile;
    }

    private static IReadOnlyList<InvoiceCheck> Errors(IssuedInvoice invoice, SellerProfile? profile = null) =>
        IssueChecks.Errors(invoice, profile ?? Profile(), Today);

    private static void AssertSingleError(IssuedInvoice invoice, string code, string field)
    {
        var error = Assert.Single(Errors(invoice));
        Assert.Equal(code, error.Code);
        Assert.Equal(field, error.Field);
    }

    [Fact]
    public void ThirdCountryNpI_HasNoErrors() => Assert.Empty(Errors(Draft()));

    [Fact]
    public void EuNpIIWithVatNumber_HasNoErrors() =>
        Assert.Empty(Errors(Draft("DE", "123456789", "EUR", 4.2m, VatRate.NotTaxableArt100)));

    [Fact]
    public void CountryPl_IsBlockedOnCountry() => AssertSingleError(Draft("PL"), "INV-VAL-030", "buyer.countryCode");

    [Fact]
    public void NoCountry_IsBlockedOnCountry() => AssertSingleError(Draft(country: null), "INV-VAL-030", "buyer.countryCode");

    [Fact]
    public void PolishNip_IsBlockedOnNip()
    {
        var invoice = Draft();
        invoice.SetBuyer(BuyerKind.Foreign, "5261040828", "Example Buyer Ltd", "1 Test Street", null);

        AssertSingleError(invoice, "INV-VAL-031", "buyer.nip");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("TOO-LONG-VAT-NUMBER-123")]
    public void EuBuyerWithoutUsableVatNumber_IsBlockedOnVatUe(string? vatNumber) =>
        AssertSingleError(Draft("DE", vatNumber, "EUR", 4.2m, VatRate.NotTaxableArt100), "INV-VAL-032", "buyer.vatUe");

    [Fact]
    public void TaxableLine_IsBlockedOnLineRate() =>
        AssertSingleError(Draft(line: VatRate.Rate23), "INV-VAL-062", "lines[0].vatRate");

    [Theory]
    [InlineData(VatRate.ZeroIntraEu)]
    [InlineData(VatRate.ZeroExport)]
    [InlineData(VatRate.ReverseCharge)]
    [InlineData(VatRate.Exempt)]
    public void OtherRates_AreBlockedOnForeign(VatRate rate)
    {
        var invoice = Draft(line: rate);

        Assert.Contains(Errors(invoice), e => e is { Code: "INV-VAL-062", Field: "lines[0].vatRate" });
    }

    [Fact]
    public void NpIForEuBuyer_IsBlockedOnLineRate() =>
        AssertSingleError(Draft("DE", "123456789", "EUR", 4.2m, VatRate.NotTaxable), "INV-VAL-063", "lines[0].vatRate");

    [Fact]
    public void NpIIForThirdCountry_IsBlockedOnLineRate() =>
        AssertSingleError(Draft(line: VatRate.NotTaxableArt100), "INV-VAL-064", "lines[0].vatRate");

    [Fact]
    public void NpIIForNorthernIreland_IsBlocked_BecauseServicesThereAreThirdCountry() =>
        AssertSingleError(Draft("XI", "123456789", "GBP", 5.2m, VatRate.NotTaxableArt100), "INV-VAL-064", "lines[0].vatRate");

    [Theory]
    [InlineData(BuyerKind.Company, "5261040828")]
    [InlineData(BuyerKind.Person, null)]
    public void NpOnDomesticKinds_IsBlockedOnLineRate(BuyerKind kind, string? nip)
    {
        foreach (var rate in new[] { VatRate.NotTaxable, VatRate.NotTaxableArt100 })
        {
            var invoice = IssuedInvoice.Draft(Guid.NewGuid(), Guid.NewGuid(), Today, Now);
            invoice.SetBuyer(kind, nip, "Nabywca", "ul. Długa 2", null);
            invoice.AddLine("Usługa", null, 1, 100m, rate);

            AssertSingleError(invoice, "INV-VAL-060", "lines[0].vatRate");
        }
    }

    [Fact]
    public void ForeignCurrencyOnDomesticKind_IsBlockedOnCurrency()
    {
        var invoice = IssuedInvoice.Draft(Guid.NewGuid(), Guid.NewGuid(), Today, Now);
        invoice.SetBuyer(BuyerKind.Company, "5261040828", "Nabywca", "ul. Długa 2", null);
        invoice.SetCurrency("GBP", 5.2m, null);
        invoice.AddLine("Usługa", null, 1, 100m, VatRate.Rate23);

        AssertSingleError(invoice, "INV-VAL-091", "currency");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPlnWithoutPositiveRate_IsBlockedOnRate(int? rate) =>
        AssertSingleError(Draft(rate: rate), "INV-VAL-092", "exchangeRate");

    [Fact]
    public void Pln_NeedsNoRate() => Assert.Empty(Errors(Draft(currency: "PLN", rate: null)));

    [Fact]
    public void ForeignAboveThreshold_HasNoSplitPaymentWarning()
    {
        var invoice = Draft();
        invoice.ReplaceLines([("Consulting", "h", 1m, 20000m, VatRate.NotTaxable)]);

        Assert.Empty(IssueChecks.Warnings(invoice));
    }

    [Fact]
    public void DomesticAboveThreshold_KeepsSplitPaymentWarning()
    {
        var invoice = IssuedInvoice.Draft(Guid.NewGuid(), Guid.NewGuid(), Today, Now);
        invoice.SetBuyer(BuyerKind.Company, "5261040828", "Nabywca", "ul. Długa 2", null);
        invoice.AddLine("Usługa", null, 1, 20000m, VatRate.Rate23);

        Assert.Contains(IssueChecks.Warnings(invoice), w => w.Code == "INV-W-MPP");
    }

    [Fact]
    public void TransferWithOnlyForeignAccount_NeedsNoNrb()
    {
        var profile = Profile(bankAccount: null);
        Assert.Contains(Errors(Draft(), profile), e => e.Code == "INV-VAL-012");

        profile.SetBankDetails(null, null, "GB82WEST12345698765432", null, null);

        Assert.Empty(Errors(Draft(), profile));
    }

    [Fact]
    public void SetForeignBuyer_NullKeepsAndEmptyClears()
    {
        var invoice = Draft("GB", "GB-TEST-1");

        invoice.SetForeignBuyer(null, null);
        Assert.Equal("GB", invoice.BuyerCountryCode);
        Assert.Equal("GB-TEST-1", invoice.BuyerForeignId);
        Assert.Equal(IssuedInvoice.BuyerIdOther, invoice.BuyerIdType);

        invoice.SetForeignBuyer(null, "");
        Assert.Null(invoice.BuyerForeignId);
        Assert.Equal(IssuedInvoice.BuyerIdNone, invoice.BuyerIdType);
        Assert.Null(invoice.BuyerNip);
    }

    [Theory]
    [InlineData("de", "DE123456789", "DE", "123456789")]
    [InlineData("DE", "de 123-456.789", "DE", "123456789")]
    [InlineData("AT", "ATU12345678", "AT", "U12345678")]
    [InlineData("EL", "EL123456789", "GR", "123456789")]
    public void SetForeignBuyer_NormalizesEuVatNumber(string country, string entered, string storedCountry, string stored)
    {
        var invoice = Draft(country, entered, "EUR", 4.2m, VatRate.NotTaxableArt100);

        Assert.Equal(storedCountry, invoice.BuyerCountryCode);
        Assert.Equal(stored, invoice.BuyerForeignId);
        Assert.Equal(IssuedInvoice.BuyerIdVatUe, invoice.BuyerIdType);
    }

    [Fact]
    public void SwitchingToDomesticKind_DropsForeignBuyerData()
    {
        var invoice = Draft("GB", "GB-TEST-1");

        invoice.SetBuyer(BuyerKind.Company, "5261040828", "Nabywca", "ul. Długa 2", null);

        Assert.Null(invoice.BuyerCountryCode);
        Assert.Null(invoice.BuyerIdType);
        Assert.Null(invoice.BuyerForeignId);
    }

    [Fact]
    public void SetCurrency_KeepsRateWhenNothingIsGiven()
    {
        var invoice = Draft();
        invoice.SetCurrency("GBP", 5.1234m, new DateOnly(2026, 11, 2));

        invoice.SetCurrency(null, null, null);
        Assert.Equal(5.1234m, invoice.ExchangeRate);
        Assert.Equal(new DateOnly(2026, 11, 2), invoice.ExchangeRateDate);

        invoice.SetCurrency("GBP", null, null);
        Assert.Equal(5.1234m, invoice.ExchangeRate);
    }

    [Fact]
    public void SetCurrency_TypedRateHasNoTableDate()
    {
        var invoice = Draft();
        invoice.SetCurrency("GBP", 5.1234m, new DateOnly(2026, 11, 2));

        invoice.SetCurrency(null, 5.5m, null);

        Assert.Equal(5.5m, invoice.ExchangeRate);
        Assert.Null(invoice.ExchangeRateDate);
    }

    [Fact]
    public void SetCurrency_ChangeDropsRate_AndPlnNeverHasOne()
    {
        var invoice = Draft();

        invoice.SetCurrency("EUR", null, null);
        Assert.Null(invoice.ExchangeRate);
        Assert.True(invoice.NeedsExchangeRate);

        invoice.SetCurrency("EUR", 4.2m, null);
        invoice.SetCurrency("PLN", 4.2m, null);
        Assert.Null(invoice.ExchangeRate);
        Assert.False(invoice.NeedsExchangeRate);
    }

    [Fact]
    public void Issue_SnapshotsAccountForForeignBuyer()
    {
        var withMain = Profile();
        withMain.SetBankDetails("TESTPLPW", "Bank Testowy S.A.", null, null, null);
        var invoice = Draft();
        invoice.Issue(withMain, "5260250274", "FV/1", Now);
        Assert.Equal(ValidNrb, invoice.SellerBankAccount);
        Assert.Equal("PL" + ValidNrb, invoice.SellerIban);
        Assert.Equal("TESTPLPW", invoice.SellerBankSwift);

        var withForeign = Profile();
        withForeign.SetBankDetails("TESTPLPW", "Bank Testowy S.A.", "gb82 west 1234 5698 7654 32", "TESTGB2L", "West Test Bank");
        invoice = Draft();
        invoice.Issue(withForeign, "5260250274", "FV/2", Now);
        Assert.Null(invoice.SellerBankAccount);
        Assert.Equal("GB82WEST12345698765432", invoice.SellerIban);
        Assert.Equal("TESTGB2L", invoice.SellerBankSwift);
        Assert.Equal("West Test Bank", invoice.SellerBankName);
        Assert.Null(invoice.KsefNumber);
    }

    [Fact]
    public void SetBankDetails_NullKeepsAndEmptyClears()
    {
        var profile = Profile();
        profile.SetBankDetails("testplpw", "Bank", "GB82WEST12345698765432", "TESTGB2L", "West");

        profile.SetBankDetails(null, null, null, null, null);
        Assert.Equal("TESTPLPW", profile.BankSwift);
        Assert.Equal("GB82WEST12345698765432", profile.ForeignBankAccount);

        profile.SetBankDetails("", null, "", null, null);
        Assert.Null(profile.BankSwift);
        Assert.Equal("Bank", profile.BankName);
        Assert.Null(profile.ForeignBankAccount);
    }

    [Theory]
    [InlineData("GB82WEST12345698765432", true)]
    [InlineData("PL61109010140000071219812874", true)]
    [InlineData("GB82WEST12345698765433", false)]
    [InlineData("gb82west12345698765432", false)]
    [InlineData("82GBWEST12345698765432", false)]
    [InlineData("GB82", false)]
    [InlineData(null, false)]
    public void IsValidIban(string? iban, bool expected) => Assert.Equal(expected, Nrb.IsValidIban(iban));
}
