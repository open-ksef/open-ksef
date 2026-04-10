namespace OpenKSeF.Invoices.Domain.ValueObjects;

public sealed record IssueDates(
    DateTime IssueDate,
    DateTime? SaleDate = null,
    DateOnly? SalePeriodStart = null,
    DateOnly? SalePeriodEnd = null,
    DateTime? DueDate = null);
