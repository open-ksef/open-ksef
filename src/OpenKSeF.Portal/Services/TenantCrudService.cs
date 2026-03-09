using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;

namespace OpenKSeF.Portal.Services;

public sealed class TenantCrudService(
    ApplicationDbContext dbContext,
    ITenantResolver tenantResolver) : ITenantCrudService
{
    public async Task<IReadOnlyList<Tenant>> ListAsync()
    {
        var userId = tenantResolver.GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return [];
        }

        return await dbContext.Tenants
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.DisplayName ?? t.Nip)
            .ToListAsync();
    }

    public async Task<TenantFormModel?> GetAsync(Guid id)
    {
        if (!await tenantResolver.HasAccessToTenantAsync(id))
        {
            return null;
        }

        var tenant = await dbContext.Tenants
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tenant is null)
        {
            return null;
        }

        return new TenantFormModel
        {
            Nip = tenant.Nip,
            DisplayName = tenant.DisplayName,
            NotificationEmail = tenant.NotificationEmail
        };
    }

    public async Task<TenantOperationResult> CreateAsync(TenantFormModel model)
    {
        var userId = tenantResolver.GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return TenantOperationResult.Fail("User is not authenticated.");
        }

        var normalizedNip = NormalizeNip(model.Nip);
        if (!IsValidNip(normalizedNip))
        {
            return TenantOperationResult.Fail("NIP must contain exactly 10 digits.");
        }

        var duplicateExists = await dbContext.Tenants
            .AnyAsync(t => t.UserId == userId && t.Nip == normalizedNip);

        if (duplicateExists)
        {
            return TenantOperationResult.Fail("A tenant with this NIP already exists.");
        }

        var now = DateTime.UtcNow;
        dbContext.Tenants.Add(new Tenant
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Nip = normalizedNip,
            DisplayName = model.DisplayName?.Trim(),
            NotificationEmail = model.NotificationEmail?.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        });

        await dbContext.SaveChangesAsync();
        return TenantOperationResult.Ok();
    }

    public async Task<TenantOperationResult> UpdateAsync(Guid id, TenantFormModel model)
    {
        if (!await tenantResolver.HasAccessToTenantAsync(id))
        {
            return TenantOperationResult.Fail("You do not have access to this tenant.");
        }

        var normalizedNip = NormalizeNip(model.Nip);
        if (!IsValidNip(normalizedNip))
        {
            return TenantOperationResult.Fail("NIP must contain exactly 10 digits.");
        }

        var tenant = await dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null)
        {
            return TenantOperationResult.Fail("Tenant not found.");
        }

        var duplicateExists = await dbContext.Tenants
            .AnyAsync(t => t.Id != id && t.UserId == tenant.UserId && t.Nip == normalizedNip);

        if (duplicateExists)
        {
            return TenantOperationResult.Fail("A tenant with this NIP already exists.");
        }

        tenant.Nip = normalizedNip;
        tenant.DisplayName = model.DisplayName?.Trim();
        tenant.NotificationEmail = model.NotificationEmail?.Trim();
        tenant.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();
        return TenantOperationResult.Ok();
    }

    public async Task<TenantOperationResult> DeleteAsync(Guid id)
    {
        if (!await tenantResolver.HasAccessToTenantAsync(id))
        {
            return TenantOperationResult.Fail("You do not have access to this tenant.");
        }

        var tenant = await dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null)
        {
            return TenantOperationResult.Fail("Tenant not found.");
        }

        dbContext.Tenants.Remove(tenant);
        await dbContext.SaveChangesAsync();
        return TenantOperationResult.Ok();
    }

    private static string NormalizeNip(string nip) =>
        new(nip.Where(char.IsDigit).ToArray());

    private static bool IsValidNip(string nip) =>
        nip.Length == 10;
}
