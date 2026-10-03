using NodaTime;

namespace SCalenderPlus.IntegrationTests.Infrastructure;

/// <summary>Test clock: starts at the real current time and only moves when advanced.</summary>
internal sealed class MutableClock : IClock
{
    private long _ticks = SystemClock.Instance.GetCurrentInstant().ToUnixTimeTicks();

    public Instant GetCurrentInstant() => Instant.FromUnixTimeTicks(Interlocked.Read(ref _ticks));

    public void Advance(Duration duration) => Interlocked.Add(ref _ticks, duration.BclCompatibleTicks);
}
