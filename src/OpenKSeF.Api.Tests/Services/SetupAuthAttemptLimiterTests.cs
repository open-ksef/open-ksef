using OpenKSeF.Api.Services;

namespace OpenKSeF.Api.Tests.Services;

public class SetupAuthAttemptLimiterTests
{
    [Fact]
    public void Release_AfterWindowRollover_DoesNotRefundIntoNewWindow()
    {
        var time = new MovableTime();
        var limiter = new SetupAuthAttemptLimiter(time);
        Assert.True(limiter.TryAcquire("10.0.0.1", out var oldWindow, out _));

        time.Now += SetupAuthAttemptLimiter.Window;
        for (var i = 0; i < SetupAuthAttemptLimiter.PermitLimit; i++)
            Assert.True(limiter.TryAcquire("10.0.0.1", out _, out _));
        limiter.Release("10.0.0.1", oldWindow);

        Assert.False(limiter.TryAcquire("10.0.0.1", out _, out _));
    }

    private sealed class MovableTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
