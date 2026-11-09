using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.DTOs;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Sync;

/// <summary>
/// Extracts payment data, settlement and line items from KSeF invoice XML (FA(2) / FA(3)).
/// Uses the CIRFMF library's <c>GetInvoiceAsync</c> output — this is a thin
/// XML reader, not a custom KSeF client.
///
/// XML paths:
///   Bank account:  Faktura / Fa / Platnosc / RachunekBankowy / NrRB (first valid PL NRB, else first)
///   Due date:      Faktura / Fa / Platnosc / TerminPlatnosci / Termin (earliest of up to 100)
///   Payment flags: Fa / Adnotacje / P_18A, Fa / Platnosc / Zaplacono, DataZaplaty, ZnacznikZaplatyCzesciowej
///   Factor:        Fa / Platnosc / RachunekBankowyFaktora / NrRB, Faktura / Podmiot3 (Rola = 1) / DaneIdentyfikacyjne
///   Amount due:    Faktura / Fa / P_15 and Fa / Rozliczenie
///   Line items:    Faktura / Fa / FaWiersz
/// Schema reference: ksef-docs/faktury/schemy/FA/
/// </summary>
public sealed class KSeFInvoiceXmlParser
{
    private static readonly XNamespace Fa2Ns = "http://crd.gov.pl/wzor/2023/06/29/12648/";
    private static readonly XNamespace Fa3Ns = "http://crd.gov.pl/wzor/2025/06/25/13775/";

    private readonly ILogger<KSeFInvoiceXmlParser> _logger;

    public KSeFInvoiceXmlParser(ILogger<KSeFInvoiceXmlParser> logger)
    {
        _logger = logger;
    }

