using System.Xml.Linq;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Integration;

namespace OpenKSeF.Invoices.Infrastructure.Mapping;

/// <summary>
/// Maps a domain <see cref="Invoice"/> to a <see cref="KsefInvoicePayload"/> containing
/// a simplified FA(2)-compatible XML envelope. Full schema compliance is enforced by
/// the KSeF technical validation rules (E4-S2) before transmission.
/// </summary>
public sealed class InvoiceToKsefPayloadMapper : IInvoiceToKsefPayloadMapper
{
    private static readonly XNamespace Fa = "http://crd.gov.pl/wzor/2023/06/29/12648/";

    public KsefInvoicePayload? TryMap(Invoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        if (invoice.DocumentNumber is null) return null;
        if (invoice.Seller.Nip is null) return null;
        if (invoice.Status is not (DocumentStatus.Approved or DocumentStatus.SubmittedToKsef)) return null;

        try
        {
            var xml = BuildXml(invoice);
            return new KsefInvoicePayload(xml, invoice.DocumentNumber.Value, invoice.Seller.Nip.Value);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildXml(Invoice invoice)
    {
        var root = new XElement(Fa + "Faktura",
            new XAttribute(XNamespace.Xmlns + "fa", Fa.NamespaceName),
            new XElement(Fa + "Naglowek",
                new XElement(Fa + "SystemInfo", "OpenKSeF"),
                new XElement(Fa + "SchemaVersion", "1-0E")),
            new XElement(Fa + "Podmiot1",
                new XElement(Fa + "DaneIdentyfikacyjne",
                    new XElement(Fa + "NIP", invoice.Seller.Nip!.Value),
                    new XElement(Fa + "Nazwa", invoice.Seller.Name.Value))),
            new XElement(Fa + "Podmiot2",
                new XElement(Fa + "DaneIdentyfikacyjne",
                    BuildBuyerNip(invoice),
                    new XElement(Fa + "Nazwa", invoice.Buyer.Name.Value))),
            new XElement(Fa + "Fa",
                new XElement(Fa + "KodWaluty", invoice.Currency.Value),
                new XElement(Fa + "P_1", invoice.IssueDate.ToString("yyyy-MM-dd")),
                new XElement(Fa + "P_2", invoice.DocumentNumber!.Value),
                new XElement(Fa + "RodzajFaktury", MapKind(invoice.Kind)),
                new XElement(Fa + "P_15", invoice.Totals.GrossTotal.Amount.ToString("F2")),
                BuildLines(invoice),
                BuildVatSummaries(invoice)));

        return root.ToString(SaveOptions.DisableFormatting);
    }

    private static XElement? BuildBuyerNip(Invoice invoice) =>
        invoice.Buyer.Nip is not null
            ? new XElement(Fa + "NIP", invoice.Buyer.Nip.Value)
            : null;

    private static string MapKind(DocumentKind kind) => kind switch
    {
        DocumentKind.VatInvoice => "VAT",
        DocumentKind.AdvanceInvoice => "ZAL",
        DocumentKind.FinalInvoice => "ROZ",
        DocumentKind.CorrectionInvoice => "KOR",
        DocumentKind.Proforma => "PRO",
        _ => "VAT"
    };

    private static XElement BuildLines(Invoice invoice)
    {
        var linesEl = new XElement(Fa + "FaWiersz");
        foreach (var line in invoice.LineItems)
        {
            linesEl.Add(new XElement(Fa + "W",
                new XElement(Fa + "NrWierszaFa", line.LineNumber),
                new XElement(Fa + "P_7", line.Description),
                new XElement(Fa + "P_8A", line.UnitOfMeasure ?? "szt."),
                new XElement(Fa + "P_8B", line.Quantity.ToString("G")),
                new XElement(Fa + "P_9A", line.NetAmount.Amount.ToString("F2")),
                new XElement(Fa + "P_11", line.VatAmount.Amount.ToString("F2")),
                new XElement(Fa + "P_12", line.VatRate.IsExempt
                    ? "zw"
                    : line.VatRate.Rate!.Value.ToString("G")))
            );

        }
        return linesEl;
    }

    private static XElement BuildVatSummaries(Invoice invoice)
    {
        var summaryEl = new XElement(Fa + "Podsumowanie");
        foreach (var vat in invoice.VatBreakdown)
        {
            summaryEl.Add(new XElement(Fa + "PodatekVAT",
                new XElement(Fa + "P_05", vat.Rate.IsExempt
                    ? "zw"
                    : vat.Rate.Rate!.Value.ToString("G")),
                new XElement(Fa + "P_06", vat.TaxableBase.Amount.ToString("F2")),
                new XElement(Fa + "P_07", vat.VatAmount.Amount.ToString("F2"))));
        }
        return summaryEl;
    }
}
