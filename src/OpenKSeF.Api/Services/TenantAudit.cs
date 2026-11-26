using Microsoft.EntityFrameworkCore;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;

namespace OpenKSeF.Api.Services;

/// <summary>Appends a tenant audit row with the actor's and target's names as they are now (saved with the caller's next SaveChanges), and pages a company's rows for the REQ-0057 history view.</summary>
public static class TenantAudit
{
    private const int PageSize = 50;

    private static readonly HashSet<string> WithDetails =
    [
        AuditEvent.InvoicePaid, AuditEvent.InvoiceUnpaid, AuditEvent.InvoicesExported, AuditEvent.CredentialSet, AuditEvent.CredentialDeleted,
        AuditEvent.InviteCreated, AuditEvent.InviteResent, AuditEvent.InviteRevoked, AuditEvent.InviteAccepted, AuditEvent.AccessGranted,
        AuditEvent.MemberRoleChanged, AuditEvent.MemberRemoved, AuditEvent.MemberLeft, AuditEvent.NotificationEmailCleared,
    ];

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

    public static async Task<TenantHistoryPageResponse> ListAsync(ApplicationDbContext db, Guid tenantId, Guid? before)
    {
        var query = db.AuditEvents.AsNoTracking().Where(a => a.TenantId == tenantId);
        if (before is { } cursorId)
        {
            var cursor = await db.AuditEvents.Where(a => a.Id == cursorId && a.TenantId == tenantId).Select(a => (DateTime?)a.OccurredAt).SingleOrDefaultAsync();
            if (cursor is null)
                return new([], false);
            query = query.Where(a => a.OccurredAt < cursor || (a.OccurredAt == cursor && a.Id.CompareTo(cursorId) < 0));
        }

        var rows = await query.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id).Take(PageSize + 1).ToListAsync();
        var page = rows.Take(PageSize).ToList();

        var invoiceIds = page.Where(a => a.Action is AuditEvent.InvoicePaid or AuditEvent.InvoiceUnpaid)
            .Select(a => Guid.TryParse(a.Target, out var id) ? id : Guid.Empty).Distinct().ToList();
        var invoices = await db.InvoiceHeaders.Where(i => i.TenantId == tenantId && invoiceIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id.ToString(), i => i.InvoiceNumber ?? i.KSeFInvoiceNumber);
        var targetIds = page.Select(a => a.TargetUserId).OfType<string>().Distinct().ToList();
        var names = await db.UserProfiles.Where(p => targetIds.Contains(p.UserId) && p.DisplayName != null)
            .ToDictionaryAsync(p => p.UserId, p => p.DisplayName!);

        return new(page.Select(a =>
        {
            var actor = a.ActorName ?? a.ActorEmail;
            if (!WithDetails.Contains(a.Action))
                return new TenantHistoryEventResponse(a.Id, a.OccurredAt, a.Action, actor, actor is null, null, null, null);
            var target = a.Action is AuditEvent.InvoicePaid or AuditEvent.InvoiceUnpaid ? invoices.GetValueOrDefault(a.Target ?? "") : a.Target;
            return new TenantHistoryEventResponse(a.Id, a.OccurredAt, a.Action, actor, actor is null,
                a.TargetUserId is null ? null : names.GetValueOrDefault(a.TargetUserId), a.TargetEmail, target);
        }).ToList(), rows.Count > PageSize);
    }
}
