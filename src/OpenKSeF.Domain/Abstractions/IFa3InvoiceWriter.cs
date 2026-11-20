using OpenKSeF.Domain.Invoicing;

namespace OpenKSeF.Domain.Abstractions;

/// <summary>Builds FA(3) XML for an issued invoice and validates it against the embedded XSD, offline.</summary>
public interface IFa3InvoiceWriter
{
    Fa3WriteResult Write(IssuedInvoice invoice, DateTime generatedAtUtc, Fa3Refs? refs = null);
}

/// <summary>
/// Documents an invoice refers to: for a correction the original named in DaneFaKorygowanej, the state it corrects and P_15ZK;
/// for a ROZ or the last ZAL the advances in FakturaZaliczkowa, and for a ROZ their split per rate.
/// </summary>
public sealed record Fa3Refs(
    IssuedInvoice? Original,
    IssuedInvoice? CorrectedState,
    decimal? AmountBefore = null,
    IReadOnlyList<string>? AdvanceKsefNumbers = null,
    IReadOnlyList<VatSummary>? Deducted = null);

public sealed record Fa3WriteResult(byte[] Xml, IReadOnlyList<string> SchemaErrors)
{
    public bool IsValid => SchemaErrors.Count == 0;
}
