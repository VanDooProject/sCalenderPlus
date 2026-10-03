using SCalenderPlus.Core.Permissions;
using static SCalenderPlus.Core.Permissions.EventLevel;
using static SCalenderPlus.Core.Permissions.ResolutionStepKind;
using static SCalenderPlus.Core.Tests.Permissions.Lions;

namespace SCalenderPlus.Core.Tests.Permissions;

/// <summary>The worked examples A–G of docs/architecture/permissions.md §5 as table tests (level and the decisive trace step).</summary>
public sealed class WorkedExamplesTests
{
    internal static readonly Guid Lions = GroupId;

    /// <summary>The events of the examples, by letter.</summary>
    private static EventAcl ExampleEvent(string example) => example switch
    {
        "A" => Event("Mia"),
        "B" => Event("Adam", Override(Principal.Everyone, FreeBusy), Override(Principal.Group(Lions, Groups.GroupRole.Admin), Read)),
        "C" => Event("Mia", Override(Principal.Everyone, None), Override(Principal.User(Vic), Read)),
        "D" => Event("Mia", Override(Principal.User(Vic), Edit), Override(Principal.Group(Lions), Read)),
        "E" => Event("Mia", Override(Principal.User(Eve), Read)),
        "F" => Event("Mia", Override(Principal.Group(Lions), None)),
        "G" => Event("Mia", Override(Principal.Everyone, Read)),
        _ => throw new ArgumentOutOfRangeException(nameof(example)),
    };

    [Theory]
    [InlineData("Olga", CalendarLevel.Owner, CalendarOwner)]
    [InlineData("Adam", CalendarLevel.Manage, GroupRoleDefault)]
    [InlineData("Mia", CalendarLevel.Contribute, GroupRoleDefault)]
    [InlineData("Vic", CalendarLevel.Read, GroupRoleDefault)]
    [InlineData("Eve", CalendarLevel.None, ResolutionStepKind.CalendarResult)]
    [InlineData("Link", CalendarLevel.FreeBusy, ShareLink)]
    public void Calendar_levels_of_the_setup(string who, CalendarLevel expected, ResolutionStepKind reason)
    {
        var access = PermissionEngine.ResolveCalendar(Context(who), Calendar());

        Assert.Equal(expected, access.Level);
        Assert.Contains(access.Steps, s => s.Kind == reason);
        Assert.Equal(new ResolutionStep(ResolutionStepKind.CalendarResult) { CalendarLevel = expected }, access.Steps[^1]);
    }

