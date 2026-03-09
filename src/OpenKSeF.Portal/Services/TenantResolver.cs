using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;

namespace OpenKSeF.Portal.Services;

public class TenantResolver(
    IHttpContextAccessor httpContextAccessor,
    ApplicationDbContext dbContext,
    ILogger<TenantResolver> logger) : ITenantResolver
{
    public string? GetCurrentUserId() => ResolveUserId();

    public async Task<List<Guid>> GetUserTenantIdsAsync()
    {
        var userId = ResolveUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return [];
        }

        return await dbContext.Tenants
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.CreatedAt)
            .Select(t => t.Id)
            .ToListAsync();
    }

    public async Task<Guid?> GetCurrentTenantIdAsync()
    {
        var tenantIds = await GetUserTenantIdsAsync();
        return tenantIds.FirstOrDefault();
    }

    public async Task<bool> HasAccessToTenantAsync(Guid tenantId)
    {
        var userId = ResolveUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        return await dbContext.Tenants
            .AnyAsync(t => t.Id == tenantId && t.UserId == userId);
    }

    private string? ResolveUserId()
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null || !(user.Identity?.IsAuthenticated ?? false))
        {
            return null;
        }

        var userId = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            logger.LogWarning("Authenticated user is missing sub/nameidentifier claim.");
        }

        return userId;
    }
}
