using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Domain.Presentation;

public static class InvoicePrintModelFactory
{
    public static InvoicePrintModel CreateStandard(Invoice invoice) => Create(invoice, language: "pl", duplicateIssuedAt: null);

    public static InvoicePrintModel CreateEnglish(Invoice invoice) => Create(invoice, language: "en", duplicateIssuedAt: null);

    public static InvoicePrintModel CreateDuplicate(Invoice invoice, DateTime duplicateIssuedAt) =>
        Create(invoice, language: "pl", duplicateIssuedAt: duplicateIssuedAt);

    private static InvoicePrintModel Create(Invoice invoice, string language, DateTime? duplicateIssuedAt)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var title = language == "en" ? "Invoice" : "Faktura";
        if (duplicateIssuedAt is not null)
        {
            title = language == "en" ? "Duplicate Invoice" : "Duplikat Faktury";
        }

        return new InvoicePrintModel(
            invoice.Kind,
            title,
            language,
            invoice.DocumentNumber?.Value,
            invoice.Seller.Name.Value,
            invoice.Buyer.Name.Value,
            invoice.Currency,
            invoice.Totals,
            duplicateIssuedAt);
    }
}
