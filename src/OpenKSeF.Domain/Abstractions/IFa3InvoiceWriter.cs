using OpenKSeF.Domain.Invoicing;

namespace OpenKSeF.Domain.Abstractions;

/// <summary>Builds FA(3) XML for an issued invoice and validates it against the embedded XSD, offline.</summary>
public interface IFa3InvoiceWriter
{
    Fa3WriteResult Write(IssuedInvoice invoice, DateTime generatedAtUtc, Fa3Refs? refs = null);
}

/// <summary>Documents a correction refers to: the original named in DaneFaKorygowanej and the state it corrects.</summary>
public sealed record Fa3Refs(IssuedInvoice Original, IssuedInvoice CorrectedState);

public sealed record Fa3WriteResult(byte[] Xml, IReadOnlyList<string> SchemaErrors)
{
    public bool IsValid => SchemaErrors.Count == 0;
}
