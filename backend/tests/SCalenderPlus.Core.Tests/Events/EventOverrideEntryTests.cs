using NodaTime;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Core.Tests.Events;

public sealed class EventOverrideEntryTests
{
    private static readonly Guid _eventId = new("50000000-0000-0000-0000-000000000001");
    private static readonly Guid _groupId = new("10000000-0000-0000-0000-000000000001");

    public static TheoryData<Principal> Principals => new()
    {
        Principal.User(new Guid("30000000-0000-0000-0000-000000000001")),
        Principal.Group(_groupId, GroupRole.Admin),
        Principal.Anonymous,
        Principal.Everyone,
    };

    [Theory]
    [MemberData(nameof(Principals))]
    public void An_entry_round_trips_to_the_engine_override(Principal principal)
    {
        var entry = EventOverrideEntry.For(_eventId, new EventOverride(principal, EventLevel.Read));

        Assert.Equal(_eventId, entry.EventId);
        Assert.NotEqual(Guid.Empty, entry.Id);
        Assert.Equal(principal, entry.Principal);
        Assert.Equal(new EventOverride(principal, EventLevel.Read), entry.ToOverride());
    }

    [Fact]
    public void Manage_cannot_be_stored()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EventOverrideEntry.For(_eventId, new EventOverride(Principal.Everyone, EventLevel.Manage)));
        Assert.Throws<ArgumentNullException>(() => EventOverrideEntry.For(_eventId, null!));
    }

    [Fact]
    public void Single_events_occur_until_their_end_series_until_their_last_occurrence()
    {
        var end = Instant.FromUtc(2026, 11, 2, 19, 0);
        var single = new Event { EndUtc = end };
        var infinite = new Event { EndUtc = end, Rrule = "FREQ=WEEKLY" };
        var bounded = new Event { EndUtc = end, Rrule = "FREQ=WEEKLY;COUNT=3", SeriesUntilUtc = end.Plus(Duration.FromDays(14)) };

        Assert.Equal(end, single.OccursUntil);
        Assert.Null(infinite.OccursUntil);
        Assert.Equal(end.Plus(Duration.FromDays(14)), bounded.OccursUntil);
    }
}