    public InvoiceXmlDetails ExtractInvoiceDetails(byte[] invoiceXml)
    {
        try
        {
            using var stream = new MemoryStream(invoiceXml);
            var doc = XDocument.Load(stream);
            return ExtractDetails(doc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse invoice XML for details extraction");
            return InvoiceXmlDetails.Unreadable;
        }
    }

    private static InvoiceXmlDetails ExtractDetails(XDocument doc)
    {
        var root = doc.Root;
        if (root is null) return InvoiceXmlDetails.Unreadable;

        var ns = DetectNamespace(root);
        if (ns is null) return InvoiceXmlDetails.Unreadable;

        var fa = root.Descendants(ns + "Fa").FirstOrDefault();
        if (fa is null) return InvoiceXmlDetails.Unreadable;

        var lines = new List<InvoiceLineDto>();
        foreach (var wiersz in fa.Elements(ns + "FaWiersz"))
        {
            var lineNumber = ParseInt(wiersz.Element(ns + "NrWierszaFa")?.Value) ?? 0;
            var unitPriceNet = ParseDecimal(wiersz.Element(ns + "P_9A")?.Value);
            var amountNet = ParseDecimal(wiersz.Element(ns + "P_11")?.Value);
            var vatRate = wiersz.Element(ns + "P_12")?.Value?.Trim();

            // P_9B, P_11A, P_11Vat are only present for special cases (art. 106e ust. 7, 8, 10).
            // For standard invoices, derive gross/VAT from net amount and rate.
            var unitPriceGross = ParseDecimal(wiersz.Element(ns + "P_9B")?.Value)
                ?? DeriveGross(unitPriceNet, vatRate, 6);
            var amountGross = ParseDecimal(wiersz.Element(ns + "P_11A")?.Value)
                ?? DeriveGross(amountNet, vatRate, 2);
            var amountVat = ParseDecimal(wiersz.Element(ns + "P_11Vat")?.Value)
                ?? DeriveVat(amountNet, vatRate);

            lines.Add(new InvoiceLineDto(
                LineNumber: lineNumber,
                Name: wiersz.Element(ns + "P_7")?.Value?.Trim(),
                Unit: wiersz.Element(ns + "P_8A")?.Value?.Trim(),
                Quantity: ParseDecimal(wiersz.Element(ns + "P_8B")?.Value),
                UnitPriceNet: unitPriceNet,
                UnitPriceGross: unitPriceGross,
                AmountNet: amountNet,
                AmountGross: amountGross,
                AmountVat: amountVat,
                VatRate: vatRate));
        }

        var platnosc = fa.Element(ns + "Platnosc");
        var rozliczenie = fa.Element(ns + "Rozliczenie");
        var (settlement, settlementAmount) = Settlement.Classify(
            fa.Element(ns + "P_15")?.Value,
            Amounts(rozliczenie, ns + "Obciazenia", ns),
            rozliczenie?.Element(ns + "SumaObciazen")?.Value,
            Amounts(rozliczenie, ns + "Odliczenia", ns),
            rozliczenie?.Element(ns + "SumaOdliczen")?.Value,
            rozliczenie?.Element(ns + "DoZaplaty")?.Value,
            rozliczenie?.Element(ns + "DoRozliczenia")?.Value);

        var factorAccounts = AccountNumbers(platnosc, ns + "RachunekBankowyFaktora");
        var factor = factorAccounts.Count == 0
            ? null
            : root.Elements(ns + "Podmiot3")
                .FirstOrDefault(p => p.Element(ns + "Rola")?.Value.Trim() == "1")
                ?.Element(ns + "DaneIdentyfikacyjne");

        return new InvoiceXmlDetails(
            PickAccount(AccountNumbers(platnosc, ns + "RachunekBankowy")),
            lines,
            ExtractDueDate(platnosc, ns),
            settlement,
            settlementAmount,
            fa.Element(ns + "Adnotacje")?.Element(ns + "P_18A")?.Value.Trim() == "1",
            platnosc?.Element(ns + "Zaplacono")?.Value.Trim() == "1" ? ParseDate(platnosc.Element(ns + "DataZaplaty")?.Value) : null,
            short.TryParse(platnosc?.Element(ns + "ZnacznikZaplatyCzesciowej")?.Value.Trim(), out var partial) ? partial : null,
            PickAccount(factorAccounts),
            Text(factor?.Element(ns + "NIP")?.Value, 10),
            Text(factor?.Element(ns + "Nazwa")?.Value, 512));
    }

    private static DateOnly? ExtractDueDate(XElement? platnosc, XNamespace ns) =>
        platnosc?.Elements(ns + "TerminPlatnosci")
            .Elements(ns + "Termin")
            .Select(t => ParseDate(t.Value))
            .Min();

    private static List<string> AccountNumbers(XElement? platnosc, XName accountElement) =>
        platnosc?.Elements(accountElement)
            .Select(a => a.Element(accountElement.Namespace + "NrRB")?.Value.Trim())
            .OfType<string>()
            .Where(nrb => nrb.Length > 0)
            .ToList() ?? [];

    private static string? PickAccount(List<string> accounts) =>
        accounts.FirstOrDefault(a => QrCodeService.SanitizeAccount(a).Length > 0) ?? accounts.FirstOrDefault();

    private static List<string> Amounts(XElement? rozliczenie, XName group, XNamespace ns) =>
        rozliczenie?.Elements(group).Select(e => e.Element(ns + "Kwota")?.Value ?? string.Empty).ToList() ?? [];

    private static string? Text(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > maxLength ? null : trimmed;
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static XNamespace? DetectNamespace(XElement root)
    {
        var ns = root.Name.Namespace;
        if (ns == Fa2Ns || ns == Fa3Ns) return ns;

        if (root.Descendants(Fa3Ns + "Fa").Any()) return Fa3Ns;
        if (root.Descendants(Fa2Ns + "Fa").Any()) return Fa2Ns;

        return ns != XNamespace.None ? ns : null;
    }

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result : null;
    }

    private static int? ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return int.TryParse(value, out var result) ? result : null;
    }

    /// <summary>
    /// Parses a KSeF VAT rate string (e.g. "23", "8", "5", "0") to a decimal multiplier.
    /// Returns null for non-taxable rates ("ZW", "NP", "OO", etc.).
    /// </summary>
    private static decimal? ParseVatRateMultiplier(string? vatRate)
    {
        if (string.IsNullOrWhiteSpace(vatRate)) return null;
        return decimal.TryParse(vatRate, NumberStyles.Any, CultureInfo.InvariantCulture, out var rate)
            ? rate / 100m : null;
    }

    private static decimal? DeriveGross(decimal? net, string? vatRate, int decimals)
    {
        if (net is null) return null;
        var multiplier = ParseVatRateMultiplier(vatRate);
        var gross = multiplier.HasValue ? net.Value * (1 + multiplier.Value) : net.Value;
        return Math.Round(gross, decimals, MidpointRounding.AwayFromZero);
    }

    private static decimal? DeriveVat(decimal? net, string? vatRate)
    {
        if (net is null) return null;
        var multiplier = ParseVatRateMultiplier(vatRate);
        return multiplier.HasValue ? Math.Round(net.Value * multiplier.Value, 2, MidpointRounding.AwayFromZero) : 0m;
    }
}
