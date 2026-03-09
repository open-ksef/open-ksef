using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;

namespace OpenKSeF.Portal.Services;

public sealed class InvoiceDetailService(
    ApplicationDbContext dbContext,
    ITenantResolver tenantResolver) : IInvoiceDetailService
{
    public async Task<InvoiceDetailModel?> GetByKsefNumberAsync(string ksefInvoiceNumber)
    {
        if (string.IsNullOrWhiteSpace(ksefInvoiceNumber))
        {
            return null;
        }

        var tenantIds = await tenantResolver.GetUserTenantIdsAsync();
        if (tenantIds.Count == 0)
        {
            return null;
        }

        return await dbContext.InvoiceHeaders
            .Where(i => tenantIds.Contains(i.TenantId) && i.KSeFInvoiceNumber == ksefInvoiceNumber)
            .Select(i => new InvoiceDetailModel
            {
                Id = i.Id,
                TenantId = i.TenantId,
                KSeFInvoiceNumber = i.KSeFInvoiceNumber,
                KSeFReferenceNumber = i.KSeFReferenceNumber,
                VendorName = i.VendorName,
                VendorNip = i.VendorNip,
                IssueDate = i.IssueDate,
                AmountGross = i.AmountGross,
                Currency = i.Currency,
                FirstSeenAt = i.FirstSeenAt,
                LastUpdatedAt = i.LastUpdatedAt
            })
            .FirstOrDefaultAsync();
    }
}
