namespace OpenKSeF.Invoices.Domain.Integration;

/// <summary>
/// Placeholder for the KSeF-formatted invoice payload produced before transmission.
/// Will be expanded in E4-S1 (Domain → KSeF payload mapping).
/// </summary>
public sealed record KsefInvoicePayload(
    string InvoiceXml,
    string DocumentNumber,
    string SellerNip);
