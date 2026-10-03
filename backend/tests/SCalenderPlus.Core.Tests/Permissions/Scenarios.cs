using CsCheck;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Core.Tests.Permissions;

/// <summary>A random world for property tests: 4 users, 3 groups, one calendar with one event, and a viewer.</summary>
internal sealed record Scenario(
    IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, GroupRole>> Memberships,
    CalendarAcl Calendar,
    EventAcl Event,
    PrincipalContext Viewer)
{
    public static readonly Guid CalendarId = new("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid OtherCalendarId = new("aaaaaaaa-0000-0000-0000-000000000002");
    public static readonly Guid[] Users = [.. Enumerable.Range(1, 4).Select(i => new Guid($"bbbbbbbb-0000-0000-0000-00000000000{i}"))];
    public static readonly Guid[] Groups = [.. Enumerable.Range(1, 3).Select(i => new Guid($"cccccccc-0000-0000-0000-00000000000{i}"))];

    public static readonly Gen<GroupRole> Role = Gen.Enum<GroupRole>();
    public static readonly Gen<CalendarLevel> GrantLevel = Gen.OneOfConst(CalendarLevel.FreeBusy, CalendarLevel.Read, CalendarLevel.Contribute, CalendarLevel.Edit, CalendarLevel.Manage);
    public static readonly Gen<CalendarLevel> DefaultLevel = Gen.OneOfConst(CalendarLevel.None, CalendarLevel.FreeBusy, CalendarLevel.Read, CalendarLevel.Contribute, CalendarLevel.Edit, CalendarLevel.Manage);
    public static readonly Gen<EventLevel> OverrideLevel = Gen.OneOfConst(EventLevel.None, EventLevel.FreeBusy, EventLevel.Read, EventLevel.Edit);

    /// <summary>Includes <c>manage</c>, which stored overrides never carry: the engine must cap it anyway.</summary>
    public static readonly Gen<EventLevel> AnyOverrideLevel = Gen.Enum<EventLevel>();

    public static readonly Gen<Principal> UserPrincipal = Gen.OneOfConst(Users).Select(Principal.User);
    public static readonly Gen<Principal> GroupPrincipal = Gen.Select(Gen.OneOfConst(Groups), Role, Principal.Group);
    public static readonly Gen<Principal> GrantPrincipal = Gen.OneOf(UserPrincipal, GroupPrincipal);
    public static readonly Gen<Principal> RestrictOnlyPrincipal = Gen.OneOfConst(Principal.Anonymous, Principal.Everyone);
    public static readonly Gen<Principal> AnyPrincipal = Gen.OneOf(UserPrincipal, GroupPrincipal, RestrictOnlyPrincipal);

    public static readonly Gen<CalendarGrant> Grant = Gen.Select(GrantPrincipal, GrantLevel, (p, l) => new CalendarGrant(p, l));
    public static readonly Gen<EventOverride> Override = Gen.Select(AnyPrincipal, AnyOverrideLevel, (p, l) => new EventOverride(p, l));
    public static readonly Gen<EventOverride> ValidOverride = Gen.Select(AnyPrincipal, OverrideLevel, (p, l) => new EventOverride(p, l));
    public static readonly Gen<EventOverride> RestrictOnlyOverride = Gen.Select(RestrictOnlyPrincipal, AnyOverrideLevel, (p, l) => new EventOverride(p, l));

    public static readonly Gen<IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, GroupRole>>> MembershipGen =
        Gen.Select(Gen.Bool, Role).Array[Users.Length * Groups.Length].Select(cells =>
        {
            var result = new Dictionary<Guid, IReadOnlyDictionary<Guid, GroupRole>>();
            for (var u = 0; u < Users.Length; u++)
            {
                var groups = new Dictionary<Guid, GroupRole>();
                for (var g = 0; g < Groups.Length; g++)
                {
                    var (member, role) = cells[(u * Groups.Length) + g];
                    if (member)
                    {
                        groups[Groups[g]] = role;
                    }
                }

                result[Users[u]] = groups;
            }

            return (IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, GroupRole>>)result;
        });

    public static readonly Gen<CalendarAcl> CalendarGen = Gen.Select(
        Gen.OneOf(UserPrincipal, Gen.OneOfConst(Groups).Select(g => Principal.Group(g))),
        Grant.Array[0, 4],
        Gen.Select(DefaultLevel, DefaultLevel, DefaultLevel, (a, m, v) => new GroupRoleDefaults(a, m, v)),
        Gen.Bool,
        Gen.Bool,
        (owner, grants, defaults, manageOwn, shareExternally) => new CalendarAcl(CalendarId, owner, grants, defaults, manageOwn, shareExternally));

    public static readonly Gen<Guid?> Creator = Gen.OneOf(Gen.OneOfConst(Users).Select(u => (Guid?)u), Gen.Const((Guid?)null));

    public static Gen<EventAcl> EventGen(Gen<EventOverride> overrides) => Gen.Select(
        Creator,
        overrides.Array[0, 5],
        Gen.Bool,
        (creator, list, exception) =>
        {
            var series = new EventAcl(Guid.CreateVersion7(), CalendarId, creator, list);
            return exception ? EventAcl.ExceptionOf(series, Guid.CreateVersion7()) : series;
        });

    public static readonly Gen<CalendarLevel> LinkLevel = Gen.OneOfConst(CalendarLevel.FreeBusy, CalendarLevel.Read);

    public static Gen<Scenario> With(Gen<EventOverride> overrides) => Gen.Select(
        MembershipGen,
        CalendarGen,
        EventGen(overrides),
        Gen.Int[0, Users.Length + 1],
        LinkLevel,
        Gen.Bool,
        (memberships, calendar, ev, viewer, linkLevel, linkOfThisCalendar) => new Scenario(
            memberships,
            calendar,
            ev,
            viewer < Users.Length
                ? PrincipalContext.ForUser(Users[viewer], memberships[Users[viewer]])
                : PrincipalContext.ForShareLink(linkOfThisCalendar ? CalendarId : OtherCalendarId, linkLevel)));

    public static readonly Gen<Scenario> Any = With(Override);

    public static readonly Gen<Scenario> Valid = With(ValidOverride);

    /// <summary>The viewer is the event's creator with the creator floor but no external sharing (rights <see cref="OverrideRights.InternalOnly"/>).</summary>
    public static readonly Gen<Scenario> CreatorWithoutExternalRights = Valid.Select(s =>
    {
        var creator = Users[0];
        var calendar = new CalendarAcl(
            CalendarId,
            s.Calendar.Owner,
            [.. s.Calendar.Grants, new CalendarGrant(Principal.User(creator), CalendarLevel.Contribute)],
            s.Calendar.RoleDefaults,
            creatorsManageOwnEvents: true,
            creatorsMayShareExternally: false);
        var series = s.Event.Series ?? s.Event;
        return s with
        {
            Calendar = calendar,
            Event = new EventAcl(series.EventId, CalendarId, creator, series.Overrides),
            Viewer = s.ContextOf(creator),
        };
    }).Where(s => OverridePolicy.RightsOf(s.Viewer, s.Calendar, s.Event) == OverrideRights.InternalOnly);

    public PrincipalContext ContextOf(Guid user) => PrincipalContext.ForUser(user, Memberships[user]);

    public EventAccess Resolve() => PermissionEngine.Resolve(Viewer, Calendar, Event);

    public CalendarLevel CalendarLevel => PermissionEngine.ResolveCalendar(Viewer, Calendar).Level;

    public IReadOnlyDictionary<Guid, CalendarLevel> UserLevels() =>
        Users.ToDictionary(u => u, u => PermissionEngine.ResolveCalendar(ContextOf(u), Calendar).Level);

    public Scenario WithEvent(IReadOnlyList<EventOverride> overrides)
    {
        var series = Event.Series ?? Event;
        return this with { Event = new EventAcl(series.EventId, series.CalendarId, series.CreatorUserId, overrides) };
    }

    public Scenario WithGrants(IReadOnlyList<CalendarGrant> grants) => this with
    {
        Calendar = new CalendarAcl(Calendar.CalendarId, Calendar.Owner, grants, Calendar.RoleDefaults, Calendar.CreatorsManageOwnEvents, Calendar.CreatorsMayShareExternally),
    };

    public bool HasFloor
    {
        get
        {
            var lc = CalendarLevel;
            var creator = (Event.Series ?? Event).CreatorUserId;
            return lc >= CalendarLevel.Manage
                || (creator is not null && creator == Viewer.UserId && lc >= CalendarLevel.Contribute && Calendar.CreatorsManageOwnEvents);
        }
    }
}
