namespace OpenKSeF.Invoices.Domain.ValueObjects;

/// <summary>Identifiers assigned by KSeF after successful invoice acceptance.</summary>
public sealed record KsefIdentifiers(
    string KsefDocumentNumber,
    string KsefReferenceNumber,
    string? CanonicalHash = null);
