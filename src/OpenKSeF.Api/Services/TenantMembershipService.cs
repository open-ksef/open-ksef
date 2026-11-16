using Microsoft.EntityFrameworkCore;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;

namespace OpenKSeF.Api.Services;

/// <summary>Member list, role change, removal and leave; the Tenants row lock serialises changes so a company always keeps an Owner.</summary>
public class TenantMembershipService(ApplicationDbContext db)
{
    public const string LastOwnerMessage = "Firma musi mieć co najmniej jednego właściciela.";

    public enum Outcome { Done, NotFound, Forbidden, LastOwner }

    public async Task<List<TenantMemberResponse>> ListAsync(Guid tenantId)
    {
        var rows = await (
            from m in db.TenantMembers
            where m.TenantId == tenantId
            join p in db.UserProfiles on m.UserId equals p.UserId into profiles
            from p in profiles.DefaultIfEmpty()
            orderby m.CreatedAt
            select new { m.UserId, p.DisplayName, p.Email, m.Role, m.CreatedAt }).ToListAsync();

        return rows.Select(r => new TenantMemberResponse(r.UserId, r.DisplayName, r.Email, r.Role.ToString(), r.CreatedAt)).ToList();
    }

    public async Task<Outcome> ChangeRoleAsync(Guid tenantId, string actorUserId, string targetUserId, TenantRole role)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var (actor, target) = await LockAsync(tenantId, actorUserId, targetUserId);
        if (actor is null || target is null)
            return Outcome.NotFound;
        if (!TenantPermissions.CanManageRole(actor.Role, target.Role) || !TenantPermissions.CanManageRole(actor.Role, role))
            return Outcome.Forbidden;
        if (target.Role == role)
            return Outcome.Done;
        if (await IsLastOwnerAsync(target))
            return Outcome.LastOwner;

        await TenantAudit.AddAsync(db, tenantId, actorUserId, AuditEvent.MemberRoleChanged, $"{target.Role}->{role}", targetUserId);
        target.Role = role;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Outcome.Done;
    }

    public async Task<Outcome> RemoveAsync(Guid tenantId, string actorUserId, string targetUserId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var (actor, target) = await LockAsync(tenantId, actorUserId, targetUserId);
        if (actor is null)
            return Outcome.NotFound;
        var leaving = actorUserId == targetUserId;
        if (!leaving && !TenantPermissions.For(actor.Role).Contains(TenantPermissions.MembersManage))
            return Outcome.Forbidden;
        if (target is null)
            return Outcome.NotFound;
        if (!leaving && !TenantPermissions.CanManageRole(actor.Role, target.Role))
            return Outcome.Forbidden;
        if (await IsLastOwnerAsync(target))
            return Outcome.LastOwner;

        await TenantAudit.AddAsync(db, tenantId, actorUserId, leaving ? AuditEvent.MemberLeft : AuditEvent.MemberRemoved, target.Role.ToString(), targetUserId);
        db.TenantMembers.Remove(target);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Outcome.Done;
    }

    private async Task<(TenantMember? Actor, TenantMember? Target)> LockAsync(Guid tenantId, string actorUserId, string targetUserId)
    {
        var locked = await db.Database
            .SqlQuery<Guid>($"""SELECT "Id" AS "Value" FROM "Tenants" WHERE "Id" = {tenantId} FOR UPDATE""")
            .ToListAsync();
        if (locked.Count == 0)
            return (null, null);

        var members = await db.TenantMembers
            .Where(m => m.TenantId == tenantId && (m.UserId == actorUserId || m.UserId == targetUserId))
            .ToListAsync();
        return (members.SingleOrDefault(m => m.UserId == actorUserId), members.SingleOrDefault(m => m.UserId == targetUserId));
    }

    private async Task<bool> IsLastOwnerAsync(TenantMember target) =>
        target.Role == TenantRole.Owner
        && await db.TenantMembers.CountAsync(m => m.TenantId == target.TenantId && m.Role == TenantRole.Owner) == 1;
}