    public static TheoryData<string, string, EventLevel, ResolutionStepKind, int?> Table { get; } = new()
    {
        // A – normal event created by Mia, no overrides.
        { "A", "Olga", Manage, ManagerFloor, null },
        { "A", "Adam", Manage, ManagerFloor, null },
        { "A", "Mia", Manage, CreatorFloor, null },
        { "A", "Vic", Read, NoMatchingOverride, null },
        { "A", "Eve", None, NoMatchingOverride, null },
        { "A", "Link", FreeBusy, NoMatchingOverride, null },

        // B – board meeting: everyone → free_busy, group:Lions[admin] → read.
        { "B", "Olga", Manage, ManagerFloor, null },
        { "B", "Adam", Manage, ManagerFloor, null },
        { "B", "Mia", FreeBusy, RestrictOnly, 1 },
        { "B", "Vic", FreeBusy, RestrictOnly, 1 },
        { "B", "Eve", None, NoMatchingOverride, null },
        { "B", "Link", FreeBusy, RestrictOnly, 1 },

        // C – coach's private note: everyone → none, user:Vic → read.
        { "C", "Olga", Manage, ManagerFloor, null },
        { "C", "Adam", Manage, ManagerFloor, null },
        { "C", "Mia", Manage, CreatorFloor, null },
        { "C", "Vic", Read, OverrideApplied, 4 },
        { "C", "Link", None, RestrictOnly, 1 },
        { "C", "Eve", None, NoMatchingOverride, null },

        // D – Mia's training: user:Vic → edit, group:Lions → read.
        { "D", "Vic", Edit, OverrideApplied, 4 },
        { "D", "Mia", Manage, CreatorFloor, null },
        { "D", "Max", Read, OverrideApplied, 3 },
        { "D", "Olga", Manage, ManagerFloor, null },

        // E – one event shared with Eve: user:Eve → read (she still has no calendar access).
        { "E", "Eve", Read, OverrideApplied, 4 },
        { "E", "Vic", Read, NoMatchingOverride, null },
        { "E", "Link", FreeBusy, NoMatchingOverride, null },

        // F – contributor hides her event from the group: group:Lions → none.
        { "F", "Olga", Manage, ManagerFloor, null },
        { "F", "Adam", Manage, ManagerFloor, null },
        { "F", "Mia", Manage, CreatorFloor, null },
        { "F", "Vic", None, OverrideApplied, 3 },
        { "F", "Link", FreeBusy, NoMatchingOverride, null },

        // G – contributor tries to widen access: everyone → read.
        { "G", "Vic", Read, RestrictOnly, 1 },
        { "G", "Link", FreeBusy, LinkCeiling, 1 },
        { "G", "Eve", None, NoMatchingOverride, null },
        { "G", "Mia", Manage, CreatorFloor, null },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Example(string example, string who, EventLevel expected, ResolutionStepKind decisive, int? tier)
    {
        var access = PermissionEngine.Resolve(Context(who), Calendar(), ExampleEvent(example));

        Assert.Equal(expected, access.Level);
        Assert.Equal(new ResolutionStep(Result) { EventLevel = expected }, access.Steps[^1]);
        Assert.Contains(access.Steps, s => s.Kind == decisive);
        var applied = access.Steps.SingleOrDefault(s => s.Kind == OverrideApplied);
        Assert.Equal(tier, applied?.Tier);
        if (decisive is ManagerFloor or CreatorFloor)
        {
            Assert.DoesNotContain(access.Steps, s => s.Kind is BaseLevel or OverrideMatched);
        }
    }

    [Fact]
    public void B_restricts_members_below_their_calendar_level_and_names_the_matching_override()
    {
        var access = PermissionEngine.Resolve(Context("Mia"), Calendar(), ExampleEvent("B"));

        Assert.Equal(CalendarLevel.Contribute, access.CalendarLevel);
        Assert.Contains(new ResolutionStep(BaseLevel) { CalendarLevel = CalendarLevel.Contribute, EventLevel = Read }, access.Steps);
        var matched = Assert.Single(access.Steps, s => s.Kind == OverrideMatched);
        Assert.Equal(Principal.Everyone, matched.Principal);
        Assert.Equal(FreeBusy, matched.EventLevel);
        Assert.True(matched.Decisive);
        Assert.Contains(new ResolutionStep(RestrictOnly) { EventLevel = FreeBusy, Applied = false }, access.Steps);
    }

    [Fact]
    public void C_user_tier_beats_everyone_tier()
    {
        var access = PermissionEngine.Resolve(Context("Vic"), Calendar(), ExampleEvent("C"));

        var matched = access.Steps.Where(s => s.Kind == OverrideMatched).ToList();
        Assert.Equal(2, matched.Count);
        Assert.Equal([(Principal.Everyone, false), (Principal.User(Vic), true)], matched.Select(s => (s.Principal!, s.Decisive!.Value)));
        Assert.DoesNotContain(access.Steps, s => s.Kind == RestrictOnly);
    }

    [Fact]
    public void G_everyone_read_is_restrict_only_and_the_link_stays_busy()
    {
        var access = PermissionEngine.Resolve(Context("Link"), Calendar(), ExampleEvent("G"));

        Assert.Contains(new ResolutionStep(OverrideApplied) { EventLevel = Read, Tier = 1 }, access.Steps);
        Assert.Contains(new ResolutionStep(RestrictOnly) { EventLevel = FreeBusy, Applied = true }, access.Steps);
        Assert.Contains(new ResolutionStep(LinkCeiling) { EventLevel = FreeBusy }, access.Steps);
    }

    [Fact]
    public void E_the_shared_event_does_not_open_the_calendar_for_Eve()
    {
        var calendar = Calendar();

        Assert.Equal(Read, PermissionEngine.Resolve(Context("Eve"), calendar, ExampleEvent("E")).Level);
        Assert.Equal(CalendarLevel.None, PermissionEngine.ResolveCalendar(Context("Eve"), calendar).Level);
        Assert.Equal(None, PermissionEngine.Resolve(Context("Eve"), calendar, ExampleEvent("A")).Level);
    }

    public static TheoryData<string, string, EventOverride[], OverrideChangeVerdict> Changes { get; } = new()
    {
        // F: a contributor may hide her event from the group; managers keep their floor.
        { "F", "Mia", [Override(Principal.Group(GroupId), None)], OverrideChangeVerdict.Allowed },

        // G: everyone → read is allowed (restrict-only, never external) …
        { "G", "Mia", [Override(Principal.Everyone, Read)], OverrideChangeVerdict.Allowed },

        // … user:Eve → read is external sharing: refused for the creator, allowed for a manager …
        { "G", "Mia", [Override(Principal.User(Eve), Read)], OverrideChangeVerdict.ExternalSharingNotAllowed },
        { "G", "Adam", [Override(Principal.User(Eve), Read)], OverrideChangeVerdict.Allowed },
        { "E", "Olga", [Override(Principal.User(Eve), Read)], OverrideChangeVerdict.Allowed },

        // … and user:Vic → manage exceeds the override cap (422).
        { "G", "Mia", [Override(Principal.User(Vic), Manage)], OverrideChangeVerdict.Invalid },
        { "G", "Adam", [Override(Principal.User(Vic), Manage)], OverrideChangeVerdict.Invalid },

        // D: elevating a calendar reader to edit is internal (Vic reads the calendar).
        { "D", "Mia", [Override(Principal.User(Vic), Edit), Override(Principal.Group(GroupId), Read)], OverrideChangeVerdict.Allowed },

        // Users without a floor cannot change overrides, even with an override-granted edit.
        { "D", "Vic", [], OverrideChangeVerdict.Forbidden },
        { "B", "Mia", [], OverrideChangeVerdict.Forbidden },

        // Eve cannot see Mia's plain event at all.
        { "A", "Eve", [], OverrideChangeVerdict.NotFound },
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public void Who_may_change_the_overrides(string example, string actor, EventOverride[] proposed, OverrideChangeVerdict expected)
    {
        var calendar = Calendar();
        var ev = ExampleEvent(example);
        if (example == "D" && actor == "Mia")
        {
            ev = Event("Mia"); // Mia sets the overrides of example D on her fresh event.
        }

        var decision = OverridePolicy.EvaluateChange(Context(actor), calendar, ev, proposed, UserLevels(calendar));

        Assert.Equal(expected, decision.Verdict);
    }

    [Fact]
    public void G_creators_may_share_externally_when_the_calendar_allows_it()
    {
        var calendar = Calendar(creatorsMayShareExternally: true);

        var decision = OverridePolicy.EvaluateChange(Context("Mia"), calendar, Event("Mia"), [Override(Principal.User(Eve), Read)], UserLevels(calendar));

        Assert.True(decision.IsAllowed);
    }
}
