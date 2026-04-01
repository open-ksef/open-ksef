using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.DTOs;

namespace OpenKSeF.Sync;

/// <summary>
/// Extracts payment data and line items from KSeF invoice XML (FA(2) / FA(3)).
/// Uses the CIRFMF library's <c>GetInvoiceAsync</c> output — this is a thin
/// XML reader, not a custom KSeF client.
///
/// XML paths:
///   Bank account: Faktura / Fa / Platnosc / RachunekBankowy / NrRB
///   Line items:   Faktura / Fa / FaWiersz
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

    public (string? BankAccount, IReadOnlyList<InvoiceLineDto> Lines) ExtractInvoiceDetails(byte[] invoiceXml)
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
            return (null, Array.Empty<InvoiceLineDto>());
        }
    }

    public string? ExtractBankAccount(byte[] invoiceXml)
    {
        try
        {
            using var stream = new MemoryStream(invoiceXml);
            var doc = XDocument.Load(stream);
            return ExtractNrRB(doc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse invoice XML for bank account extraction");
            return null;
        }
    }

    public string? ExtractBankAccount(string invoiceXml)
    {
        try
        {
            var doc = XDocument.Parse(invoiceXml);
            return ExtractNrRB(doc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse invoice XML for bank account extraction");
            return null;
        }
    }

    private static (string? BankAccount, IReadOnlyList<InvoiceLineDto> Lines) ExtractDetails(XDocument doc)
    {
        var root = doc.Root;
        if (root is null) return (null, Array.Empty<InvoiceLineDto>());

        var ns = DetectNamespace(root);
        if (ns is null) return (null, Array.Empty<InvoiceLineDto>());

        var bankAccount = root
            .Descendants(ns + "Platnosc")
            .Descendants(ns + "RachunekBankowy")
            .Elements(ns + "NrRB")
            .FirstOrDefault()
            ?.Value;

        if (string.IsNullOrWhiteSpace(bankAccount))
            bankAccount = null;
        else
            bankAccount = bankAccount.Trim();

        var fa = root.Descendants(ns + "Fa").FirstOrDefault();
        var lines = new List<InvoiceLineDto>();

        if (fa is not null)
        {
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
        }

        return (bankAccount, lines);
    }

    private static string? ExtractNrRB(XDocument doc)
    {
        var root = doc.Root;
        if (root is null) return null;

        var ns = DetectNamespace(root);
        if (ns is null) return null;

        var nrRb = root
            .Descendants(ns + "Platnosc")
            .Descendants(ns + "RachunekBankowy")
            .Elements(ns + "NrRB")
            .FirstOrDefault()
            ?.Value;

        return string.IsNullOrWhiteSpace(nrRb) ? null : nrRb.Trim();
    }

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
        return decimal.TryParse(value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var result)
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
        return decimal.TryParse(vatRate, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var rate)
            ? rate / 100m : null;
    }

    private static decimal? DeriveGross(decimal? net, string? vatRate, int decimals)
    {
        if (net is null) return null;
        var multiplier = ParseVatRateMultiplier(vatRate);
        var gross = multiplier.HasValue ? net.Value * (1 + multiplier.Value) : net.Value;
        return Math.Round(gross, decimals);
    }

    private static decimal? DeriveVat(decimal? net, string? vatRate)
    {
        if (net is null) return null;
        var multiplier = ParseVatRateMultiplier(vatRate);
        return multiplier.HasValue ? Math.Round(net.Value * multiplier.Value, 2) : 0m;
    }
}
