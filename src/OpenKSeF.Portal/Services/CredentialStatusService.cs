using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;

namespace OpenKSeF.Portal.Services;

public sealed class CredentialStatusService(
    ApplicationDbContext dbContext,
    ITenantResolver tenantResolver,
    TimeProvider? timeProvider = null) : ICredentialStatusService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<TenantCredentialStatusRow>> GetStatusesAsync()
    {
        var tenantIds = await tenantResolver.GetUserTenantIdsAsync();
        if (tenantIds.Count == 0)
        {
            return [];
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var tenants = await dbContext.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .OrderBy(t => t.DisplayName ?? t.Nip)
            .Select(t => new
            {
                t.Id,
                t.Nip,
                t.DisplayName
            })
            .ToListAsync();

        var credentialsByTenant = await dbContext.KSeFCredentials
            .Where(c => tenantIds.Contains(c.TenantId))
            .GroupBy(c => c.TenantId)
            .Select(group => new
            {
                TenantId = group.Key,
                TokenConfigured = group.Any(c => !string.IsNullOrWhiteSpace(c.EncryptedToken))
            })
            .ToDictionaryAsync(x => x.TenantId, x => x.TokenConfigured);

        var syncByTenant = await dbContext.SyncStates
            .Where(s => tenantIds.Contains(s.TenantId))
            .ToDictionaryAsync(s => s.TenantId, s => s.LastSuccessfulSync);

        return tenants.Select(tenant =>
        {
            var tokenConfigured = credentialsByTenant.GetValueOrDefault(tenant.Id);
            var lastSuccessfulSync = syncByTenant.GetValueOrDefault(tenant.Id);

            return new TenantCredentialStatusRow
            {
                TenantId = tenant.Id,
                Nip = tenant.Nip,
                DisplayName = tenant.DisplayName,
                TokenConfigured = tokenConfigured,
                LastSuccessfulSync = lastSuccessfulSync,
                Status = ResolveStatus(tokenConfigured, lastSuccessfulSync, now)
            };
        }).ToList();
    }

    private static CredentialHealthStatus ResolveStatus(
        bool tokenConfigured,
        DateTime? lastSuccessfulSync,
        DateTime now)
    {
        if (!tokenConfigured || !lastSuccessfulSync.HasValue)
        {
            return CredentialHealthStatus.Error;
        }

        var age = now - lastSuccessfulSync.Value;
        if (age <= TimeSpan.FromHours(24))
        {
            return CredentialHealthStatus.Active;
        }

        if (age <= TimeSpan.FromHours(72))
        {
            return CredentialHealthStatus.Warning;
        }

        return CredentialHealthStatus.Error;
    }
}
