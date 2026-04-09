namespace OpenKSeF.Invoices.Domain.ValueObjects;

public sealed record DuplicateMetadata(DateTime IssuedAt, string? IssuedBy = null);
