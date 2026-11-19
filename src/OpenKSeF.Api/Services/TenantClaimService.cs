using KSeF.Client.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Services;

/// <summary>Company claim by a KSeF token with CredentialsManage (REQ-0054): the proof runs outside the transaction, then under the NIP and Tenants locks the claimant replaces every Owner.</summary>
public class TenantClaimService(
    ApplicationDbContext db,
    IKSeFGateway gateway,
    IKSeFIssuingEnvironment environment,
    TenantClaimLimiter limiter,
    IEmailService email,
    ILogger<TenantClaimService> logger)
{
    public const string ProductionOnlyMessage = "Przejęcie firmy tokenem działa tylko na środowisku PRODUKCJA KSeF. Napisz do administratora serwera.";
    public const string NoCompanyMessage = "Nie ma firmy z tym NIP-em do przejęcia. Dodaj ją zwykłym formularzem.";
    public const string DuplicatesMessage = "Na tym serwerze jest więcej niż jedna firma z tym NIP-em. Napisz do administratora serwera.";
    public const string LimitedMessage = "Za dużo prób przejęcia firmy. Spróbuj później.";
    public const string UnavailableMessage = "KSeF nie odpowiada. Spróbuj później.";
    public const string RequiredPermission = "CredentialsManage";

    public enum Outcome { Claimed, Invalid, Conflict, Limited, Unavailable }

    public record Result(Outcome Outcome, string? Error = null, TenantResponse? Tenant = null);

    public static string NotProvenMessage(string nip) => $"Ten token nie potwierdza dostępu do KSeF dla NIP {nip}. Sprawdź token i NIP.";

    public async Task<Result> ClaimAsync(string userId, string rawNip, string token, CancellationToken ct = default)
    {
        if (!NipValidator.HasValidChecksum(rawNip))
            return new Result(Outcome.Invalid, TenantNipGuard.InvalidNipMessage);

        var nip = NipValidator.Normalize(rawNip);
        if (environment.Current != "production")
            return new Result(Outcome.Conflict, ProductionOnlyMessage);
        if (await RefuseAsync(nip, userId) is { Refused: { } refused })
            return refused;
        if (token.Split('|') is [_, var context, _, ..] && context != $"nip-{nip}")
            return NotProven(userId, nip, "context");
        if (!limiter.TryAcquire(userId, nip))
            return new Result(Outcome.Limited, LimitedMessage);

        KSeFTokenGrant? grant;
        try
        {
            grant = await gateway.CheckTokenAsync(nip, token, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TimeoutException or OperationCanceledException or KsefApiException)
        {
            logger.LogWarning("Company claim by user {UserId} for NIP {Nip} refused: unavailable ({Error})", userId, nip, e.GetType().Name);
            return new Result(Outcome.Unavailable, UnavailableMessage);
        }

        if (grant is null)
            return NotProven(userId, nip, "rejected");
        if (!grant.Permissions.Contains(RequiredPermission))
            return NotProven(userId, nip, "permissions");

        return await ReplaceOwnersAsync(userId, nip, grant);
    }

    private async Task<Result> ReplaceOwnersAsync(string userId, string nip, KSeFTokenGrant grant)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await TenantNipGuard.LockAsync(db, nip);
        var (refused, tenantId) = await RefuseAsync(nip, userId);
        if (refused is not null)
            return refused;
        var tenant = await TenantInviteService.LockTenantAsync(db, tenantId);
        if (tenant is null)
            return new Result(Outcome.Conflict, NoCompanyMessage);

        var owners = await db.TenantMembers.Where(m => m.TenantId == tenantId && m.Role == TenantRole.Owner).ToListAsync();
        var ownerIds = owners.Select(o => o.UserId).ToList();
        var ownerEmails = await db.UserProfiles
            .Where(p => ownerIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.Email })
            .ToListAsync();

        await TenantAudit.AddAsync(db, tenantId, userId, AuditEvent.OwnershipClaimed, $"{string.Join(',', grant.Permissions)};auth:{grant.AuthReferenceNumber}");
        foreach (var owner in owners)
        {
            await TenantAudit.AddAsync(db, tenantId, userId, AuditEvent.MemberRemoved, nameof(TenantRole.Owner), owner.UserId);
            db.TenantMembers.Remove(owner);
        }

        var now = DateTime.UtcNow;
        var member = new TenantMember { TenantId = tenantId, UserId = userId, Role = TenantRole.Owner, CreatedAt = now };
        db.TenantMembers.Add(member);
        if (ownerEmails.FirstOrDefault(o => TenantMembershipService.SameAddress(o.Email, tenant.NotificationEmail)) is { } cleared)
        {
            await TenantAudit.AddAsync(db, tenantId, userId, AuditEvent.NotificationEmailCleared, targetUserId: cleared.UserId, targetEmail: tenant.NotificationEmail);
            tenant.NotificationEmail = null;
            tenant.UpdatedAt = now;
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        logger.LogInformation("Company {TenantId} claimed by user {UserId}; {Count} former Owners removed", tenantId, userId, owners.Count);

        var company = string.IsNullOrWhiteSpace(tenant.DisplayName) ? $"NIP {tenant.Nip}" : tenant.DisplayName;
        var claimant = await db.UserProfiles.Where(p => p.UserId == userId).Select(p => p.DisplayName ?? p.Email).FirstOrDefaultAsync() ?? "Użytkownik OpenKSeF";
        foreach (var address in ownerEmails.Select(o => o.Email).OfType<string>().Where(e => !string.IsNullOrWhiteSpace(e)))
            await TrySendAsync(tenantId, () => email.SendOwnershipClaimedEmailAsync(address, tenantId, company, tenant.Nip, claimant));

        return new Result(Outcome.Claimed, Tenant: TenantResponse.From(tenant, member));
    }

    private async Task<(Result? Refused, Guid TenantId)> RefuseAsync(string nip, string userId)
    {
        if (await TenantNipGuard.MemberCompanyNameAsync(db, nip, userId) is { } name)
            return (new Result(Outcome.Conflict, TenantNipGuard.AlreadyMemberMessage(name)), default);

        var active = await db.Tenants.Where(t => t.Nip == nip && !t.UserId.StartsWith("deleted:")).Select(t => t.Id).Take(2).ToListAsync();
        return active.Count switch
        {
            0 => (new Result(Outcome.Conflict, NoCompanyMessage), default),
            1 => (null, active[0]),
            _ => (new Result(Outcome.Conflict, DuplicatesMessage), default),
        };
    }

    private Result NotProven(string userId, string nip, string reason)
    {
        logger.LogInformation("Company claim by user {UserId} for NIP {Nip} refused: {Reason}", userId, nip, reason);
        return new Result(Outcome.Invalid, NotProvenMessage(nip));
    }

    private async Task TrySendAsync(Guid tenantId, Func<Task<bool>> send)
    {
        try
        {
            await send();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ownership claimed e-mail failed for tenant {TenantId}", tenantId);
        }
    }
}
