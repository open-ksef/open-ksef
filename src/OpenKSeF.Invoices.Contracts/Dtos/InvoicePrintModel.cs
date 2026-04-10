namespace OpenKSeF.Invoices.Contracts.Dtos;

/// <summary>
/// Print projection supporting standard, duplicate, and English-label variants.
/// All variants share the same fiscal data; only labels and duplicate metadata differ.
/// </summary>
public sealed record InvoicePrintModel(
    InvoiceReadDto InvoiceData,
    PrintVariant Variant,
    PrintLabels Labels,
    DuplicatePrintInfo? DuplicateInfo = null);

/// <summary>
/// Identifies the print variant requested by the user.
/// </summary>
public enum PrintVariant
{
    /// <summary>Standard Polish-language fiscal print.</summary>
    Standard,
    /// <summary>Duplicate reissue — same fiscal content, supplemented with <see cref="DuplicatePrintInfo"/>.</summary>
    Duplicate,
    /// <summary>English-label print — same fiscal data, English field labels.</summary>
    English
}

/// <summary>
/// Metadata appended to a duplicate print.
/// The original invoice data is unchanged; only this metadata distinguishes it.
/// </summary>
public sealed record DuplicatePrintInfo(
    DateTime IssuedAt,
    string? IssuedBy,
    Guid OriginalInvoiceId,
    string OriginalDocumentNumber);

/// <summary>
/// Localised label set for a print. Swap to produce Standard (Polish) or English output.
/// </summary>
public sealed record PrintLabels(
    string InvoiceTitle,
    string SellerLabel,
    string BuyerLabel,
    string IssueDateLabel,
    string SaleDateLabel,
    string DueDateLabel,
    string DocumentNumberLabel,
    string CurrencyLabel,
    string TotalNetLabel,
    string TotalVatLabel,
    string TotalGrossLabel,
    string LineDescriptionLabel,
    string LineQuantityLabel,
    string LineUnitPriceLabel,
    string LineNetAmountLabel,
    string LineVatRateLabel,
    string LineVatAmountLabel,
    string LineGrossAmountLabel,
    string DuplicateLabel)
{
    public static readonly PrintLabels Polish = new(
        "FAKTURA VAT", "Sprzedawca", "Nabywca",
        "Data wystawienia", "Data sprzedaży", "Termin płatności",
        "Numer dokumentu", "Waluta", "Razem netto", "Razem VAT", "Razem brutto",
        "Opis", "Ilość", "Cena jedn.", "Netto", "Stawka VAT", "VAT", "Brutto",
        "DUPLIKAT");

    public static readonly PrintLabels English = new(
        "VAT INVOICE", "Seller", "Buyer",
        "Issue Date", "Sale Date", "Due Date",
        "Document Number", "Currency", "Total Net", "Total VAT", "Total Gross",
        "Description", "Quantity", "Unit Price", "Net", "VAT Rate", "VAT", "Gross",
        "DUPLICATE");
}
