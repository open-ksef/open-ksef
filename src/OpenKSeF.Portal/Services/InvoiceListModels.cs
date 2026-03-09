namespace OpenKSeF.Portal.Services;

public enum InvoiceSortBy
{
    IssueDate = 0,
    AmountGross = 1
}

public enum SortDirection
{
    Desc = 0,
    Asc = 1
}

public sealed class InvoiceListQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public Guid? TenantId { get; init; }
    public DateTime? DateFrom { get; init; }
    public DateTime? DateTo { get; init; }
    public InvoiceSortBy SortBy { get; init; } = InvoiceSortBy.IssueDate;
    public SortDirection SortDirection { get; init; } = SortDirection.Desc;
}

public sealed class InvoiceListRow
{
    public required Guid Id { get; init; }
    public required Guid TenantId { get; init; }
    public required string KSeFInvoiceNumber { get; init; }
    public required string VendorName { get; init; }
    public required string VendorNip { get; init; }
    public required DateTime IssueDate { get; init; }
    public required decimal AmountGross { get; init; }
    public required string Currency { get; init; }
}
