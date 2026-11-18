using OpenKSeF.Sync;

namespace OpenKSeF.Domain.Tests.Services;

public class KSeFRequestLimitsTests
{
    private static readonly TimeSpan Spacing = TimeSpan.FromMilliseconds(150);
    private readonly FixedTime _time = new(new DateTimeOffset(2026, 11, 18, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task WaitTurn_QueuedCallersGetSuccessiveSlots_OnTheInjectedClock()
    {
        var sut = new KSeFRequestLimits(_time);

        var first = sut.WaitTurnAsync("1111111111", Spacing, CancellationToken.None);
        var second = sut.WaitTurnAsync("1111111111", Spacing, CancellationToken.None);
        var third = sut.WaitTurnAsync("1111111111", Spacing, CancellationToken.None);

        Assert.True(first.IsCompletedSuccessfully);
        _time.Advance(Spacing - TimeSpan.FromMilliseconds(1));
        Assert.False(second.IsCompleted);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(third.IsCompleted);
        _time.Advance(Spacing);
        await third.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void WaitTurn_AfterSpacingElapsed_OrOtherKey_OrZeroSpacing_DoesNotWait()
    {
        var sut = new KSeFRequestLimits(_time);

        Assert.True(sut.WaitTurnAsync("1111111111", Spacing, CancellationToken.None).IsCompletedSuccessfully);
        Assert.True(sut.WaitTurnAsync("1111111111:metadata", Spacing, CancellationToken.None).IsCompletedSuccessfully);
        Assert.True(sut.WaitTurnAsync("zero", TimeSpan.Zero, CancellationToken.None).IsCompletedSuccessfully);
        Assert.True(sut.WaitTurnAsync("zero", TimeSpan.Zero, CancellationToken.None).IsCompletedSuccessfully);
        _time.Advance(Spacing);
        Assert.True(sut.WaitTurnAsync("1111111111", Spacing, CancellationToken.None).IsCompletedSuccessfully);
    }

    [Fact]
    public async Task WaitTurn_FrozenClockWithSystemTimers_WaitsOneSpacing_DoesNotSpin()
    {
        var sut = new KSeFRequestLimits(new FrozenTime());

        await sut.WaitTurnAsync("1111111111", Spacing, CancellationToken.None);
        await sut.WaitTurnAsync("1111111111", Spacing, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Block_LastsUntilRetryAfter_OnTheInjectedClock()
    {
        var sut = new KSeFRequestLimits(_time);

        sut.Block("1111111111", TimeSpan.FromSeconds(30));

        Assert.True(sut.IsBlocked("1111111111"));
        Assert.False(sut.IsBlocked("1111111112"));
        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.True(sut.IsBlocked("1111111111"));
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(sut.IsBlocked("1111111111"));
    }

    private sealed class FrozenTime : TimeProvider
    {
        private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
