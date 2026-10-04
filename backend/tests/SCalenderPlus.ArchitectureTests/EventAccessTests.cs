namespace SCalenderPlus.ArchitectureTests;

/// <summary>Issue #44: events are read only through the permission-aware query service (tenant isolation).</summary>
public sealed class EventAccessTests
{
    public static TheoryData<string> Assemblies => new()
    {
        Layers.Application.Location,
        Layers.Infrastructure.Location,
        Layers.Api.Location,
        Layers.Worker.Location,
    };

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void Only_the_event_query_service_and_writer_touch_the_event_set(string assembly)
    {
        var violations = EventAccessRules.Violations(assembly, "SCalenderPlus");

        Assert.True(violations.Count == 0, "Read events through EventQueryService (writes: EventWriter). Violations:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void The_rule_sees_the_allowed_services_use_the_set()
    {
        // Without the allow-list the services themselves are reported: the scan does find real uses.
        var violations = EventAccessRules.Violations(Layers.Application.Location, "SCalenderPlus.Application.Events", allowed: []);

        Assert.Contains(violations, v => v.StartsWith("SCalenderPlus.Application.Events.EventQueryService", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.StartsWith("SCalenderPlus.Application.Events.EventWriter", StringComparison.Ordinal));
    }

    [Fact]
    public void The_rule_fails_for_direct_use_of_the_event_set()
    {
        var violations = EventAccessRules.Violations(typeof(EventAccessTests).Assembly.Location, "SCalenderPlus.ArchitectureTests.Fixtures.EventViolation");

        Assert.Contains(violations, v => v.Contains("ReadsEventsDirectly.Count", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("ReadsEventsDirectly.InQuery", StringComparison.Ordinal)); // inside a LINQ expression tree
        Assert.Contains(violations, v => v.Contains("ReadsEventsDirectly.Generic", StringComparison.Ordinal)); // Set<Event>()
        Assert.Contains(violations, v => v.Contains("ReadsEventsDirectly.Exceptions", StringComparison.Ordinal)); // exceptions of series
        Assert.Contains(violations, v => v.Contains("HoldsTheSet.Events (field)", StringComparison.Ordinal) || v.Contains("HoldsTheSet.<Events>", StringComparison.Ordinal));
        Assert.DoesNotContain(violations, v => v.Contains("UsesCalendarsOnly", StringComparison.Ordinal));
    }
}
