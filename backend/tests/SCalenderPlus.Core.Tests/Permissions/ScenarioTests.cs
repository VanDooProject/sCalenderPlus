using System.Security.Cryptography;
using System.Text;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;
using static SCalenderPlus.Core.Permissions.EventLevel;

namespace SCalenderPlus.Core.Tests.Permissions;

/// <summary>
/// Real-world scenarios of the permission engine review (docs/reviews/2026-10-permission-engine-review.md):
/// what a user expects in a family calendar, a sports club, a company team calendar, a public event calendar,
/// with people in two groups, an owner leaving, a demoted creator and recurring series. Every row checks the
/// traced and the untraced resolution.
/// </summary>
public sealed class ScenarioTests
{
    internal static readonly Guid OtherCalendar = Id("calendar:other");

    private sealed record World(CalendarAcl Calendar, IReadOnlyDictionary<string, PrincipalContext> People, IReadOnlyDictionary<string, EventAcl> Events);

    private static Guid Id(string name) => new(SHA256.HashData(Encoding.UTF8.GetBytes(name)).AsSpan(0, 16));

    private static PrincipalContext Person(string name, params (string Group, GroupRole Role)[] groups) =>
        PrincipalContext.ForUser(Id(name), groups.ToDictionary(g => Id(g.Group), g => g.Role));

    private static EventOverride O(Principal principal, EventLevel level) => new(principal, level);

    private static Principal U(string name) => Principal.User(Id(name));

    private static Principal G(string name, GroupRole minRole = GroupRole.Viewer) => Principal.Group(Id(name), minRole);

    private static EventAcl Ev(CalendarAcl calendar, string creator, params EventOverride[] overrides) =>
        new(Id($"event:{calendar.CalendarId}:{creator}:{overrides.Length}:{string.Join(',', overrides.Select(o => o.ToString()))}"), calendar.CalendarId, creator.Length == 0 ? null : Id(creator), overrides);

    /// <summary>Family calendar owned by Dad; Mom manages; the kids (group Family, members) contribute; Grandma sees busy blocks.</summary>
    private static World Family()
    {
        var calendar = new CalendarAcl(
            Id("calendar:family"),
            U("Dad"),
            [new(U("Mom"), CalendarLevel.Manage), new(G("Family", GroupRole.Member), CalendarLevel.Contribute), new(U("Grandma"), CalendarLevel.FreeBusy)]);
        return new(
            calendar,
            new Dictionary<string, PrincipalContext>
            {
                ["Dad"] = Person("Dad", ("Family", GroupRole.Owner)),
                ["Mom"] = Person("Mom", ("Family", GroupRole.Owner)),
                ["Tom"] = Person("Tom", ("Family", GroupRole.Member)),
                ["Lisa"] = Person("Lisa", ("Family", GroupRole.Member)),
                ["Grandma"] = Person("Grandma"),
            },
            new Dictionary<string, EventAcl>
            {
                ["tom-diary"] = Ev(calendar, "Tom", O(Principal.Everyone, None)),
                ["tom-dentist"] = Ev(calendar, "Tom"),
                ["mom-surprise-for-dad"] = Ev(calendar, "Mom", O(U("Dad"), FreeBusy)),
                ["lisa-party-for-tom"] = Ev(calendar, "Lisa", O(U("Tom"), None)),
            });
    }

    /// <summary>Sports club calendar owned by group Club (default role defaults), members add their own entries, a free_busy share link.</summary>
    private static World Club()
    {
        var calendar = new CalendarAcl(Id("calendar:club"), G("Club"));
        return new(
            calendar,
            new Dictionary<string, PrincipalContext>
            {
                ["Olga"] = Person("Olga", ("Club", GroupRole.Owner)),
                ["Adam"] = Person("Adam", ("Club", GroupRole.Admin)),
                ["Mia"] = Person("Mia", ("Club", GroupRole.Member)),
                ["Max"] = Person("Max", ("Club", GroupRole.Member)),
                ["Vic"] = Person("Vic", ("Club", GroupRole.Viewer)),
                ["Olga-left"] = Person("Olga"),
                ["Olga-admin"] = Person("Olga", ("Club", GroupRole.Admin)),
                ["Mia-viewer"] = Person("Mia", ("Club", GroupRole.Viewer)),
                ["Link"] = PrincipalContext.ForShareLink(calendar.CalendarId, CalendarLevel.FreeBusy),
            },
            new Dictionary<string, EventAcl>
            {
                ["max-physio"] = Ev(calendar, "Max", O(Principal.Everyone, None)),
                ["mia-training"] = Ev(calendar, "Mia"),
                ["mia-note"] = Ev(calendar, "Mia", O(Principal.Everyone, None)),
                ["olga-board"] = Ev(calendar, "Olga", O(Principal.Everyone, None)),
                ["mia-series"] = Ev(calendar, "Mia", O(U("Vic"), Edit)),
                ["imported"] = Ev(calendar, string.Empty, O(Principal.User(Id("Max")), Manage)), // legacy row with manage: capped
            });
    }

