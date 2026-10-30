namespace OpenKSeF.Domain.Invoicing;

public enum VatRate
{
    Rate23,
    Rate22,
    Rate8,
    Rate7,
    Rate5,
    Rate4,
    Rate3,
    ZeroDomestic,
    ZeroIntraEu,
    ZeroExport,
    Exempt,
    ReverseCharge,
    NotTaxable,
    NotTaxableArt100
}

/// <summary>FA(3) TStawkaPodatku codes, P_13_x/P_14_x buckets and the rates allowed when issuing.</summary>
public static class VatRates
{
    private static readonly Dictionary<VatRate, (string Code, decimal Fraction, string Bucket)> Table = new()
    {
        [VatRate.Rate23] = ("23", 0.23m, "1"),
        [VatRate.Rate22] = ("22", 0.22m, "1"),
        [VatRate.Rate8] = ("8", 0.08m, "2"),
        [VatRate.Rate7] = ("7", 0.07m, "2"),
        [VatRate.Rate5] = ("5", 0.05m, "3"),
        [VatRate.Rate4] = ("4", 0.04m, "4"),
        [VatRate.Rate3] = ("3", 0.03m, "4"),
        [VatRate.ZeroDomestic] = ("0 KR", 0m, "6_1"),
        [VatRate.ZeroIntraEu] = ("0 WDT", 0m, "6_2"),
        [VatRate.ZeroExport] = ("0 EX", 0m, "6_3"),
        [VatRate.Exempt] = ("zw", 0m, "7"),
        [VatRate.NotTaxable] = ("np I", 0m, "8"),
        [VatRate.NotTaxableArt100] = ("np II", 0m, "9"),
        [VatRate.ReverseCharge] = ("oo", 0m, "10"),
    };

    public static readonly IReadOnlySet<VatRate> Issuable = new HashSet<VatRate>
    {
        VatRate.Rate23, VatRate.Rate8, VatRate.Rate5, VatRate.ZeroDomestic, VatRate.Exempt
    };

    public static string Code(this VatRate rate) => Table[rate].Code;

    public static decimal Fraction(this VatRate rate) => Table[rate].Fraction;

    public static string Bucket(this VatRate rate) => Table[rate].Bucket;

    public static bool HasVatAmount(this VatRate rate) => Table[rate].Bucket is "1" or "2" or "3" or "4" or "5";

    public static bool TryParse(string? code, out VatRate rate)
    {
        foreach (var (key, value) in Table)
        {
            if (value.Code == code)
            {
                rate = key;
                return true;
            }
        }

        rate = default;
        return false;
    }

    public static VatRate Parse(string code) =>
        TryParse(code, out var rate) ? rate : throw new FormatException($"Unknown FA(3) VAT rate code '{code}'.");
}
