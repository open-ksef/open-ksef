using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;

namespace OpenKSeF.Api.Services;

/// <summary>E-mail invites: a verified existing account gets access at once, anyone else a pending link; the Tenants row lock serialises invites, limits and accepts per company.</summary>
public class TenantInviteService(ApplicationDbContext db, INotificationService notifications, IEmailService email, TimeProvider time, ILogger<TenantInviteService> logger)
{
    public const string AlreadyMemberMessage = "Ta osoba ma już dostęp do tej firmy.";
    public const string LimitMessage = "Osiągnięto dzienny limit zaproszeń dla tej firmy (20). Spróbuj ponownie później.";
    public const string PersonLimitMessage = "Osiągnięto Twój dzienny limit zaproszeń (50). Spróbuj ponownie później.";
    public const string InvalidMessage = "Zaproszenie jest nieprawidłowe lub wygasło.";
    public const string WrongEmailMessage = "To zaproszenie jest dla innego adresu e-mail.";
    public const int TenantDailyLimit = 20;
    public const int ActorDailyLimit = 50;
    public static readonly TimeSpan ValidFor = TimeSpan.FromDays(14);
    private static readonly string[] LimitedActions = [AuditEvent.InviteCreated, AuditEvent.InviteResent, AuditEvent.AccessGranted];

    public enum Outcome { Done, NotFound, Forbidden, AlreadyMember, Limited, PersonLimited, WrongEmail, NotConfigured }

    public static string Normalize(string? address) => address?.Trim().ToLowerInvariant() ?? "";

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task<(Outcome, CreateInviteResponse?)> CreateAsync(Guid tenantId, string actorUserId, string address, TenantRole role, string? baseUrl)
    {
        var invitee = Normalize(address);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var (tenant, actor) = await LockAsync(tenantId, actorUserId);
        if (tenant is null || actor is null)
            return (Outcome.NotFound, null);
        if (!TenantPermissions.CanManageRole(actor.Role, role))
            return (Outcome.Forbidden, null);
        if (baseUrl is null)
            return (Outcome.NotConfigured, null);
        if (await db.TenantMembers.AnyAsync(m => m.TenantId == tenantId && db.UserProfiles.Any(p => p.UserId == m.UserId && p.Email == invitee)))
            return (Outcome.AlreadyMember, null);
        var invite = await db.TenantInvites.FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Email == invitee && i.Status == TenantInviteStatus.Pending);
        if (invite is not null && !TenantPermissions.CanManageRole(actor.Role, invite.Role))
            return (Outcome.Forbidden, null);
        if (await LimitAsync(tenantId, actorUserId) is { } limited)
            return (limited, null);

        var now = time.GetUtcNow().UtcDateTime;
        var company = CompanyName(tenant);
        var account = await db.UserProfiles
            .Where(p => p.Email == invitee && p.EmailVerified)
            .OrderByDescending(p => p.UpdatedAt)
            .FirstOrDefaultAsync();
        if (account is not null)
        {
            db.TenantMembers.Add(new TenantMember { TenantId = tenantId, UserId = account.UserId, Role = role, CreatedAt = now });
            invite?.Accept(account.UserId, now);
            await TenantAudit.AddAsync(db, tenantId, actorUserId, AuditEvent.AccessGranted, role.ToString(), account.UserId, invitee);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            await notifications.NotifyAccessGrantedAsync(account.UserId, tenantId, company);
            var granter = await InviterNameAsync(actorUserId);
            await TrySendAsync(tenantId, () => email.SendAccessGrantedEmailAsync(invitee, tenantId, company, tenant.Nip, role, granter, baseUrl));
            return (Outcome.Done, new CreateInviteResponse("granted", DisplayName: account.DisplayName ?? account.Email));
        }

        if (invite is null)
        {
            invite = new TenantInvite { Id = Guid.NewGuid(), TenantId = tenantId, Email = invitee, TokenHash = "", InvitedByUserId = actorUserId };
            db.TenantInvites.Add(invite);
        }