    /// <summary>
    /// Company team calendar owned by group Acme (admin manage, member read, viewer free_busy); group HR's
    /// members contribute. HR keeps confidential events visible as busy only.
    /// </summary>
    private static World Company()
    {
        var calendar = new CalendarAcl(
            Id("calendar:acme"),
            G("Acme"),
            [new(G("HR", GroupRole.Member), CalendarLevel.Contribute)],
            new(CalendarLevel.Manage, CalendarLevel.Read, CalendarLevel.FreeBusy));
        return new(
            calendar,
            new Dictionary<string, PrincipalContext>
            {
                ["Lead"] = Person("Lead", ("Acme", GroupRole.Admin)),
                ["Bob"] = Person("Bob", ("Acme", GroupRole.Member)),
                ["Carl"] = Person("Carl", ("Acme", GroupRole.Member)),
                ["Hanna"] = Person("Hanna", ("Acme", GroupRole.Member), ("HR", GroupRole.Member)),
                ["Hugo"] = Person("Hugo", ("Acme", GroupRole.Member), ("HR", GroupRole.Member)),
                ["Intern"] = Person("Intern", ("Acme", GroupRole.Viewer)),
            },
            new Dictionary<string, EventAcl>
            {
                ["review-bob"] = Ev(calendar, "Hanna", O(Principal.Everyone, FreeBusy), O(U("Bob"), Read), O(G("HR"), Read)),
                ["hr-only"] = Ev(calendar, "Hanna", O(Principal.Everyone, None), O(G("HR"), Read)),
                ["standup"] = Ev(calendar, "Lead"),
            });
    }

    /// <summary>Public event calendar owned by Org, Staff edits, a public <c>read</c> link and a <c>free_busy</c> link.</summary>
    private static World Public()
    {
        var calendar = new CalendarAcl(Id("calendar:public"), U("Org"), [new(U("Staff"), CalendarLevel.Edit)]);
        return new(
            calendar,
            new Dictionary<string, PrincipalContext>
            {
                ["Org"] = Person("Org"),
                ["Staff"] = Person("Staff"),
                ["Stranger"] = Person("Stranger"),
                ["PublicLink"] = PrincipalContext.ForShareLink(calendar.CalendarId, CalendarLevel.Read),
                ["BusyLink"] = PrincipalContext.ForShareLink(calendar.CalendarId, CalendarLevel.FreeBusy),
                ["OtherLink"] = PrincipalContext.ForShareLink(OtherCalendar, CalendarLevel.Read),
            },
            new Dictionary<string, EventAcl>
            {
                ["concert"] = Ev(calendar, "Staff"),
                ["planning"] = Ev(calendar, "Staff", O(Principal.Anonymous, None)),
                ["ticket-sale"] = Ev(calendar, "Org", O(Principal.Everyone, FreeBusy), O(Principal.Anonymous, Read)),
                ["teaser"] = Ev(calendar, "Org", O(Principal.Anonymous, FreeBusy)),
            });
    }

    /// <summary>A calendar granted to two groups: A → read, B (members and up) → edit. Pat: A member, B viewer; Quinn: A member, B member.</summary>
    private static World TwoGroups()
    {
        var calendar = new CalendarAcl(
            Id("calendar:two-groups"),
            U("Owner"),
            [new(G("A"), CalendarLevel.Read), new(G("B", GroupRole.Member), CalendarLevel.Edit)]);
        return new(
            calendar,
            new Dictionary<string, PrincipalContext>
            {
                ["Pat"] = Person("Pat", ("A", GroupRole.Member), ("B", GroupRole.Viewer)),
                ["Quinn"] = Person("Quinn", ("A", GroupRole.Member), ("B", GroupRole.Member)),
            },
            new Dictionary<string, EventAcl>
            {
                ["plain"] = Ev(calendar, "Owner"),
                ["hide-from-a"] = Ev(calendar, "Owner", O(G("A"), None)),
                ["a-none-b-read"] = Ev(calendar, "Owner", O(G("A"), None), O(G("B"), Read)),
            });
    }

