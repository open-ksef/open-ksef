namespace OpenKSeF.Invoices.Contracts.Commands;

public sealed record CreateFinalInvoiceFromAdvancesCommand(
    Guid TenantId,
    DateTime IssueDate,
    IReadOnlyList<AdvanceSettlementEntry> Advances);

public sealed record AdvanceSettlementEntry(
    Guid AdvanceInvoiceId,
    string AdvanceDocumentNumber,
    decimal SettledAmount);