        invite.Role = role;
        invite.InvitedByUserId = actorUserId;
        invite.CreatedAt = now;
        var token = Renew(invite, now);
        await TenantAudit.AddAsync(db, tenantId, actorUserId, AuditEvent.InviteCreated, role.ToString(), targetEmail: invitee);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        var link = Link(baseUrl, token);
        var inviter = await InviterNameAsync(actorUserId);
        var emailSent = await TrySendAsync(tenantId, () => email.SendInviteEmailAsync(invitee, tenantId, company, tenant.Nip, role, inviter, link, invite.ExpiresAt));
        return (Outcome.Done, new CreateInviteResponse("pending", null, invite.Id, link, invite.ExpiresAt, emailSent));
    }

    public async Task<List<TenantInviteResponse>> ListAsync(Guid tenantId)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var rows = await (
            from i in db.TenantInvites
            where i.TenantId == tenantId && i.Status == TenantInviteStatus.Pending
            join p in db.UserProfiles on i.InvitedByUserId equals p.UserId into profiles
            from p in profiles.DefaultIfEmpty()
            orderby i.CreatedAt
            select new { i.Id, i.Email, i.Role, InvitedByName = p.DisplayName ?? p.Email, i.CreatedAt, i.ExpiresAt }).ToListAsync();

        return rows.Select(r => new TenantInviteResponse(r.Id, r.Email, r.Role.ToString(), r.InvitedByName, r.CreatedAt, r.ExpiresAt, r.ExpiresAt <= now)).ToList();
    }

    public async Task<(Outcome, InviteLinkResponse?)> ResendAsync(Guid tenantId, string actorUserId, Guid inviteId, string? baseUrl)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var (outcome, tenant, invite) = await PendingForActorAsync(tenantId, actorUserId, inviteId);
        if (outcome != Outcome.Done)
            return (outcome, null);
        if (baseUrl is null)
            return (Outcome.NotConfigured, null);
        if (await LimitAsync(tenantId, actorUserId) is { } limited)
            return (limited, null);

        var token = Renew(invite!, time.GetUtcNow().UtcDateTime);
        await TenantAudit.AddAsync(db, tenantId, actorUserId, AuditEvent.InviteResent, invite!.Role.ToString(), targetEmail: invite.Email);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        var link = Link(baseUrl, token);
        var inviter = await InviterNameAsync(actorUserId);
        var emailSent = await TrySendAsync(tenantId, () => email.SendInviteEmailAsync(invite.Email, tenantId, CompanyName(tenant!), tenant!.Nip, invite.Role, inviter, link, invite.ExpiresAt));
        return (Outcome.Done, new InviteLinkResponse(link, invite.ExpiresAt, emailSent));
    }

    public async Task<Outcome> RevokeAsync(Guid tenantId, string actorUserId, Guid inviteId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var (outcome, _, invite) = await PendingForActorAsync(tenantId, actorUserId, inviteId);
        if (outcome != Outcome.Done)
            return outcome;

        invite!.Status = TenantInviteStatus.Revoked;
        await TenantAudit.AddAsync(db, tenantId, actorUserId, AuditEvent.InviteRevoked, invite.Role.ToString(), targetEmail: invite.Email);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Outcome.Done;
    }

    public async Task<(Outcome, AcceptInviteResponse?)> AcceptAsync(string userId, string? callerEmail, string token)
    {
        var hash = Hash(token);
        var tenantId = await db.TenantInvites.Where(i => i.TokenHash == hash).Select(i => (Guid?)i.TenantId).FirstOrDefaultAsync();
        if (tenantId is null)
            return (Outcome.NotFound, null);

        await using var transaction = await db.Database.BeginTransactionAsync();
        var (tenant, member) = await LockAsync(tenantId.Value, userId);
        var now = time.GetUtcNow().UtcDateTime;
        var invite = await db.TenantInvites.SingleOrDefaultAsync(i => i.TokenHash == hash);
        if (tenant is null || invite is null || invite.Status != TenantInviteStatus.Pending || invite.ExpiresAt <= now)
            return (Outcome.NotFound, null);
        if (Normalize(callerEmail) != invite.Email)
            return (Outcome.WrongEmail, null);

        if (member is null)
        {
            member = new TenantMember { TenantId = invite.TenantId, UserId = userId, Role = invite.Role, CreatedAt = now };
            db.TenantMembers.Add(member);
        }

        invite.Accept(userId, now);
        await TenantAudit.AddAsync(db, invite.TenantId, userId, AuditEvent.InviteAccepted, member.Role.ToString(), userId, invite.Email);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return (Outcome.Done, new AcceptInviteResponse(tenant.Id, CompanyName(tenant), member.Role.ToString()));
    }

    private async Task<(Outcome, Tenant?, TenantInvite?)> PendingForActorAsync(Guid tenantId, string actorUserId, Guid inviteId)
    {
        var (tenant, actor) = await LockAsync(tenantId, actorUserId);
        var invite = await db.TenantInvites.FirstOrDefaultAsync(i => i.Id == inviteId && i.TenantId == tenantId && i.Status == TenantInviteStatus.Pending);
        if (tenant is null || actor is null || invite is null)
            return (Outcome.NotFound, null, null);
        return TenantPermissions.CanManageRole(actor.Role, invite.Role) ? (Outcome.Done, tenant, invite) : (Outcome.Forbidden, null, null);
    }

    public static async Task<Tenant?> LockTenantAsync(ApplicationDbContext db, Guid tenantId) =>
        (await db.Tenants.FromSql($"""SELECT * FROM "Tenants" WHERE "Id" = {tenantId} FOR UPDATE""").ToListAsync()).SingleOrDefault();

    private async Task<(Tenant? Tenant, TenantMember? Member)> LockAsync(Guid tenantId, string userId)
    {
        var tenant = await LockTenantAsync(db, tenantId);
        if (tenant is null)
            return (null, null);
        return (tenant, await db.TenantMembers.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId));
    }

    private async Task<Outcome?> LimitAsync(Guid tenantId, string actorUserId)
    {
        var since = time.GetUtcNow().UtcDateTime.AddDays(-1);
        var sent = db.AuditEvents.Where(a => a.OccurredAt > since && LimitedActions.Contains(a.Action));
        if (await sent.CountAsync(a => a.TenantId == tenantId) >= TenantDailyLimit)
            return Outcome.Limited;
        return await sent.CountAsync(a => a.ActorUserId == actorUserId) >= ActorDailyLimit ? Outcome.PersonLimited : null;
    }

    private static string Renew(TenantInvite invite, DateTime now)
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        invite.TokenHash = Hash(token);
        invite.ExpiresAt = now + ValidFor;
        return token;
    }

    private async Task<string> InviterNameAsync(string userId) =>
        await db.UserProfiles.Where(p => p.UserId == userId).Select(p => p.DisplayName ?? p.Email).FirstOrDefaultAsync() ?? "Użytkownik OpenKSeF";

    private async Task<bool> TrySendAsync(Guid tenantId, Func<Task<bool>> send)
    {
        try
        {
            return await send();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invite e-mail failed for tenant {TenantId}", tenantId);
            return false;
        }
    }

    private static string Link(string baseUrl, string token) => $"{baseUrl}/zaproszenie#{token}";

    private static string CompanyName(Tenant tenant) => string.IsNullOrWhiteSpace(tenant.DisplayName) ? $"NIP {tenant.Nip}" : tenant.DisplayName;
}
