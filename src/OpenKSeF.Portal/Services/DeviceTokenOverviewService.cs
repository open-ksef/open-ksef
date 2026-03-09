using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;

namespace OpenKSeF.Portal.Services;

public sealed class DeviceTokenOverviewService(
    ApplicationDbContext dbContext,
    ITenantResolver tenantResolver) : IDeviceTokenOverviewService
{
    public async Task<IReadOnlyList<DeviceTokenOverviewRow>> ListAsync(Guid? tenantId = null)
    {
        var userId = tenantResolver.GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return [];
        }

        var tenantIds = await tenantResolver.GetUserTenantIdsAsync();
        if (tenantId.HasValue && !tenantIds.Contains(tenantId.Value))
        {
            return [];
        }

        var query = dbContext.DeviceTokens
            .AsNoTracking()
            .Where(device => device.UserId == userId);

        if (tenantId.HasValue)
        {
            query = query.Where(device => device.TenantId == tenantId.Value);
        }

        return await query
            .OrderByDescending(device => device.UpdatedAt)
            .Select(device => new DeviceTokenOverviewRow
            {
                Id = device.Id,
                Platform = device.Platform,
                TenantId = device.TenantId,
                TokenMasked = MaskToken(device.Token),
                RegisteredAt = device.CreatedAt,
                LastSeenAt = device.UpdatedAt
            })
            .ToListAsync();
    }

    private static string MaskToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return "-";
        }

        var trimmed = token.Trim();
        return trimmed.Length <= 10 ? trimmed : $"{trimmed[..10]}...";
    }
}
