namespace OpenKSeF.Invoices.Domain.ValueObjects;

public sealed record ApprovalState(DateTime ApprovedAt, string? ApprovedBy = null);
