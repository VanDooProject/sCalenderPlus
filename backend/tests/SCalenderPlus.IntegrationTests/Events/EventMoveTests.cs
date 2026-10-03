using System.Net;
using System.Text.Json.Nodes;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Calendars;
using SCalenderPlus.IntegrationTests.Groups;
using SCalenderPlus.IntegrationTests.Infrastructure;
using SCalenderPlus.IntegrationTests.Problems;

namespace SCalenderPlus.IntegrationTests.Events;

/// <summary>
/// Issue #48: <c>POST /events/{id}/move</c> (permissions.md §4.6). Group Lions (Olga owner, Adam admin, Mia member,
/// Vic viewer) owns "Club" and "Fixtures" (default role defaults: Mia contributes, creators may not share
/// externally) and "Curated" (creators do not manage their own events); Mia has a personal calendar; Eve, outside
/// the club, shares the group "Friends" with Adam.
/// </summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class EventMoveTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly List<HttpClient> _clients = [];
    private ApiTestHost _host = null!;
    private HttpClient _olga = null!;
    private HttpClient _adam = null!;
    private HttpClient _mia = null!;
    private HttpClient _vic = null!;
    private HttpClient _eve = null!;
    private Guid _olgaId;
    private Guid _miaId;
    private Guid _vicId;
    private Guid _eveId;
    private Guid _club;
    private Guid _fixtures;
    private Guid _curated;
    private Guid _personal;

    public async ValueTask InitializeAsync()
    {
        _host = await ApiTestHost.StartAsync(postgres);
        (_olgaId, _olga) = await PersonAsync("olga");
        var lions = await _olga.CreateGroupAsync();
        Guid adamId;
        (adamId, _adam) = await MemberAsync(lions, "adam", GroupRole.Admin);
        (_miaId, _mia) = await MemberAsync(lions, "mia", GroupRole.Member);
        (_vicId, _vic) = await MemberAsync(lions, "vic", GroupRole.Viewer);
        (_eveId, _eve) = await PersonAsync("eve");
        var friends = await _eve.CreateGroupAsync("Friends");
        await _host.AddMemberAsync(friends, adamId, GroupRole.Member);
        _club = await _olga.CreateCalendarAsync("Club", lions);
        _fixtures = await _olga.CreateCalendarAsync("Fixtures", lions);
        _curated = await _olga.CreateCalendarAsync("Curated", lions);
        using (var curated = await _olga.SendJsonAsync(HttpMethod.Patch, $"/api/v1/calendars/{_curated}", new { creatorsManageOwnEvents = false }, ifMatch: "*"))
        {
            Assert.Equal(HttpStatusCode.OK, curated.StatusCode);
        }

        _personal = await _mia.CreateCalendarAsync("Mia");
    }

    public async ValueTask DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task A_creator_moves_her_event_with_sync_log_acl_versions_and_audit()
    {
        var (created, etag) = await _mia.CreateEventAsync(EventApi.Timed(_club, title: "Dentist"));
        var id = (Guid)created["id"]!;
        var clubVersion = await _host.CalendarAclVersionAsync(_club);
        var personalVersion = await _host.CalendarAclVersionAsync(_personal);

        using var response = await MoveAsync(_mia, id, _personal, etag);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.JsonAsync();
        Assert.Equal(_personal, (Guid)body["calendarId"]!);
        Assert.Equal("manage", (string?)body["myLevel"]);
        Assert.Equal(response.Headers.ETag!.ToString(), (await _mia.GetEventAsync(id)).ETag);
        Assert.Equal(_personal, (await _host.StoredEventAsync(id))!.CalendarId);
        Assert.Equal(CalendarChangeKind.Delete, (await _host.ChangesAsync(_club)).Last().Change);
        Assert.Equal(CalendarChangeKind.Upsert, Assert.Single(await _host.ChangesAsync(_personal), c => c.EventId == id).Change);
        Assert.Equal(clubVersion + 1, await _host.CalendarAclVersionAsync(_club));
        Assert.Equal(personalVersion + 1, await _host.CalendarAclVersionAsync(_personal));
        // Recorded for both plan subjects: the club's (Olga) and Mia's.
        var audit = (await _host.AuditEventsAsync("event", id)).Where(a => a.Action == "event.moved").ToList();
        Assert.Equal([_olgaId, _miaId], audit.Select(a => a.SubjectId!.Value).Order());
        Assert.All(audit, a => Assert.Equal(_club, (Guid)JsonNode.Parse(a.Before!)!["calendarId"]!));
        Assert.All(audit, a => Assert.Equal(_personal, (Guid)JsonNode.Parse(a.After!)!["calendarId"]!));

        // The club no longer shows it.
        Assert.Equal("none", await _vic.LevelOnAsync(id));
    }

    [Fact]
    public async Task Moving_needs_manage_on_the_event_and_contribute_on_the_target()
    {
        var adams = await _adam.CreateEventIdAsync(EventApi.Timed(_club, title: "Board"));
        var mias = await _mia.CreateEventIdAsync(EventApi.Timed(_club, title: "Training"));
        var vics = await _vic.CreateCalendarAsync("Vic");

        // Mia only reads Adam's event; Vic reads everything.
        using (var notCreator = await MoveAsync(_mia, adams, _fixtures))
        {
            var problem = await ProblemResponse.AssertProblemAsync(notCreator, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);
            Assert.Equal("manage", (string?)problem["required"]);
        }

        // Adam cannot see Vic's personal calendar: 404; Mia only reads one where Vic grants her read: 403 with calendar levels.
        using (var invisible = await MoveAsync(_adam, adams, vics))
        {
            await ProblemResponse.AssertProblemAsync(invisible, HttpStatusCode.NotFound, ErrorCodes.NotFound);
        }

        await _host.GrantAsync(vics, _miaId, CalendarLevel.Read);
        using (var readOnly = await MoveAsync(_mia, mias, vics))
        {
            var problem = await ProblemResponse.AssertProblemAsync(readOnly, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);
            Assert.Equal("contribute", (string?)problem["required"]);
            Assert.Equal("read", (string?)problem["actual"]);
        }

        using (var same = await MoveAsync(_mia, mias, _club))
        {
            await ProblemResponse.AssertProblemAsync(same, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        }

        using (var missing = await MoveAsync(_mia, mias, _fixtures, ifMatch: null))
        {
            await ProblemResponse.AssertProblemAsync(missing, HttpStatusCode.PreconditionRequired, ErrorCodes.PreconditionRequired);
        }

        using (var stale = await MoveAsync(_mia, mias, _fixtures, ifMatch: "\"stale\""))
        {
            await ProblemResponse.AssertProblemAsync(stale, HttpStatusCode.PreconditionFailed, ErrorCodes.PreconditionFailed);
        }

        // Managers move any event of the calendar.
        using var moved = await MoveAsync(_adam, mias, _fixtures);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
    }

    [Fact]
    public async Task Overrides_the_mover_could_not_set_in_the_target_block_the_move()
    {
        // Adam (manager) shared Mia's event with Eve, who is outside the club and outside Fixtures.
        var training = await _mia.CreateEventIdAsync(EventApi.Timed(_club, title: "Training"));
        await _adam.SetOverridesAsync(training, EventApi.User(_eveId, "read"), EventApi.User(_vicId, "edit"));

        // Mia has the creator floor in Fixtures but no external rights: Eve's entry is external sharing there.
        using (var external = await MoveAsync(_mia, training, _fixtures))
        {
            var problem = await ProblemResponse.AssertProblemAsync(external, HttpStatusCode.Conflict, ErrorCodes.OverrideInvalidInTarget);
            var violation = Assert.Single(problem["violations"]!.AsArray())!;
            Assert.Equal(_eveId, (Guid)violation["principal"]!["id"]!);
            Assert.Equal("read", (string?)violation["level"]);
            Assert.Equal("external_sharing", (string?)violation["reason"]);
        }

        // In "Curated" Mia would hold no floor on the event: no override may travel.
        using (var curated = await MoveAsync(_mia, training, _curated))
        {
            var problem = await ProblemResponse.AssertProblemAsync(curated, HttpStatusCode.Conflict, ErrorCodes.OverrideInvalidInTarget);
            Assert.Equal(2, problem["violations"]!.AsArray().Count);
            Assert.All(problem["violations"]!.AsArray(), v => Assert.Equal("no_override_rights_in_target", (string?)v!["reason"]));
        }

        Assert.Equal(_club, (await _host.StoredEventAsync(training))!.CalendarId);

        // The manager may move it with its overrides: they travel, Eve keeps her share, Vic his edit.
        var eveVersion = await _host.AclVersionAsync(_eveId);
        using var moved = await MoveAsync(_adam, training, _fixtures);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.True((bool)(await moved.JsonAsync())["hasOverrides"]!);
        Assert.Equal("read", await _eve.LevelOnAsync(training));
        Assert.Equal("edit", await _vic.LevelOnAsync(training));
        Assert.Equal(eveVersion + 1, await _host.AclVersionAsync(_eveId));
    }

    [Fact]
    public async Task A_uid_taken_in_the_target_is_409()
    {
        var club = await _mia.CreateEventIdAsync(new { calendarId = _club, title = "Imported", uid = "match-42@fc.example", start = new { dateTime = "2026-11-02T18:00:00" }, end = new { dateTime = "2026-11-02T19:00:00" } });
        await _mia.CreateEventIdAsync(new { calendarId = _personal, title = "Copy", uid = "match-42@fc.example", start = new { dateTime = "2026-11-02T18:00:00" }, end = new { dateTime = "2026-11-02T19:00:00" } });

        using var response = await MoveAsync(_mia, club, _personal);

        await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.Conflict, ErrorCodes.UidConflict);
    }

    [Fact]
    public async Task Frozen_calendars_refuse_moves_in_and_out()
    {
        var training = await _mia.CreateEventIdAsync(EventApi.Timed(_club, title: "Training"));
        await _host.ExecuteSqlAsync($"UPDATE calendars SET frozen_at = now() WHERE id = {_fixtures}");

        using (var into = await MoveAsync(_mia, training, _fixtures))
        {
            await ProblemResponse.AssertProblemAsync(into, HttpStatusCode.Conflict, ErrorCodes.CalendarFrozen);
        }

        await _host.ExecuteSqlAsync($"UPDATE calendars SET frozen_at = now() WHERE id = {_club}");
        using var outOf = await MoveAsync(_mia, training, _personal);
        await ProblemResponse.AssertProblemAsync(outOf, HttpStatusCode.Conflict, ErrorCodes.CalendarFrozen);
    }

    private static Task<HttpResponseMessage> MoveAsync(HttpClient client, Guid eventId, Guid targetCalendarId, string? ifMatch = "*") =>
        client.SendJsonAsync(HttpMethod.Post, $"/api/v1/events/{eventId}/move", new { targetCalendarId }, ifMatch);

    private async Task<(Guid Id, HttpClient Client)> PersonAsync(string name)
    {
        var email = ApiTestHost.UniqueEmail(name);
        var id = await _host.CreateUserAsync(email, displayName: name);
        var client = await _host.SignedInClientAsync(email);
        _clients.Add(client);
        return (id, client);
    }

    private async Task<(Guid Id, HttpClient Client)> MemberAsync(Guid groupId, string name, GroupRole role)
    {
        var person = await PersonAsync(name);
        await _host.AddMemberAsync(groupId, person.Id, role);
        return person;
    }
}
