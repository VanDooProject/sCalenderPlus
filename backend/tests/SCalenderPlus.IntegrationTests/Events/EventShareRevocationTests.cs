using System.Net;
using System.Text.Json.Nodes;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Calendars;
using SCalenderPlus.IntegrationTests.Groups;
using SCalenderPlus.IntegrationTests.Infrastructure;

namespace SCalenderPlus.IntegrationTests.Events;

/// <summary>
/// Issue #49: losing calendar level revokes individual event shares (permissions.md §4.6) — member removal,
/// leaving, demotion, grant removal and lowering (each with the opt-out <c>?revokeEventShares=false</c>) and group
/// deletion. Group Lions (Olga owner, Adam admin, Mia member, Vic viewer) owns "Club" (default role defaults); Eve
/// and Pat are outside; the group "Friends" (Eve owner, Adam, Mia) has no access to the club.
/// TODO(M4): also assert that the revoked event leaves the removed member's iCal feed (feeds come with M4).
/// </summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class EventShareRevocationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly List<HttpClient> _clients = [];
    private readonly Dictionary<string, (Guid Id, HttpClient Client)> _people = new(StringComparer.Ordinal);
    private ApiTestHost _host = null!;
    private Guid _lions;
    private Guid _friends;
    private Guid _club;

    public async ValueTask InitializeAsync()
    {
        _host = await ApiTestHost.StartAsync(postgres);
        _lions = await (await PersonAsync("olga")).CreateGroupAsync("Lions");
        await MembersAsync(_lions, ("adam", GroupRole.Admin), ("mia", GroupRole.Member), ("vic", GroupRole.Viewer));
        _friends = await (await PersonAsync("eve")).CreateGroupAsync("Friends");
        await MembersAsync(_friends, ("adam", GroupRole.Member), ("mia", GroupRole.Member));
        await PersonAsync("pat");
        _club = await Person("olga").CreateCalendarAsync("Club", _lions);
    }

    public async ValueTask DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task A_removed_member_no_longer_sees_an_event_shared_with_them_individually()
    {
        // Example C: Mia's private note, shown to Vic individually.
        var note = await Person("mia").CreateEventIdAsync(EventApi.Timed(_club, title: "Coach's note"));
        await Person("mia").SetOverridesAsync(note, EventApi.Everyone("none"), EventApi.User(Id("vic"), "read"));
        Assert.Equal("read", await Person("vic").LevelOnAsync(note));
        var clubVersion = await _host.CalendarAclVersionAsync(_club);

        await RemoveMemberAsync("vic");

        Assert.Equal("none", await Person("vic").LevelOnAsync(note));
        Assert.Equal(["everyone"], (await _host.StoredOverridesAsync(note)).Select(o => o.Principal.ToString()));
        Assert.Equal(clubVersion + 1, await _host.CalendarAclVersionAsync(_club));
        Assert.Equal(CalendarChangeKind.Acl, (await _host.ChangesAsync(_club)).Last().Change);
        var audit = Assert.Single(await _host.AuditEventsAsync("event", note), a => a.Action == "event.overrides.revoked");
        Assert.Contains($"user:{Id("vic")} → read", Overrides(audit.Before));
        Assert.Equal(["everyone → none"], Overrides(audit.After));
    }

    [Fact]
    public async Task The_remover_can_keep_the_shares()
    {
        var note = await Person("mia").CreateEventIdAsync(EventApi.Timed(_club, title: "Coach's note"));
        await Person("mia").SetOverridesAsync(note, EventApi.Everyone("none"), EventApi.User(Id("vic"), "read"));

        await RemoveMemberAsync("vic", revoke: false);

        // Vic is outside the club now, but the individual share stays: the event is "shared with me".
        Assert.Equal("read", await Person("vic").LevelOnAsync(note));
        Assert.True((bool)(await Person("vic").GetEventAsync(note)).Body["sharedWithMe"]!);
        Assert.Equal(2, (await _host.StoredOverridesAsync(note)).Count);
        Assert.DoesNotContain(await _host.AuditEventsAsync("event", note), a => a.Action == "event.overrides.revoked");
    }

    [Fact]
    public async Task Leaving_and_demotion_revoke_shares_above_the_new_level_and_keep_restrictions()
    {
        // Adam's event: Mia may edit it (above her contribute → read), Vic may not see it; Mia's event: Vic edits it.
        var board = await Person("adam").CreateEventIdAsync(EventApi.Timed(_club, title: "Board"));
        await Person("adam").SetOverridesAsync(board, EventApi.User(Id("mia"), "edit"), EventApi.User(Id("vic"), "none"));
        var training = await Person("mia").CreateEventIdAsync(EventApi.Timed(_club, title: "Training"));
        await Person("mia").SetOverridesAsync(training, EventApi.User(Id("vic"), "edit"));

        // Mia demoted to viewer: her edit share goes (read stays from the calendar).
        using (var demote = await Person("olga").SendJsonAsync(HttpMethod.Patch, $"/api/v1/groups/{_lions}/members/{Id("mia")}", new { role = "viewer" }, ifMatch: "*"))
        {
            Assert.Equal(HttpStatusCode.OK, demote.StatusCode);
        }

        Assert.Equal("read", await Person("mia").LevelOnAsync(board));

        // Vic leaves: his edit share on Mia's event goes, his restriction on Adam's stays (it never gave him anything).
        using (var leave = await Person("vic").SendJsonAsync(HttpMethod.Delete, $"/api/v1/groups/{_lions}/members/{Id("vic")}", ifMatch: "*"))
        {
            Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
        }

        Assert.Equal("none", await Person("vic").LevelOnAsync(training));
        Assert.Equal([$"user:{Id("vic")}"], (await _host.StoredOverridesAsync(board)).Select(o => o.Principal.ToString()));
        Assert.False((await _host.StoredEventAsync(training))!.HasOverrides);
    }

    [Fact]
    public async Task An_entry_whose_removal_would_raise_the_user_stays()
    {
        // Adam shares the event with the Friends group (edit) but gives Mia only read; Mia then leaves Lions.
        var party = await Person("adam").CreateEventIdAsync(EventApi.Timed(_club, title: "Party"));
        await Person("adam").SetOverridesAsync(party, EventApi.Group(_friends, "edit"), EventApi.User(Id("mia"), "read"));

        await RemoveMemberAsync("mia");

        // Removing user:Mia → read would hand the decision to the Friends entry (edit): it stays.
        Assert.Equal("read", await Person("mia").LevelOnAsync(party));
        Assert.Equal(2, (await _host.StoredOverridesAsync(party)).Count);
    }

    [Fact]
    public async Task Removing_or_lowering_a_grant_revokes_the_shares_of_those_who_lose_level()
    {
        // Eve shares a group with Olga (so Olga may grant her) that has no access to the club.
        await MembersAsync(await Person("olga").CreateGroupAsync("Contacts"), ("eve", GroupRole.Member));
        var eveGrant = await GrantAsync(new { type = "user", id = Id("eve") }, "read");
        var shared = await Person("adam").CreateEventIdAsync(EventApi.Timed(_club, title: "Shared"));
        await Person("adam").SetOverridesAsync(shared, EventApi.Everyone("none"), EventApi.User(Id("eve"), "read"));
        Assert.Equal("read", await Person("eve").LevelOnAsync(shared));

        // Lowering to free_busy: read is above what the calendar now gives Eve.
        using (var lower = await Person("olga").SendJsonAsync(HttpMethod.Patch, $"/api/v1/calendars/{_club}/grants/{eveGrant}", new { level = "free_busy" }, ifMatch: "*"))
        {
            Assert.Equal(HttpStatusCode.OK, lower.StatusCode);
        }

        Assert.Equal("none", await Person("eve").LevelOnAsync(shared));

        // Removing with the opt-out keeps a new share.
        await Person("adam").SetOverridesAsync(shared, EventApi.Everyone("none"), EventApi.User(Id("eve"), "read"));
        using (var keep = await Person("olga").SendJsonAsync(HttpMethod.Delete, $"/api/v1/calendars/{_club}/grants/{eveGrant}?revokeEventShares=false", ifMatch: "*"))
        {
            Assert.Equal(HttpStatusCode.NoContent, keep.StatusCode);
        }

        Assert.Equal("read", await Person("eve").LevelOnAsync(shared));
    }

    [Fact]
    public async Task Removing_a_group_grant_revokes_its_members_shares()
    {
        var partners = await Person("olga").CreateGroupAsync("Partners");
        await MembersAsync(partners, ("pat", GroupRole.Member));
        var grant = await GrantAsync(new { type = "group", id = partners }, "read");
        var shared = await Person("adam").CreateEventIdAsync(EventApi.Timed(_club, title: "Shared"));
        await Person("adam").SetOverridesAsync(shared, EventApi.Everyone("none"), EventApi.User(Id("pat"), "read"));
        Assert.Equal("read", await Person("pat").LevelOnAsync(shared));

        using (var removed = await Person("olga").SendJsonAsync(HttpMethod.Delete, $"/api/v1/calendars/{_club}/grants/{grant}", ifMatch: "*"))
        {
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }

        Assert.Equal("none", await Person("pat").LevelOnAsync(shared));
    }

    [Fact]
    public async Task Deleting_a_group_removes_its_overrides_and_its_members_shares()
    {
        // Partners (Olga, Pat) have read on the club; one event is shared with the group, one with Pat in person.
        var partners = await Person("olga").CreateGroupAsync("Partners");
        await MembersAsync(partners, ("pat", GroupRole.Member));
        await GrantAsync(new { type = "group", id = partners }, "read");
        var groupOnly = await Person("adam").CreateEventIdAsync(EventApi.Timed(_club, title: "For partners"));
        await Person("adam").SetOverridesAsync(groupOnly, EventApi.Group(partners, "edit"));
        var personal = await Person("adam").CreateEventIdAsync(EventApi.Timed(_club, title: "For Pat"));
        await Person("adam").SetOverridesAsync(personal, EventApi.Everyone("none"), EventApi.User(Id("pat"), "read"));
        Assert.Equal("read", await Person("pat").LevelOnAsync(personal));

        using (var deleted = await Person("olga").SendJsonAsync(HttpMethod.Delete, $"/api/v1/groups/{partners}", ifMatch: "*"))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        Assert.Empty(await _host.StoredOverridesAsync(groupOnly));
        Assert.False((await _host.StoredEventAsync(groupOnly))!.HasOverrides);
        Assert.Single(await _host.AuditEventsAsync("event", groupOnly), a => a.Action == "event.overrides.removed_with_group");
        Assert.Equal(["everyone"], (await _host.StoredOverridesAsync(personal)).Select(o => o.Principal.ToString()));
        Assert.Equal("none", await Person("pat").LevelOnAsync(personal));
    }

    private static List<string> Overrides(string? state) =>
        [.. JsonNode.Parse(state!)!["overrides"]!.AsArray().Select(o => (string)o!)];

    private async Task RemoveMemberAsync(string name, bool revoke = true)
    {
        var query = revoke ? string.Empty : "?revokeEventShares=false";
        using var response = await Person("olga").SendJsonAsync(HttpMethod.Delete, $"/api/v1/groups/{_lions}/members/{Id(name)}{query}", ifMatch: "*");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<Guid> GrantAsync(object principal, string level)
    {
        using var response = await Person("olga").SendJsonAsync(HttpMethod.Post, $"/api/v1/calendars/{_club}/grants", new { principal, level });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (Guid)(await response.JsonAsync())["id"]!;
    }

    private async Task MembersAsync(Guid groupId, params (string Name, GroupRole Role)[] members)
    {
        foreach (var (name, role) in members)
        {
            if (!_people.ContainsKey(name))
            {
                await PersonAsync(name);
            }

            await _host.AddMemberAsync(groupId, Id(name), role);
        }
    }

    private HttpClient Person(string name) => _people[name].Client;

    private Guid Id(string name) => _people[name].Id;

    private async Task<HttpClient> PersonAsync(string name)
    {
        var email = ApiTestHost.UniqueEmail(name);
        var id = await _host.CreateUserAsync(email, displayName: name);
        var client = await _host.SignedInClientAsync(email);
        _clients.Add(client);
        _people[name] = (id, client);
        return client;
    }
}
