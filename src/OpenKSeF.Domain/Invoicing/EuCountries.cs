namespace OpenKSeF.Domain.Invoicing;

/// <summary>EU buyers for services: FA(3) TKodyKrajowUE without PL and XI, because a service to Northern Ireland is a third-country case. Countries use TKodKraju codes, so Greece is GR here and EL as a VAT prefix.</summary>
public static class EuCountries
{
    private const string Greece = "GR";
    private const string GreeceVatPrefix = "EL";

    private static readonly HashSet<string> Codes =
    [
        "AT", "BE", "BG", "CY", "CZ", "DE", "DK", "EE", "ES", "FI", "FR", Greece, "HR",
        "HU", "IE", "IT", "LT", "LU", "LV", "MT", "NL", "PT", "RO", "SE", "SI", "SK"
    ];

    public static bool Contains(string? countryCode) => countryCode is not null && Codes.Contains(countryCode);

    public static string VatPrefix(string countryCode) => countryCode == Greece ? GreeceVatPrefix : countryCode;

    public static string FromVatPrefix(string code) => code == GreeceVatPrefix ? Greece : code;

    public static string VatNumberWithoutPrefix(string number, string countryCode)
    {
        var cleaned = new string(number.Where(c => !char.IsWhiteSpace(c) && c is not ('-' or '.')).ToArray()).ToUpperInvariant();
        var prefix = VatPrefix(countryCode);
        return cleaned.StartsWith(prefix, StringComparison.Ordinal) ? cleaned[prefix.Length..] : cleaned;
    }
}
