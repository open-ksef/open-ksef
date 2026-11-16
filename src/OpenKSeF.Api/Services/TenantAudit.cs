using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;

namespace OpenKSeF.Api.Services;

/// <summary>Appends a tenant audit row with the actor's and target's names as they are now; saved with the caller's next SaveChanges.</summary>
public static class TenantAudit
{
    public static async Task AddAsync(ApplicationDbContext db, Guid tenantId, string actorUserId, string action, string? target = null, string? targetUserId = null, string? targetEmail = null)
    {
        var profiles = await db.UserProfiles
            .Where(p => p.UserId == actorUserId || p.UserId == targetUserId)
            .Select(p => new { p.UserId, p.Email, Name = p.DisplayName ?? p.Email })
            .ToListAsync();
        var actor = profiles.FirstOrDefault(p => p.UserId == actorUserId);

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            OccurredAt = DateTime.UtcNow,
            TenantId = tenantId,
            ActorUserId = actorUserId,
            ActorEmail = actor?.Email,
            ActorName = actor?.Name,
            Action = action,
            TargetUserId = targetUserId,
            TargetEmail = targetEmail ?? profiles.FirstOrDefault(p => p.UserId == targetUserId)?.Email,
            Target = target,
        });
    }
}
