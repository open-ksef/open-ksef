using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Services;

/// <summary>Keeps a UserProfiles row in step with the caller's token claims and claims pending invites once the e-mail is verified; the cache keeps it to one DB check per claim set per 15 min.</summary>
public class UserProfileService(ApplicationDbContext db, IMemoryCache cache)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);
    private const int MaxDisplayName = 200;

    public async Task EnsureAsync(ClaimsPrincipal user)
    {
        var userId = Claim(user, ClaimTypes.NameIdentifier, "sub");
        if (userId is null)
            return;

        var email = Claim(user, ClaimTypes.Email, "email")?.Trim().ToLowerInvariant();
        var emailVerified = string.Equals(Claim(user, "email_verified"), "true", StringComparison.OrdinalIgnoreCase);
        var displayName = Claim(user, "name", ClaimTypes.Name)?.Trim() is { } name ? name[..Math.Min(name.Length, MaxDisplayName)] : null;
        var key = (nameof(UserProfileService), userId, email, emailVerified, displayName);
        if (cache.TryGetValue(key, out _))
            return;

        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        if (profile is null)
        {
            profile = new UserProfile { UserId = userId };
            db.UserProfiles.Add(profile);
        }
        else if (profile.Email == email && profile.EmailVerified == emailVerified && profile.DisplayName == displayName)
        {
            cache.Set(key, true, CacheFor);
            return;
        }

        profile.Email = email;
        profile.EmailVerified = emailVerified;
        profile.DisplayName = displayName;
        profile.UpdatedAt = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync();
            if (emailVerified && email is not null)
                await ClaimInvitesAsync(userId, email);
        }
        catch (DbUpdateException e) when (IssuedInvoiceService.IsUniqueViolation(e))
        {
            db.ChangeTracker.Clear();
            return;
        }

        cache.Set(key, true, CacheFor);
    }

    private async Task ClaimInvitesAsync(string userId, string email)
    {
        var now = DateTime.UtcNow;
        var invites = await db.TenantInvites
            .Where(i => i.Email == email && i.Status == TenantInviteStatus.Pending && i.ExpiresAt > now)
            .ToListAsync();
        if (invites.Count == 0)
            return;

        foreach (var invite in invites)
        {
            if (!await db.TenantMembers.AnyAsync(m => m.TenantId == invite.TenantId && m.UserId == userId))
                db.TenantMembers.Add(new TenantMember { TenantId = invite.TenantId, UserId = userId, Role = invite.Role, CreatedAt = now });
            invite.Accept(userId, now);
            await TenantAudit.AddAsync(db, invite.TenantId, userId, AuditEvent.InviteAccepted, invite.Email, userId);
        }

        await db.SaveChangesAsync();
    }

    private static string? Claim(ClaimsPrincipal user, params string[] types) =>
        types.Select(t => user.FindFirst(t)?.Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
