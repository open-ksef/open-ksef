using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;

namespace OpenKSeF.Api.Controllers;

internal static class TenantOwnership
{
    public static Task<bool> OwnsTenantAsync(this ApplicationDbContext db, Guid tenantId, string userId, CancellationToken ct = default) =>
        db.Tenants.AnyAsync(t => t.Id == tenantId && t.UserId == userId, ct);
}
