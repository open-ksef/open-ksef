using OpenKSeF.Api.Services;

namespace OpenKSeF.Api.Tests.Services;

public class TenantClaimLimiterTests
{
    private readonly MovableTime _time = new();

    [Fact]
    public void User_FivePerSlidingHour()
    {
        var limiter = new TenantClaimLimiter(_time);
        for (var i = 0; i < 5; i++)
        {
            Assert.True(limiter.TryAcquire("b", $"nip-{i}"));
            _time.Now += TimeSpan.FromMinutes(10);
        }

        Assert.False(limiter.TryAcquire("b", "nip-x"));
        Assert.True(limiter.TryAcquire("other", "nip-x"));
        _time.Now += TimeSpan.FromMinutes(10);
        Assert.True(limiter.TryAcquire("b", "nip-x"));
        Assert.False(limiter.TryAcquire("b", "nip-x"));
    }

    [Fact]
    public void Nip_TenPerSlidingDay_AcrossUsers_RefusalCountsNothing()
    {
        var limiter = new TenantClaimLimiter(_time);
        for (var i = 0; i < 10; i++)
            Assert.True(limiter.TryAcquire($"user-{i}", "1111111111"));

        Assert.False(limiter.TryAcquire("b", "1111111111"));
        for (var i = 0; i < 5; i++)
            Assert.True(limiter.TryAcquire("b", $"other-{i}"));
        _time.Now += TimeSpan.FromDays(1);
        Assert.True(limiter.TryAcquire("c", "1111111111"));
    }

    private sealed class MovableTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