    private static World WorldOf(string name) => name switch
    {
        "family" => Family(),
        "club" => Club(),
        "company" => Company(),
        "public" => Public(),
        "two-groups" => TwoGroups(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    public static TheoryData<string, string, string, EventLevel> Levels { get; } = new()
    {
        // Family: private entries stay private from siblings and grandparents, never from the parents (manager floor).
        { "family", "tom-diary", "Tom", Manage },
        { "family", "tom-diary", "Lisa", None },
        { "family", "tom-diary", "Grandma", None },
        { "family", "tom-diary", "Dad", Manage },
        { "family", "tom-diary", "Mom", Manage },
        { "family", "tom-dentist", "Lisa", Read },
        { "family", "tom-dentist", "Grandma", FreeBusy },
        { "family", "lisa-party-for-tom", "Tom", None },
        { "family", "lisa-party-for-tom", "Grandma", FreeBusy },
        { "family", "lisa-party-for-tom", "Lisa", Manage },
        { "family", "mom-surprise-for-dad", "Dad", Manage }, // surprising but by design: owners cannot be restricted (rule 5)
        { "family", "mom-surprise-for-dad", "Tom", Read },

        // Sports club: members add their own entries and may keep them private; managers keep control.
        { "club", "max-physio", "Max", Manage },
        { "club", "max-physio", "Mia", None },
        { "club", "max-physio", "Vic", None },
        { "club", "max-physio", "Adam", Manage },
        { "club", "max-physio", "Link", None },
        { "club", "mia-training", "Max", Read }, // contributors do not touch other people's entries
        { "club", "mia-training", "Vic", Read },
        { "club", "mia-training", "Link", FreeBusy },
        { "club", "imported", "Max", Edit }, // a stored manage override is capped at edit (rule 9)
        { "club", "imported", "Mia", Read }, // no creator, no floor

        // Owner leaving or demoted: the floor follows the current role.
        { "club", "olga-board", "Olga", Manage },
        { "club", "olga-board", "Olga-left", None },
        { "club", "olga-board", "Olga-admin", Manage },
        { "club", "mia-training", "Olga-left", None },

        // Creator demoted to viewer: the creator floor lapses and her own restriction now applies to her.
        { "club", "mia-note", "Mia", Manage },
        { "club", "mia-note", "Mia-viewer", None }, // surprising: open product question in the review
        { "club", "mia-training", "Mia-viewer", Read },

        // Company: HR-confidential events are busy blocks for the team, details for the people concerned.
        { "company", "review-bob", "Hanna", Manage },
        { "company", "review-bob", "Bob", Read },
        { "company", "review-bob", "Carl", FreeBusy },
        { "company", "review-bob", "Hugo", Read },
        { "company", "review-bob", "Intern", FreeBusy },
        { "company", "review-bob", "Lead", Manage }, // team admins manage the calendar and see everything
        { "company", "hr-only", "Carl", None },
        { "company", "hr-only", "Hugo", Read },
        { "company", "hr-only", "Intern", None },
        { "company", "standup", "Intern", FreeBusy },
        { "company", "standup", "Carl", Read },
        { "company", "standup", "Hanna", Read },

        // Public calendar: the link level is a ceiling; signed-in strangers do not see it as themselves.
        { "public", "concert", "PublicLink", Read },
        { "public", "concert", "BusyLink", FreeBusy },
        { "public", "concert", "OtherLink", None },
        { "public", "concert", "Stranger", None },
        { "public", "concert", "Staff", Manage }, // creator floor with calendar edit
        { "public", "planning", "PublicLink", None },
        { "public", "planning", "Staff", Manage },
        { "public", "planning", "Org", Manage },
        { "public", "ticket-sale", "PublicLink", Read }, // anonymous (tier 2) beats everyone (tier 1)
        { "public", "ticket-sale", "BusyLink", FreeBusy }, // … but never above the link
        { "public", "ticket-sale", "Staff", FreeBusy }, // surprising: everyone → free_busy restricts the editor, the public link reads
        { "public", "teaser", "PublicLink", FreeBusy },
        { "public", "teaser", "Staff", Edit }, // anonymous overrides do not match signed-in users
        { "public", "teaser", "Org", Manage },

        // A person in two groups: grants combine (max); a group override decides for every member of that group.
        { "two-groups", "plain", "Pat", Read },
        { "two-groups", "plain", "Quinn", Edit },
        { "two-groups", "hide-from-a", "Pat", None },
        { "two-groups", "hide-from-a", "Quinn", None }, // surprising: the A override hides it although B grants edit (rule 3)
        { "two-groups", "a-none-b-read", "Pat", Read }, // same tier: union
        { "two-groups", "a-none-b-read", "Quinn", Read },
    };

    [Theory]
    [MemberData(nameof(Levels))]
    public void Level(string world, string ev, string who, EventLevel expected)
    {
        var w = WorldOf(world);
        var principal = w.People[who];
        var acl = w.Events[ev];

        Assert.Equal(expected, PermissionEngine.Resolve(principal, w.Calendar, acl).Level);
        Assert.Equal(expected, PermissionEngine.ResolveLevel(principal, w.Calendar, acl));
        Assert.Equal(PermissionEngine.ResolveCalendar(principal, w.Calendar).Level, PermissionEngine.ResolveCalendarLevel(principal, w.Calendar));
    }

    [Theory]
    [InlineData("Vic", Edit)]
    [InlineData("Max", Read)]
    [InlineData("Mia", Manage)]
    [InlineData("Link", FreeBusy)]
    [InlineData("Mia-viewer", Read)]
    public void An_occurrence_moved_by_someone_else_and_a_split_keep_the_series_acl(string who, EventLevel expected)
    {
        var club = Club();
        var series = club.Events["mia-series"];
        var movedByVic = EventAcl.ExceptionOf(series, Id("exception:moved-by-vic"));
        var splitByVic = new EventAcl(Id("series:split-by-vic"), series.CalendarId, series.CreatorUserId, series.Overrides); // §4.6: keeps the creator

        Assert.Equal(expected, PermissionEngine.ResolveLevel(club.People[who], club.Calendar, movedByVic));
        Assert.Equal(expected, PermissionEngine.Resolve(club.People[who], club.Calendar, movedByVic).Level);
        Assert.Equal(expected, PermissionEngine.ResolveLevel(club.People[who], club.Calendar, splitByVic));
    }

    [Fact]
    public void Who_moved_an_occurrence_does_not_give_override_rights()
    {
        var club = Club();
        var series = club.Events["mia-series"];
        var exception = EventAcl.ExceptionOf(series, Id("exception:moved-by-vic"));
        var levels = UserLevels(club);

        Assert.Equal(OverrideChangeVerdict.Forbidden, OverridePolicy.EvaluateChange(club.People["Vic"], club.Calendar, exception, [], levels).Verdict);
        Assert.True(OverridePolicy.EvaluateChange(club.People["Mia"], club.Calendar, exception, [], levels).IsAllowed);
    }

    internal static readonly EventOverride ManagerRestriction = O(Principal.Everyone, FreeBusy);
    internal static readonly EventOverride PartnersShare = O(G("Partners"), Read); // external: Partners hold no grant on the club calendar
    internal static readonly EventOverride ExcludeEve = O(U("Eve"), None); // Eve is a partner but must not see it
    internal static readonly EventOverride ExcludeVic = O(U("Vic"), None);
    internal static readonly EventOverride EveEdits = O(U("Eve"), Edit);
    internal static readonly EventOverride ClubReads = O(G("Club"), Read); // internal: every club role reads the calendar

    public static TheoryData<string, string, EventOverride[], EventOverride[], OverrideChangeVerdict> Changes { get; } = new()
    {
        // A creator may lift a restriction a manager put on her own event: people get back what the calendar
        // gives them (the creator floor is manage on that event; curated calendars disable the floor).
        { "lift-manager-restriction", "Mia", [ManagerRestriction], [], OverrideChangeVerdict.Allowed },

        // … but not an exclusion from a manager's external group share: Eve would read through the Partners share.
        { "remove-exclusion-from-external-share", "Mia", [PartnersShare, ExcludeEve, ClubReads], [PartnersShare, ClubReads], OverrideChangeVerdict.ExternalSharingNotAllowed },
        { "remove-exclusion-and-share", "Mia", [PartnersShare, ExcludeEve], [], OverrideChangeVerdict.Allowed },
        { "remove-insider-exclusion", "Mia", [PartnersShare, ExcludeVic], [PartnersShare], OverrideChangeVerdict.Allowed },
        { "revoke-a-higher-external-share", "Mia", [PartnersShare, EveEdits], [PartnersShare], OverrideChangeVerdict.Allowed },
        { "keep-restriction-remove-exclusion", "Mia", [ManagerRestriction, ExcludeEve], [ManagerRestriction], OverrideChangeVerdict.Allowed },
        { "remove-a-group-entry", "Mia", [PartnersShare, ClubReads], [PartnersShare], OverrideChangeVerdict.Allowed },
        { "manager-removes-exclusion", "Adam", [PartnersShare, ExcludeEve], [PartnersShare], OverrideChangeVerdict.Allowed },
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public void Override_change(string scenario, string actor, EventOverride[] before, EventOverride[] after, OverrideChangeVerdict expected)
    {
        Assert.NotEmpty(scenario);
        var club = Club();
        var ev = new EventAcl(Id($"event:{scenario}"), club.Calendar.CalendarId, Id("Mia"), before);

        var decision = OverridePolicy.EvaluateChange(club.People[actor], club.Calendar, ev, after, UserLevels(club));

        Assert.Equal(expected, decision.Verdict);
        if (expected == OverrideChangeVerdict.ExternalSharingNotAllowed)
        {
            Assert.Equal([new(ExcludeEve, OverrideViolationReason.RemovalExposesExternalShare)], decision.Violations);
        }
    }

    [Fact]
    public void Lifting_a_manager_restriction_gives_back_the_calendar_level()
    {
        var club = Club();
        var restricted = new EventAcl(Id("event:restricted"), club.Calendar.CalendarId, Id("Mia"), [ManagerRestriction]);
        var lifted = new EventAcl(restricted.EventId, club.Calendar.CalendarId, Id("Mia"), []);

        Assert.Equal(FreeBusy, PermissionEngine.ResolveLevel(club.People["Vic"], club.Calendar, restricted));
        Assert.Equal(Read, PermissionEngine.ResolveLevel(club.People["Vic"], club.Calendar, lifted));
    }

    [Fact]
    public void Creators_with_external_rights_may_remove_exclusions()
    {
        var club = Club();
        var calendar = new CalendarAcl(club.Calendar.CalendarId, club.Calendar.Owner, creatorsMayShareExternally: true);
        var ev = new EventAcl(Id("event:external-rights"), calendar.CalendarId, Id("Mia"), [PartnersShare, ExcludeEve]);

        Assert.True(OverridePolicy.EvaluateChange(club.People["Mia"], calendar, ev, [PartnersShare], UserLevels(club)).IsAllowed);
    }

    [Fact]
    public void Moving_into_a_calendar_without_override_rights_there_carries_no_overrides()
    {
        var club = Club();
        var curated = new CalendarAcl(Id("calendar:curated"), U("Org"), [new(U("Mia"), CalendarLevel.Contribute)], creatorsManageOwnEvents: false);
        var open = new CalendarAcl(Id("calendar:open"), U("Org"), [new(U("Mia"), CalendarLevel.Contribute), new(U("Vic"), CalendarLevel.Read)]);
        var elevate = O(U("Vic"), Edit);
        var hide = O(Principal.Everyone, None);
        var ev = new EventAcl(Id("event:to-move"), club.Calendar.CalendarId, Id("Mia"), [elevate, hide]);
        var levels = new Dictionary<Guid, CalendarLevel> { [Id("Mia")] = CalendarLevel.Contribute, [Id("Vic")] = CalendarLevel.Read };

        // In the curated calendar Mia could not set any override herself, so none may travel with her move.
        Assert.Equal([elevate, hide], OverridePolicy.InvalidInTarget(club.People["Mia"], curated, ev, levels));
        Assert.Empty(OverridePolicy.InvalidInTarget(club.People["Mia"], open, ev, levels));
        Assert.Empty(OverridePolicy.InvalidInTarget(Person("Org"), curated, ev, levels));
    }

    private static Dictionary<Guid, CalendarLevel> UserLevels(World world)
    {
        var levels = new Dictionary<Guid, CalendarLevel>();
        foreach (var (name, person) in world.People)
        {
            if (person.UserId is { } user && !name.Contains('-', StringComparison.Ordinal)) // "Mia-viewer" etc. are other states of the same user
            {
                levels[user] = PermissionEngine.ResolveCalendarLevel(person, world.Calendar);
            }
        }

        levels[Id("Eve")] = CalendarLevel.None;
        return levels;
    }
}
