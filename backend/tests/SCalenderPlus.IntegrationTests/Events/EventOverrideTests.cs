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
/// Issue #46: <c>GET/PUT /events/{id}/overrides</c> — who may read and change them (§4.4), the external-sharing
/// rule, validation, If-Match, <c>has_overrides</c>, <c>acl_version</c>, the sync log and the audit. Setup follows
/// permissions.md §5: "FC Lions – Club" of group Lions (Olga owner, Adam admin, Mia member, Vic viewer); Eve has no
/// calendar access but shares the group "Friends" with Adam and Mia (so they may name her); Sam is a stranger.
/// </summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class EventOverrideTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly List<HttpClient> _clients = [];
    private ApiTestHost _host = null!;
    private HttpClient _olga = null!;
    private HttpClient _adam = null!;
    private HttpClient _mia = null!;
    private HttpClient _vic = null!;
    private HttpClient _eve = null!;
    private Guid _lions;
    private Guid _friends;
    private Guid _adamId;
    private Guid _miaId;
    private Guid _vicId;
    private Guid _eveId;
    private Guid _samId;
    private Guid _club;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await ApiTestHost.StartAsync(postgres);
        (_, _olga) = await PersonAsync("olga");
        _lions = await _olga.CreateGroupAsync();
        (_adamId, _adam) = await MemberAsync(_lions, "adam", GroupRole.Admin);
        (_miaId, _mia) = await MemberAsync(_lions, "mia", GroupRole.Member);
        (_vicId, _vic) = await MemberAsync(_lions, "vic", GroupRole.Viewer);
        (_eveId, _eve) = await PersonAsync("eve");
        (_samId, _) = await PersonAsync("sam");
        _friends = await _eve.CreateGroupAsync("Friends");
        await _host.AddMemberAsync(_friends, _adamId, GroupRole.Member);
        await _host.AddMemberAsync(_friends, _miaId, GroupRole.Member);
        _club = await _olga.CreateCalendarAsync("FC Lions – Club", _lions);
    }

    public async ValueTask DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task A_contributor_cannot_hide_her_event_from_managers()
    {
        // Example F: Mia hides her event from the whole group and from Adam by name — permitted, managers keep manage.
        var note = await _mia.CreateEventIdAsync(EventApi.Timed(_club, title: "Coach's note"));

        var set = await _mia.SetOverridesAsync(note, EventApi.Group(_lions, "none"), EventApi.User(_adamId, "none"));

        Assert.Equal(2, set["items"]!.AsArray().Count);
        Assert.Equal("manage", await _adam.LevelOnAsync(note));
        Assert.Equal("manage", await _olga.LevelOnAsync(note));
        Assert.Equal("manage", await _mia.LevelOnAsync(note));
        Assert.Equal("none", await _vic.LevelOnAsync(note));
    }

    [Fact]
    public async Task A_contributor_cannot_share_with_an_outsider_but_a_manager_can()
    {
        // Example G / E: Eve has no calendar level; the calendar keeps creatorsMayShareExternally = false.
        var training = await _mia.CreateEventIdAsync(EventApi.Timed(_club, title: "Training"));

        using var refused = await _mia.PutOverridesAsync(training, [EventApi.User(_eveId, "read")]);

        var problem = await ProblemResponse.AssertProblemAsync(refused, HttpStatusCode.Forbidden, ErrorCodes.ExternalSharingNotAllowed);
        var violation = Assert.Single(problem["violations"]!.AsArray())!;
        Assert.Equal("user", (string?)violation["principal"]!["type"]);
        Assert.Equal(_eveId, (Guid)violation["principal"]!["id"]!);
        Assert.Equal("external_sharing", (string?)violation["reason"]);
        Assert.Equal("none", await _eve.LevelOnAsync(training));
        Assert.Empty(await _host.StoredOverridesAsync(training));

        // Restricting an outsider is not sharing; the manager may share.
        await _mia.SetOverridesAsync(training, EventApi.User(_eveId, "none"));
        await _adam.SetOverridesAsync(training, EventApi.User(_eveId, "read"));
        Assert.Equal("read", await _eve.LevelOnAsync(training));

        // Mia may lower the manager's share (unchanged/lowered entries are not re-checked) and remove it.
        await _mia.SetOverridesAsync(training, EventApi.User(_eveId, "free_busy"));
        Assert.Equal("free_busy", await _eve.LevelOnAsync(training));
        await _mia.SetOverridesAsync(training);
        Assert.Equal("none", await _eve.LevelOnAsync(training));
    }

    [Fact]
    public async Task Removing_an_exclusion_from_an_external_group_share_needs_external_rights()
    {
        // Engine review P1 through the api: Adam shares with the Friends group (outside the calendar) but excludes Eve.
        var party = await _mia.CreateEventIdAsync(EventApi.Timed(_club, title: "Party"));
        await _adam.SetOverridesAsync(party, EventApi.Group(_friends, "read"), EventApi.User(_eveId, "none"));
        Assert.Equal("none", await _eve.LevelOnAsync(party));

        using var refused = await _mia.PutOverridesAsync(party, [EventApi.Group(_friends, "read")]);

        var problem = await ProblemResponse.AssertProblemAsync(refused, HttpStatusCode.Forbidden, ErrorCodes.ExternalSharingNotAllowed);
        Assert.Equal("removal_exposes_external_share", (string?)Assert.Single(problem["violations"]!.AsArray())!["reason"]);
        Assert.Equal("none", await _eve.LevelOnAsync(party));
    }

    public static TheoryData<string> InvalidSets => new()
    {
        "manage",
        "duplicate",
        "foreign-group",
        "stranger",
    };

    [Theory]
    [MemberData(nameof(InvalidSets))]
    public async Task Invalid_sets_are_422_override_invalid(string kind)
    {
        var ev = await _adam.CreateEventIdAsync(EventApi.Timed(_club));
        var foreignGroup = await _eve.CreateGroupAsync("Eve's own");
        object[] overrides = kind switch
        {
            "manage" => [EventApi.User(_vicId, "manage")],
            "duplicate" => [EventApi.Everyone("read"), EventApi.Everyone("none")],
            "foreign-group" => [EventApi.Group(foreignGroup, "none")], // Adam is not in it and it has no access
            _ => [EventApi.User(_samId, "none")], // Sam shares nothing with Adam: not selectable (like an unknown id)
        };

        using var response = await _adam.PutOverridesAsync(ev, overrides);

        var problem = await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, ErrorCodes.OverrideInvalid);
        var reason = (string?)problem["violations"]![0]!["reason"];
        Assert.Equal(kind switch { "manage" => "level_too_high", "duplicate" => "duplicate_principal", "foreign-group" => "group_not_selectable", _ => "user_not_selectable" }, reason);
        Assert.NotNull(problem["errors"]!["overrides"]);

        using var unknown = await _adam.PutOverridesAsync(ev, [EventApi.User(Guid.CreateVersion7(), "none")]);
        Assert.Equal("user_not_selectable", (string?)(await ProblemResponse.AssertProblemAsync(unknown, HttpStatusCode.UnprocessableEntity, ErrorCodes.OverrideInvalid))["violations"]![0]!["reason"]);
    }

    [Fact]
    public async Task Malformed_entries_are_400()
    {
        var ev = await _adam.CreateEventIdAsync(EventApi.Timed(_club));

        foreach (var entry in new object[]
        {
            new { principal = new { type = "everyone" }, level = "owner" },
            new { principal = new { type = "robot" }, level = "read" },
            new { principal = new { type = "user" }, level = "read" },
            new { principal = new { type = "everyone", id = _vicId }, level = "read" },
            new { principal = new { type = "user", id = _vicId, minRole = "admin" }, level = "read" },
        })
        {
            using var response = await _adam.PutOverridesAsync(ev, [entry]);
            await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        }
    }

    [Fact]
    public async Task Only_floor_holders_read_and_change_overrides()
    {
        var ev = await _adam.CreateEventIdAsync(EventApi.Timed(_club));
        await _adam.SetOverridesAsync(ev, EventApi.User(_vicId, "edit"));

        // Vic edits through the override, but override-granted edit is not a floor.
        Assert.Equal("edit", await _vic.LevelOnAsync(ev));
        foreach (var client in new[] { _vic, _mia })
        {
            using var get = await client.GetAsync(new Uri($"/api/v1/events/{ev}/overrides", UriKind.Relative), Ct);
            var problem = await ProblemResponse.AssertProblemAsync(get, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);
            Assert.Equal("manage", (string?)problem["required"]);
            using var put = await client.PutOverridesAsync(ev, []);
            await ProblemResponse.AssertProblemAsync(put, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);
        }

        using var hidden = await _eve.GetAsync(new Uri($"/api/v1/events/{ev}/overrides", UriKind.Relative), Ct);
        await ProblemResponse.AssertProblemAsync(hidden, HttpStatusCode.NotFound, ErrorCodes.NotFound);
        Assert.Single((await _olga.GetOverridesAsync(ev)).Body["items"]!.AsArray());
    }

    [Fact]
    public async Task Replacing_maintains_has_overrides_acl_versions_the_sync_log_and_the_audit()
    {
        var ev = await _adam.CreateEventIdAsync(EventApi.Timed(_club));
        var (empty, etag) = await _adam.GetOverridesAsync(ev);
        Assert.Empty(empty["items"]!.AsArray());
        var calendarVersion = await _host.CalendarAclVersionAsync(_club);
        var vicVersion = await _host.AclVersionAsync(_vicId);
        var lionsVersion = await _host.GroupAclVersionAsync(_lions);

        // If-Match is required and must be current.
        using (var missing = await _adam.PutOverridesAsync(ev, [EventApi.Everyone("free_busy")], ifMatch: null))
        {
            await ProblemResponse.AssertProblemAsync(missing, HttpStatusCode.PreconditionRequired, ErrorCodes.PreconditionRequired);
        }

        using (var current = await _adam.PutOverridesAsync(ev, [EventApi.Everyone("free_busy"), EventApi.User(_vicId, "read"), EventApi.Group(_lions, "read", "admin")], ifMatch: etag))
        {
            Assert.Equal(HttpStatusCode.OK, current.StatusCode);
            var body = await current.JsonAsync();
            Assert.Equal(current.Headers.ETag!.ToString(), (string?)body["etag"]);
            Assert.Equal(["user", "group", "everyone"], body["items"]!.AsArray().Select(i => (string)i!["principal"]!["type"]!));
            Assert.Equal("vic", (string?)body["items"]![0]!["principalName"]);
            Assert.Equal("admin", (string?)body["items"]![1]!["principal"]!["minRole"]);
            Assert.Null(body["items"]![2]!["principalName"]);
        }

        using (var stale = await _adam.PutOverridesAsync(ev, [], ifMatch: etag))
        {
            await ProblemResponse.AssertProblemAsync(stale, HttpStatusCode.PreconditionFailed, ErrorCodes.PreconditionFailed);
        }

        Assert.True((await _host.StoredEventAsync(ev))!.HasOverrides);
        Assert.True((bool)(await _adam.GetEventAsync(ev)).Body["hasOverrides"]!);
        Assert.Equal(calendarVersion + 1, await _host.CalendarAclVersionAsync(_club));
        Assert.Equal(vicVersion + 1, await _host.AclVersionAsync(_vicId));
        Assert.Equal(lionsVersion + 1, await _host.GroupAclVersionAsync(_lions));
        Assert.Equal(CalendarChangeKind.Acl, (await _host.ChangesAsync(_club)).Last().Change);
        Assert.Equal("free_busy", await _mia.LevelOnAsync(ev));

        // The same set again changes nothing; removing all clears has_overrides.
        await _adam.SetOverridesAsync(ev, EventApi.Everyone("free_busy"), EventApi.User(_vicId, "read"), EventApi.Group(_lions, "read", "admin"));
        Assert.Equal(calendarVersion + 1, await _host.CalendarAclVersionAsync(_club));
        await _adam.SetOverridesAsync(ev);
        Assert.False((await _host.StoredEventAsync(ev))!.HasOverrides);
        Assert.Equal("read", await _mia.LevelOnAsync(ev));

        var audit = (await _host.AuditEventsAsync("event", ev)).Where(a => a.Action == "event.overrides.changed").ToList();
        Assert.Equal(2, audit.Count);
        Assert.Empty(Overrides(audit[0].Before));
        Assert.Contains("everyone → free_busy", Overrides(audit[0].After));
        Assert.Contains($"user:{_vicId} → read", Overrides(audit[1].Before));
        Assert.Empty(Overrides(audit[1].After));
    }

    [Fact]
    public async Task Frozen_calendars_allow_removing_overrides_only()
    {
        var ev = await _adam.CreateEventIdAsync(EventApi.Timed(_club));
        await _adam.SetOverridesAsync(ev, EventApi.Everyone("free_busy"), EventApi.User(_vicId, "edit"));
        await _host.ExecuteSqlAsync($"UPDATE calendars SET frozen_at = now() WHERE id = {_club}");

        using var add = await _adam.PutOverridesAsync(ev, [EventApi.Everyone("free_busy"), EventApi.User(_vicId, "edit"), EventApi.User(_miaId, "none")]);
        await ProblemResponse.AssertProblemAsync(add, HttpStatusCode.Conflict, ErrorCodes.CalendarFrozen);

        await _adam.SetOverridesAsync(ev, EventApi.User(_vicId, "read"));
        Assert.Equal("read", await _mia.LevelOnAsync(ev));
    }

    private static List<string> Overrides(string? state) =>
        [.. JsonNode.Parse(state!)!["overrides"]!.AsArray().Select(o => (string)o!)];

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
