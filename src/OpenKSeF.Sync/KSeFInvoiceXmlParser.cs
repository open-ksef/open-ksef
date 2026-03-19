using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace OpenKSeF.Sync;

/// <summary>
/// Extracts payment data from KSeF invoice XML (FA(2) / FA(3)).
/// Uses the CIRFMF library's <c>GetInvoiceAsync</c> output — this is a thin
/// XML reader, not a custom KSeF client.
///
/// XML path: Faktura / Fa / Platnosc / RachunekBankowy / NrRB
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
}
