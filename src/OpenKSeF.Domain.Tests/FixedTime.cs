namespace OpenKSeF.Domain.Tests;

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    private readonly List<FakeTimer> _timers = [];
    private DateTimeOffset _now = now;
    public DateTime UtcNow => _now.UtcDateTime;
    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by)
    {
        _now += by;
        List<FakeTimer> due;
        lock (_timers)
            due = _timers.Where(t => t.DueAt <= _now).ToList();
        foreach (var timer in due)
        {
            timer.DueAt = DateTimeOffset.MaxValue;
            timer.Callback(timer.State);
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_timers)
            _timers.Add(timer);
        return timer;
    }

    private sealed class FakeTimer(FixedTime owner, TimerCallback callback, object? state) : ITimer
    {
        public TimerCallback Callback => callback;
        public object? State => state;
        public DateTimeOffset DueAt = DateTimeOffset.MaxValue;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            DueAt = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : owner._now + dueTime;
            return true;
        }

        public void Dispose()
        {
            lock (owner._timers)
                owner._timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
