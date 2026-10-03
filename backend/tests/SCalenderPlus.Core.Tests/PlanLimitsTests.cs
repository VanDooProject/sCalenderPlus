using NodaTime;
using SCalenderPlus.Core.Entitlements;

namespace SCalenderPlus.Core.Tests;

/// <summary>Issue #52: what counts against plan limits (docs/product/plans.md).</summary>
public sealed class PlanLimitsTests
{
    private static readonly PlanLimitValues _free = new(3, 1, 15, 10, 3);
    private static readonly Instant _now = Instant.FromUtc(2026, 10, 3, 12, 0);

    [Theory]
    [InlineData(PlanLimit.OwnedCalendars, "owned_calendars", 3)]
    [InlineData(PlanLimit.OwnedGroups, "owned_groups", 1)]
    [InlineData(PlanLimit.MembersPerGroup, "members_per_group", 15)]
    [InlineData(PlanLimit.EventsWithOverrides, "events_with_overrides", 10)]
    [InlineData(PlanLimit.OverridesPerEvent, "overrides_per_event", 3)]
    public void Limits_have_stable_keys(PlanLimit limit, string key, int free)
    {
        Assert.Equal(key, PlanLimits.Key(limit));
        Assert.Equal(free, _free.Get(limit));
        Assert.Null(PlanLimitValues.Unlimited.Get(limit));
        Assert.Contains(limit, PlanLimits.All);
    }

    [Fact]
    public void Unknown_limits_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanLimits.Key((PlanLimit)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => _free.Get((PlanLimit)99));
    }

    [Theory]
    [InlineData(1, 0, 1, false)]
    [InlineData(1, 1, 1, true)]
    [InlineData(15, 14, 1, false)]
    [InlineData(15, 15, 1, true)]
    [InlineData(15, 20, 0, false)] // over the limit after a downgrade: nothing new, but nothing refused either
    [InlineData(15, 20, -1, false)]
    public void Adding_beyond_the_maximum_is_refused(int max, int used, int adding, bool refused)
    {
        var result = PlanLimits.CheckAdd(PlanLimit.MembersPerGroup, max, used, adding);

        Assert.Equal(refused, result is not null);
        if (result is not null)
        {
            Assert.Equal(new PlanLimitExceeded(PlanLimit.MembersPerGroup, max, used), result);
            Assert.Equal("members_per_group", result.Key);
        }
    }

    [Fact]
    public void Unlimited_never_refuses() => Assert.Null(PlanLimits.CheckAdd(PlanLimit.OwnedGroups, null, int.MaxValue - 1));

    [Fact]
    public void Events_are_active_until_their_last_occurrence_ended_and_infinite_series_always()
    {
        Assert.True(PlanLimits.IsActive(null, _now));
        Assert.True(PlanLimits.IsActive(_now + Duration.FromMinutes(1), _now));
        Assert.False(PlanLimits.IsActive(_now, _now));
        Assert.False(PlanLimits.IsActive(_now - Duration.FromDays(1), _now));
    }

    [Fact]
    public void The_eleventh_active_event_with_overrides_is_refused_on_free()
    {
        var eleventh = new OverrideUsage(ActiveEventsWithOverrides: 10, EventIsActive: true, CurrentEntries: 0, ProposedEntries: 1, OnlyRemovals: false);

        Assert.Equal(new PlanLimitExceeded(PlanLimit.EventsWithOverrides, 10, 10), PlanLimits.CheckOverrideChange(_free, eleventh));
        Assert.Null(PlanLimits.CheckOverrideChange(_free, eleventh with { ActiveEventsWithOverrides = 9 }));
        Assert.Null(PlanLimits.CheckOverrideChange(PlanLimitValues.Unlimited, eleventh with { ActiveEventsWithOverrides = 1000 }));
    }

    [Fact]
    public void An_infinite_series_counts_as_active()
    {
        var infiniteSeries = new OverrideUsage(10, PlanLimits.IsActive(occursUntil: null, _now), 0, 1, false);

        Assert.NotNull(PlanLimits.CheckOverrideChange(_free, infiniteSeries));
    }

    [Fact]
    public void Past_events_and_events_that_already_count_need_no_room()
    {
        var past = new OverrideUsage(10, EventIsActive: false, CurrentEntries: 0, ProposedEntries: 2, OnlyRemovals: false);
        var counted = new OverrideUsage(10, EventIsActive: true, CurrentEntries: 1, ProposedEntries: 2, OnlyRemovals: false);

        Assert.Null(PlanLimits.CheckOverrideChange(_free, past));
        Assert.Null(PlanLimits.CheckOverrideChange(_free, counted));
    }

    [Fact]
    public void Over_the_limit_after_a_downgrade_only_removals_pass()
    {
        var overLimit = new OverrideUsage(12, EventIsActive: true, CurrentEntries: 5, ProposedEntries: 2, OnlyRemovals: false);

        Assert.Equal(new PlanLimitExceeded(PlanLimit.EventsWithOverrides, 10, 12), PlanLimits.CheckOverrideChange(_free, overLimit));
        Assert.Null(PlanLimits.CheckOverrideChange(_free, overLimit with { OnlyRemovals = true }));
    }

    [Fact]
    public void Entries_per_event_are_capped_but_existing_ones_may_be_removed()
    {
        var fourth = new OverrideUsage(1, true, CurrentEntries: 3, ProposedEntries: 4, OnlyRemovals: false);
        var downgraded = new OverrideUsage(1, true, CurrentEntries: 10, ProposedEntries: 9, OnlyRemovals: true);

        Assert.Equal(new PlanLimitExceeded(PlanLimit.OverridesPerEvent, 3, 3), PlanLimits.CheckOverrideChange(_free, fourth));
        Assert.Null(PlanLimits.CheckOverrideChange(_free, downgraded));
        Assert.NotNull(PlanLimits.CheckOverrideChange(_free, downgraded with { OnlyRemovals = false }));
        Assert.Null(PlanLimits.CheckOverrideChange(_free with { OverridesPerEvent = null }, fourth));
    }

    [Fact]
    public void Null_inputs_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => PlanLimits.CheckOverrideChange(null!, new(0, true, 0, 1, false)));
        Assert.Throws<ArgumentNullException>(() => PlanLimits.CheckOverrideChange(_free, null!));
    }
}
