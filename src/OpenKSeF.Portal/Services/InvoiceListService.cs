using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Models;

namespace OpenKSeF.Portal.Services;

public sealed class InvoiceListService(
    ApplicationDbContext dbContext,
    ITenantResolver tenantResolver) : IInvoiceListService
{
    public async Task<PagedResult<InvoiceListRow>> SearchAsync(InvoiceListQuery query)
    {
        var tenantIds = await tenantResolver.GetUserTenantIdsAsync();
        if (tenantIds.Count == 0)
        {
            return new PagedResult<InvoiceListRow>
            {
                Items = [],
                Page = NormalizePage(query.Page),
                PageSize = NormalizePageSize(query.PageSize),
                TotalCount = 0
            };
        }

        var normalizedPage = NormalizePage(query.Page);
        var normalizedPageSize = NormalizePageSize(query.PageSize);
        var effectiveTenantIds = query.TenantId.HasValue && tenantIds.Contains(query.TenantId.Value)
            ? new List<Guid> { query.TenantId.Value }
            : tenantIds;

        var invoicesQuery = dbContext.InvoiceHeaders
            .Where(i => effectiveTenantIds.Contains(i.TenantId))
            .AsQueryable();

        if (query.DateFrom.HasValue)
        {
            invoicesQuery = invoicesQuery.Where(i => i.IssueDate >= query.DateFrom.Value);
        }

        if (query.DateTo.HasValue)
        {
            invoicesQuery = invoicesQuery.Where(i => i.IssueDate <= query.DateTo.Value);
        }

        invoicesQuery = query.SortBy switch
        {
            InvoiceSortBy.AmountGross when query.SortDirection == SortDirection.Asc =>
                invoicesQuery.OrderBy(i => i.AmountGross).ThenBy(i => i.IssueDate),
            InvoiceSortBy.AmountGross =>
                invoicesQuery.OrderByDescending(i => i.AmountGross).ThenByDescending(i => i.IssueDate),
            _ when query.SortDirection == SortDirection.Asc =>
                invoicesQuery.OrderBy(i => i.IssueDate).ThenBy(i => i.KSeFInvoiceNumber),
            _ =>
                invoicesQuery.OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.KSeFInvoiceNumber)
        };

        var totalCount = await invoicesQuery.CountAsync();
        var items = await invoicesQuery
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(i => new InvoiceListRow
            {
                Id = i.Id,
                TenantId = i.TenantId,
                KSeFInvoiceNumber = i.KSeFInvoiceNumber,
                VendorName = i.VendorName,
                VendorNip = i.VendorNip,
                IssueDate = i.IssueDate,
                AmountGross = i.AmountGross,
                Currency = i.Currency
            })
            .ToListAsync();

        return new PagedResult<InvoiceListRow>
        {
            Items = items,
            Page = normalizedPage,
            PageSize = normalizedPageSize,
            TotalCount = totalCount
        };
    }

    private static int NormalizePage(int page) => page < 1 ? 1 : page;

    private static int NormalizePageSize(int pageSize)
    {
        return pageSize switch
        {
            <= 0 => 25,
            > 200 => 200,
            _ => pageSize
        };
    }
}
