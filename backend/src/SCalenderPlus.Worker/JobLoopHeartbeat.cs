using NodaTime;

namespace SCalenderPlus.Worker;

/// <summary>Last time the job loop reported progress; read by <see cref="JobLoopHealthCheck"/>.</summary>
internal sealed class JobLoopHeartbeat(IClock clock)
{
    private long _lastBeatTicks = long.MinValue;

    public Instant? LastBeat
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastBeatTicks);
            return ticks == long.MinValue ? null : Instant.FromUnixTimeTicks(ticks);
        }
    }

    public void Beat() => Interlocked.Exchange(ref _lastBeatTicks, clock.GetCurrentInstant().ToUnixTimeTicks());
}
