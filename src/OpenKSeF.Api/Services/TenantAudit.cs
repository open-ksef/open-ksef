using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;

namespace OpenKSeF.Api.Services;

/// <summary>Appends a tenant audit row with the actor's name as it is now; saved with the caller's next SaveChanges.</summary>
public static class TenantAudit
{
    public static async Task AddAsync(ApplicationDbContext db, Guid tenantId, string actorUserId, string action, string? target = null)
    {
        var actor = await db.UserProfiles
            .Where(p => p.UserId == actorUserId)
            .Select(p => new { p.Email, Name = p.DisplayName ?? p.Email })
            .FirstOrDefaultAsync();

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            OccurredAt = DateTime.UtcNow,
            TenantId = tenantId,
            ActorUserId = actorUserId,
            ActorEmail = actor?.Email,
            ActorName = actor?.Name,
            Action = action,
            Target = target,
        });
    }
}
