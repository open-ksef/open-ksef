using OpenKSeF.Domain.Invoicing;

namespace OpenKSeF.Domain.Tests.Invoicing;

public class InvoicingRulesTests
{
    [Theory]
    [InlineData("23", VatRate.Rate23, "1", 0.23)]
    [InlineData("22", VatRate.Rate22, "1", 0.22)]
    [InlineData("8", VatRate.Rate8, "2", 0.08)]
    [InlineData("7", VatRate.Rate7, "2", 0.07)]
    [InlineData("5", VatRate.Rate5, "3", 0.05)]
    [InlineData("4", VatRate.Rate4, "4", 0.04)]
    [InlineData("3", VatRate.Rate3, "4", 0.03)]
    [InlineData("0 KR", VatRate.ZeroDomestic, "6_1", 0)]
    [InlineData("0 WDT", VatRate.ZeroIntraEu, "6_2", 0)]
    [InlineData("0 EX", VatRate.ZeroExport, "6_3", 0)]
    [InlineData("zw", VatRate.Exempt, "7", 0)]
    [InlineData("np I", VatRate.NotTaxable, "8", 0)]
    [InlineData("np II", VatRate.NotTaxableArt100, "9", 0)]
    [InlineData("oo", VatRate.ReverseCharge, "10", 0)]
    public void VatRate_CoversAllFa3Codes(string code, VatRate rate, string bucket, double fraction)
    {
        Assert.Equal(rate, VatRates.Parse(code));
        Assert.Equal(code, rate.Code());
        Assert.Equal(bucket, rate.Bucket());
        Assert.Equal((decimal)fraction, rate.Fraction());
    }

    [Fact]
    public void VatRate_EveryEnumValueHasCode_AndIssuableIsMvpList()
    {
        Assert.All(Enum.GetValues<VatRate>(), r => Assert.True(VatRates.TryParse(r.Code(), out _)));
        Assert.Equal(["23", "8", "5", "0 KR", "zw"], VatRates.Issuable.Order().Select(r => r.Code()));
        Assert.False(VatRates.TryParse("0", out _));
        Assert.False(VatRates.TryParse(null, out _));
        Assert.True(VatRate.Rate5.HasVatAmount());
        Assert.False(VatRate.ZeroDomestic.HasVatAmount());
    }

    [Theory]
    [InlineData("61109010140000071219812874", true)]
    [InlineData("61109010140000071219812875", false)]
    [InlineData("6110901014000007121981287", false)]
    [InlineData("6110901014000007121981287A", false)]
    [InlineData(null, false)]
    public void Nrb_IsValid_ChecksMod97AndDigitsOnly(string? nrb, bool expected)
    {
        Assert.Equal(expected, Nrb.IsValid(nrb));
    }

    [Fact]
    public void Nrb_Normalize_StripsSpacesAndDashes()
    {
        Assert.Equal("61109010140000071219812874", Nrb.Normalize("61 1090 1014-0000 0712 1981 2874"));
    }

    [Theory]
    [InlineData("FV/{nr}/{MM}/{RRRR}", 2026, 11, 3, "2026-11", "FV/57/11/2026")]
    [InlineData("{nr}/{RRRR}", 2026, 1, 15, "2026", "57/2026")]
    [InlineData("{RRRR}-{MM}-{nr}", 2026, 2, 28, "2026-02", "2026-02-57")]
    public void Numbering_PeriodFromIssueDateAndFormat(string pattern, int y, int m, int d, string period, string number)
    {
        var date = new DateOnly(y, m, d);

        Assert.Equal(period, InvoiceNumbering.PeriodKey(pattern, date));
        Assert.Equal(number, InvoiceNumbering.Format(pattern, 57, date));
    }

    [Theory]
    [InlineData("FV/{nr}/{MM}/{RRRR}", true)]
    [InlineData("FV/{MM}/{RRRR}", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Numbering_PatternNeedsNumberToken(string? pattern, bool expected)
    {
        Assert.Equal(expected, InvoiceNumbering.HasNumberToken(pattern));
    }
}
