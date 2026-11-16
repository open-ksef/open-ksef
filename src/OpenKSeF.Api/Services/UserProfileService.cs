using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Api.Services;

/// <summary>Keeps a UserProfiles row in step with the caller's token claims; the cache keeps it to one DB check per claim set per 15 min.</summary>
public class UserProfileService(ApplicationDbContext db, IMemoryCache cache)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);

    public async Task EnsureAsync(ClaimsPrincipal user)
    {
        var userId = Claim(user, ClaimTypes.NameIdentifier, "sub");
        if (userId is null)
            return;

        var email = Claim(user, ClaimTypes.Email, "email")?.Trim().ToLowerInvariant();
        var emailVerified = string.Equals(Claim(user, "email_verified"), "true", StringComparison.OrdinalIgnoreCase);
        var displayName = Claim(user, "name", ClaimTypes.Name)?.Trim();
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
        }
        catch (DbUpdateException e) when (IssuedInvoiceService.IsUniqueViolation(e))
        {
            db.Entry(profile).State = EntityState.Detached;
            return;
        }

        cache.Set(key, true, CacheFor);
    }

    private static string? Claim(ClaimsPrincipal user, params string[] types) =>
        types.Select(t => user.FindFirst(t)?.Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
