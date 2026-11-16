using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Data;

namespace OpenKSeF.Api.Tests.Services;

public class UserProfileServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task EnsureAsync_FirstCall_CreatesProfileWithNormalizedEmail()
    {
        await Service().EnsureAsync(User(" B@Example.Invalid ", "true", "Bogna Test"));

        var profile = await _db.UserProfiles.SingleAsync();
        Assert.Equal(("user-1", "b@example.invalid", true, "Bogna Test"), (profile.UserId, profile.Email, profile.EmailVerified, profile.DisplayName));
    }

    [Fact]
    public async Task EnsureAsync_NameLongerThanColumn_IsTruncatedTo200()
    {
        await Service().EnsureAsync(User("b@example.invalid", "true", new string('a', 300)));

        Assert.Equal(new string('a', 200), (await _db.UserProfiles.SingleAsync()).DisplayName);
    }

    [Fact]
    public async Task EnsureAsync_SameClaims_DoesNotRewrite_EvenWithColdCache()
    {
        await Service().EnsureAsync(User("b@example.invalid", "false", "Bogna Test"));
        var written = (await _db.UserProfiles.AsNoTracking().SingleAsync()).UpdatedAt;
        await Task.Delay(20);

        await Service().EnsureAsync(User("b@example.invalid", "false", "Bogna Test"));

        Assert.Equal(written, (await _db.UserProfiles.AsNoTracking().SingleAsync()).UpdatedAt);
    }

    [Fact]
    public async Task EnsureAsync_ChangedClaims_UpdatesProfile()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        await Service(cache).EnsureAsync(User("b@example.invalid", "false", "Bogna Test"));

        await Service(cache).EnsureAsync(User("b@example.invalid", "true", "Bogna Nowak"));

        var profile = await _db.UserProfiles.AsNoTracking().SingleAsync();
        Assert.True(profile.EmailVerified);
        Assert.Equal("Bogna Nowak", profile.DisplayName);
    }

    [Fact]
    public async Task EnsureAsync_CacheHit_SkipsDatabase()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        await Service(cache).EnsureAsync(User("b@example.invalid", "true", "Bogna Test"));
        _db.UserProfiles.Remove(await _db.UserProfiles.SingleAsync());
        await _db.SaveChangesAsync();

        await Service(cache).EnsureAsync(User("b@example.invalid", "true", "Bogna Test"));

        Assert.Empty(_db.UserProfiles);
    }

    private UserProfileService Service(IMemoryCache? cache = null) => new(_db, cache ?? new MemoryCache(new MemoryCacheOptions()));

    private static ClaimsPrincipal User(string email, string emailVerified, string name) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, "user-1"),
        new Claim(ClaimTypes.Email, email),
        new Claim("email_verified", emailVerified),
        new Claim("name", name),
    ], "test"));

    public void Dispose() => _db.Dispose();
}
