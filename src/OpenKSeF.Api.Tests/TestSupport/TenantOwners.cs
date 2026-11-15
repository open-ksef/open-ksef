using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;

namespace OpenKSeF.Api.Tests.TestSupport;

public static class TenantOwners
{
    public static void AddOwnerMembers(this ApplicationDbContext db)
    {
        foreach (var tenant in db.ChangeTracker.Entries<Tenant>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList())
            db.TenantMembers.Add(new TenantMember { TenantId = tenant.Id, UserId = tenant.UserId, Role = TenantRole.Owner, CreatedAt = tenant.CreatedAt });
    }
}
