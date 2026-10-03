using NodaTime;

namespace SCalenderPlus.Application.Common;

public static class ClockExtensions
{
    /// <summary>
    /// The current instant at PostgreSQL's <c>timestamptz</c> precision (microseconds), so a representation built
    /// from the saved entity equals the one read back later (stable ETags).
    /// </summary>
    public static Instant Now(this IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var ticks = clock.GetCurrentInstant().ToUnixTimeTicks();
        return Instant.FromUnixTimeTicks(ticks - (ticks % (NodaConstants.TicksPerMillisecond / 1000)));
    }
}
