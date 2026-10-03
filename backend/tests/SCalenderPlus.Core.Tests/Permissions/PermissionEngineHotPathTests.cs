using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;

namespace SCalenderPlus.Core.Tests.Permissions;

/// <summary>
/// Window queries and feeds resolve every event of a listing (§8 "Listing performance"): the untraced
/// resolution must not allocate, so thousands of events cost no garbage. The traced one is for the explainer.
/// </summary>
public sealed class PermissionEngineHotPathTests
{
    private const int Calls = 10_000;

    internal static readonly Guid Club = Guid.CreateVersion7();
    internal static readonly Guid Board = Guid.CreateVersion7();
    internal static readonly Guid Anna = Guid.CreateVersion7();

    internal static readonly CalendarAcl Calendar = new(
        Guid.CreateVersion7(),
        Principal.Group(Club),
        [new(Principal.Group(Board), CalendarLevel.Read), new(Principal.User(Guid.CreateVersion7()), CalendarLevel.Edit), new(Principal.Group(Board, GroupRole.Admin), CalendarLevel.Edit)]);

    internal static readonly EventAcl Event = new(
        Guid.CreateVersion7(),
        Calendar.CalendarId,
        Guid.CreateVersion7(),
        [
            new(Principal.Everyone, EventLevel.FreeBusy),
            new(Principal.Anonymous, EventLevel.None),
            new(Principal.Group(Board), EventLevel.Read),
            new(Principal.Group(Club, GroupRole.Admin), EventLevel.Edit),
            new(Principal.User(Guid.CreateVersion7()), EventLevel.Edit),
        ]);

    public static TheoryData<string> Viewers { get; } = ["member", "link", "stranger"];

    private static PrincipalContext Viewer(string name) => name switch
    {
        "member" => PrincipalContext.ForUser(Anna, new Dictionary<Guid, GroupRole> { [Club] = GroupRole.Member, [Board] = GroupRole.Viewer }),
        "link" => PrincipalContext.ForShareLink(Calendar.CalendarId, CalendarLevel.Read),
        _ => PrincipalContext.ForUser(Guid.CreateVersion7()),
    };

    [Theory]
    [MemberData(nameof(Viewers))]
    public void The_untraced_resolution_does_not_allocate(string viewer)
    {
        var principal = Viewer(viewer);
        var expected = PermissionEngine.Resolve(principal, Calendar, Event).Level;
        var sum = 0;
        for (var i = 0; i < 100; i++)
        {
            sum += (int)PermissionEngine.ResolveLevel(principal, Calendar, Event); // warm-up
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Calls; i++)
        {
            sum += (int)PermissionEngine.ResolveLevel(principal, Calendar, Event);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal((int)expected * (Calls + 100), sum);
        Assert.True(allocated < 1024, $"{allocated} bytes allocated by {Calls} untraced resolutions");
    }

    [Fact]
    public void The_traced_resolution_allocates_its_trace()
    {
        var principal = Viewer("member");
        var before = GC.GetAllocatedBytesForCurrentThread();
        var steps = PermissionEngine.Resolve(principal, Calendar, Event).Steps.Count;

        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before > 0);
        Assert.True(steps > 5);
    }
}
