using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Core.Tests.Permissions;

/// <summary>
/// The setup of the worked examples (docs/architecture/permissions.md §5): calendar "FC Lions – Club" owned by
/// group Lions (Olga owner, Adam admin, Mia member, Vic viewer; Max is a second member for "other members"),
/// external Eve, a <c>free_busy</c> share link, default role defaults, creator floor on.
/// </summary>
internal static class Lions
{
    public static readonly Guid GroupId = new("10000000-0000-0000-0000-000000000001");
    public static readonly Guid CalendarId = new("20000000-0000-0000-0000-000000000001");
    public static readonly Guid Olga = new("30000000-0000-0000-0000-000000000001");
    public static readonly Guid Adam = new("30000000-0000-0000-0000-000000000002");
    public static readonly Guid Mia = new("30000000-0000-0000-0000-000000000003");
    public static readonly Guid Vic = new("30000000-0000-0000-0000-000000000004");
    public static readonly Guid Eve = new("30000000-0000-0000-0000-000000000005");
    public static readonly Guid Max = new("30000000-0000-0000-0000-000000000006");

    internal static readonly string[] People = ["Olga", "Adam", "Mia", "Max", "Vic", "Eve"];

    public static CalendarAcl Calendar(bool creatorsManageOwnEvents = true, bool creatorsMayShareExternally = false) =>
        new(CalendarId, Principal.Group(GroupId), creatorsManageOwnEvents: creatorsManageOwnEvents, creatorsMayShareExternally: creatorsMayShareExternally);

    public static PrincipalContext Context(string name) => name switch
    {
        "Olga" => Member(Olga, GroupRole.Owner),
        "Adam" => Member(Adam, GroupRole.Admin),
        "Mia" => Member(Mia, GroupRole.Member),
        "Max" => Member(Max, GroupRole.Member),
        "Vic" => Member(Vic, GroupRole.Viewer),
        "Eve" => PrincipalContext.ForUser(Eve),
        "Link" => PrincipalContext.ForShareLink(CalendarId, CalendarLevel.FreeBusy),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    public static Guid UserId(string name) => Context(name).UserId!.Value;

    /// <summary>Calendar levels of the named users (for external-sharing checks).</summary>
    public static IReadOnlyDictionary<Guid, CalendarLevel> UserLevels(CalendarAcl calendar) =>
        People.ToDictionary(UserId, n => PermissionEngine.ResolveCalendar(Context(n), calendar).Level);

    public static EventAcl Event(string creator, params EventOverride[] overrides) =>
        new(Guid.CreateVersion7(), CalendarId, UserId(creator), overrides);

    public static EventOverride Override(Principal principal, EventLevel level) => new(principal, level);

    private static PrincipalContext Member(Guid user, GroupRole role) =>
        PrincipalContext.ForUser(user, new Dictionary<Guid, GroupRole> { [GroupId] = role });
}
