using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;

namespace OpenKSeF.Domain.Services;

/// <summary>Deletes audit events no longer linked to a company (deleted companies, 2FA resets) after the retention period.</summary>
public sealed class AuditRetentionService(ApplicationDbContext db, TimeProvider time)
{
    public const int RetentionMonths = 12;
    private const int BatchSize = 1000;

    public async Task<int> PurgeUnlinkedAsync(CancellationToken ct)
    {
        var cutoff = time.GetUtcNow().UtcDateTime.AddMonths(-RetentionMonths);
        var total = 0;
        int deleted;
        do
        {
            deleted = await db.AuditEvents
                .Where(a => a.TenantId == null && a.OccurredAt < cutoff)
                .OrderBy(a => a.OccurredAt)
                .Take(BatchSize)
                .ExecuteDeleteAsync(ct);
            total += deleted;
        } while (deleted == BatchSize);
        return total;
    }
}
