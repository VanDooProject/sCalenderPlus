using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NodaTime;
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
/// Issue #44: single events CRUD — wall clock + zone, DST gap/overlap, ETag/If-Match, merge-patch, soft delete,
/// sync log, audit, and levels from the permission engine. Setup: group "Lions" (Olga owner, Adam admin, Mia
/// member, Vic viewer) owning the calendar "Club" (default role defaults), Eve outside with a free_busy grant.
/// </summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class EventTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly List<HttpClient> _clients = [];
    private ApiTestHost _host = null!;
    private HttpClient _olga = null!;
    private HttpClient _adam = null!;
    private HttpClient _mia = null!;
    private HttpClient _vic = null!;
    private HttpClient _eve = null!;
    private HttpClient _stranger = null!;
    private Guid _olgaId;
    private Guid _miaId;
    private Guid _eveId;
    private Guid _club;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await ApiTestHost.StartAsync(postgres);
        (_olgaId, _olga) = await PersonAsync("olga");
        var lions = await _olga.CreateGroupAsync();
        (_, _adam) = await MemberAsync(lions, "adam", GroupRole.Admin);
        (_miaId, _mia) = await MemberAsync(lions, "mia", GroupRole.Member);
        (_, _vic) = await MemberAsync(lions, "vic", GroupRole.Viewer);
        (_eveId, _eve) = await PersonAsync("eve");
        (_, _stranger) = await PersonAsync("stranger");
        _club = await _olga.CreateCalendarAsync("Club", lions);
        await _host.GrantAsync(_club, _eveId, CalendarLevel.FreeBusy);
    }

    public async ValueTask DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task A_contributor_creates_a_timed_event_with_wall_clock_zone_and_utc()
    {
        using var response = await _mia.SendJsonAsync(HttpMethod.Post, "/api/v1/events", new
        {
            calendarId = _club,
            title = "  Training ",
            description = "Bring shoes",
            location = "Pitch 2",
            url = "https://lions.example/training",
            status = "tentative",
            color = "#AA00FF",
            categories = (string[])["sport", "youth"],
            start = new { dateTime = "2026-11-02T18:00:00", timeZone = "Europe/Berlin" },
            end = new { dateTime = "2026-11-02T20:00" },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ev = await response.JsonAsync();
        var id = (Guid)ev["id"]!;
        Assert.Equal($"/api/v1/events/{id}", response.Headers.Location!.OriginalString);
        Assert.Equal(_club, (Guid)ev["calendarId"]!);
        Assert.Equal($"{id}@scalenderplus", (string?)ev["uid"]);
        Assert.Equal("Training", (string?)ev["title"]);
        Assert.Equal("Bring shoes", (string?)ev["description"]);
        Assert.Equal("tentative", (string?)ev["status"]);
        Assert.Equal("opaque", (string?)ev["transparency"]);
        Assert.Equal("#aa00ff", (string?)ev["color"]);
        Assert.Equal(["sport", "youth"], ev["categories"]!.AsArray().Select(c => (string)c!));
        Assert.False((bool)ev["allDay"]!);
        Assert.Equal("2026-11-02T18:00:00", (string?)ev["start"]!["dateTime"]);
        Assert.Equal("Europe/Berlin", (string?)ev["start"]!["timeZone"]);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 17, 0, 0, TimeSpan.Zero), (DateTimeOffset)ev["start"]!["utc"]!);
        Assert.Equal("Europe/Berlin", (string?)ev["end"]!["timeZone"]); // the end follows the start's zone
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 19, 0, 0, TimeSpan.Zero), (DateTimeOffset)ev["end"]!["utc"]!);
        Assert.Null(ev["start"]!["date"]);
        Assert.Equal("manage", (string?)ev["myLevel"]); // creator floor (contribute + creatorsManageOwnEvents)
        Assert.Equal(_miaId, (Guid)ev["createdBy"]!["id"]!);
        Assert.Equal("mia", (string?)ev["createdBy"]!["displayName"]);
        Assert.False((bool)ev["hasOverrides"]!);
        Assert.Equal(0, (int)ev["sequence"]!);
        Assert.Null(ev["warnings"]);
        Assert.Null(ev["recurrence"]);

        // The ETag of the creation is the one GET returns; others see the event with their own level.
        Assert.Equal(response.Headers.ETag!.ToString(), (await _mia.GetEventAsync(id)).ETag);
        Assert.Equal("read", (string?)(await _vic.GetEventAsync(id)).Body["myLevel"]);
        Assert.Equal("manage", (string?)(await _adam.GetEventAsync(id)).Body["myLevel"]);

        var audit = Assert.Single(await _host.AuditEventsAsync("event", id));
        Assert.Equal("event.created", audit.Action);
        Assert.Equal("Training", (string?)JsonNode.Parse(audit.After!)!["title"]);
        Assert.Equal(_olgaId, audit.SubjectId); // the group's billing owner

        var change = Assert.Single(await _host.ChangesAsync(_club));
        Assert.Equal((id, CalendarChangeKind.Upsert), (change.EventId, change.Change));
    }

    [Fact]
    public async Task The_zone_defaults_to_the_calendars_and_all_day_events_are_floating_dates()
    {
        var (timed, _) = await _mia.CreateEventAsync(EventApi.Timed(_club, timeZone: null));
        Assert.Equal("Europe/Berlin", (string?)timed["start"]!["timeZone"]);

        var (allDay, _) = await _mia.CreateEventAsync(EventApi.AllDay(_club, start: "2026-11-02", end: "2026-11-04"));
        Assert.True((bool)allDay["allDay"]!);
        Assert.Equal(new JsonObject { ["date"] = "2026-11-02" }.ToJsonString(), allDay["start"]!.ToJsonString());
        Assert.Equal(new JsonObject { ["date"] = "2026-11-04" }.ToJsonString(), allDay["end"]!.ToJsonString());

        var stored = (await _host.StoredEventAsync((Guid)allDay["id"]!))!;
        Assert.Null(stored.TimeZone);
        Assert.Equal(Instant.FromUtc(2026, 11, 1, 10, 0), stored.StartUtc); // padded for viewers in every zone
        Assert.Equal(Instant.FromUtc(2026, 11, 4, 14, 0), stored.EndUtc);
    }

    [Fact]
    public async Task A_time_in_the_dst_gap_is_shifted_forward_with_a_warning()
    {
        // 2026-03-29 02:30 does not exist in Berlin (02:00 → 03:00).
        using var response = await _mia.SendJsonAsync(HttpMethod.Post, "/api/v1/events", EventApi.Timed(_club, start: "2026-03-29T02:30:00", end: "2026-03-29T04:00:00"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ev = await response.JsonAsync();
        Assert.Equal("2026-03-29T03:30:00", (string?)ev["start"]!["dateTime"]);
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), (DateTimeOffset)ev["start"]!["utc"]!);
        var warning = Assert.Single(ev["warnings"]!.AsArray())!;
        Assert.Equal("time_shifted_dst_gap", (string?)warning["code"]);
        Assert.Equal("start", (string?)warning["field"]);
        Assert.Equal("2026-03-29T02:30:00", (string?)warning["requested"]);
        Assert.Equal("2026-03-29T03:30:00", (string?)warning["resolved"]);

        // The shifted time is what is stored and returned from now on (no warning on reads).
        var (read, etag) = await _mia.GetEventAsync((Guid)ev["id"]!);
        Assert.Equal("2026-03-29T03:30:00", (string?)read["start"]!["dateTime"]);
        Assert.Null(read["warnings"]);
        Assert.Equal(response.Headers.ETag!.ToString(), etag); // warnings are not part of the representation

        // Moving an event into the gap with PATCH warns too.
        using var patch = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{ev["id"]}", new { end = new { dateTime = "2026-03-29T02:45:00" }, start = new { dateTime = "2026-03-29T01:00:00" } }, "*", "application/merge-patch+json");
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var patched = await patch.JsonAsync();
        Assert.Equal("2026-03-29T03:45:00", (string?)patched["end"]!["dateTime"]);
        Assert.Equal("end", (string?)Assert.Single(patched["warnings"]!.AsArray())!["field"]);
    }

    [Fact]
    public async Task An_ambiguous_time_takes_the_earlier_offset_with_a_warning()
    {
        var (ev, _) = await _mia.CreateEventAsync(EventApi.Timed(_club, start: "2026-10-25T02:30:00", end: "2026-10-25T05:00:00"));

        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), (DateTimeOffset)ev["start"]!["utc"]!); // +02:00
        Assert.Equal("time_ambiguous_earlier_offset", (string?)Assert.Single(ev["warnings"]!.AsArray())!["code"]);
    }

    public static TheoryData<string, string, string> InvalidEvents => new()
    {
        { """{"title":"x","start":{"dateTime":"2026-11-02T18:00:00"},"end":{"dateTime":"2026-11-02T17:00:00"}}""", ErrorCodes.ValidationFailed, "end.dateTime" },
        { """{"title":"x","start":{"date":"2026-11-02"},"end":{"dateTime":"2026-11-02T17:00:00"}}""", ErrorCodes.ValidationFailed, "end" },
        { """{"title":"x","start":{"date":"2026-11-02"},"end":{"date":"2026-11-02"}}""", ErrorCodes.ValidationFailed, "end.date" },
        { """{"title":"x","start":{"dateTime":"2026-11-02T18:00:00+01:00"},"end":{"dateTime":"2026-11-02T19:00:00"}}""", ErrorCodes.ValidationFailed, "start.dateTime" },
        { """{"title":"x","start":{"dateTime":"2026-11-02T18:00:00","timeZone":"Europe/Berlin"},"end":{"dateTime":"2026-11-02T19:00:00","timeZone":"Europe/Paris"}}""", ErrorCodes.ValidationFailed, "end.timeZone" },
        { """{"title":"x","start":{"dateTime":"2026-11-02T18:00:00","timeZone":"Mars/Olympus"},"end":{"dateTime":"2026-11-02T19:00:00"}}""", ErrorCodes.TimeZoneInvalid, "start.timeZone" },
        { """{"title":" ","start":{"date":"2026-11-02"},"end":{"date":"2026-11-03"}}""", ErrorCodes.ValidationFailed, "title" },
        { """{"title":"x","status":"maybe","start":{"date":"2026-11-02"},"end":{"date":"2026-11-03"}}""", ErrorCodes.ValidationFailed, "status" },
        { """{"title":"x","url":"javascript:alert(1)","start":{"date":"2026-11-02"},"end":{"date":"2026-11-03"}}""", ErrorCodes.ValidationFailed, "url" },
        { """{"title":"x","uid":"has space","start":{"date":"2026-11-02"},"end":{"date":"2026-11-03"}}""", ErrorCodes.ValidationFailed, "uid" },
        { """{"title":"x","recurrence":{"rrule":"FREQ=WEEKLY"},"start":{"date":"2026-11-02"},"end":{"date":"2026-11-03"}}""", ErrorCodes.RecurrenceNotSupported, "recurrence" },
    };

    [Theory]
    [MemberData(nameof(InvalidEvents))]
    public async Task Invalid_events_are_refused_with_the_field(string json, string code, string field)
    {
        var body = JsonNode.Parse(json)!.AsObject();
        body["calendarId"] = _club.ToString();

        using var response = await _mia.SendJsonAsync(HttpMethod.Post, "/api/v1/events", body);

        var status = code == ErrorCodes.ValidationFailed ? HttpStatusCode.BadRequest : HttpStatusCode.UnprocessableEntity;
        var problem = await ProblemResponse.AssertProblemAsync(response, status, code);
        Assert.NotNull(problem["errors"]![field]);
        Assert.Empty(await _host.ChangesAsync(_club));
    }

    [Fact]
    public async Task Creating_needs_contribute_and_an_unfrozen_calendar()
    {
        using var viewer = await _vic.SendJsonAsync(HttpMethod.Post, "/api/v1/events", EventApi.Timed(_club));
        var problem = await ProblemResponse.AssertProblemAsync(viewer, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);
        Assert.Equal("contribute", (string?)problem["required"]);
        Assert.Equal("read", (string?)problem["actual"]);

        using var busy = await _eve.SendJsonAsync(HttpMethod.Post, "/api/v1/events", EventApi.Timed(_club));
        await ProblemResponse.AssertProblemAsync(busy, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);
        using var stranger = await _stranger.SendJsonAsync(HttpMethod.Post, "/api/v1/events", EventApi.Timed(_club));
        await ProblemResponse.AssertProblemAsync(stranger, HttpStatusCode.NotFound, ErrorCodes.NotFound);
        using var unknown = await _mia.SendJsonAsync(HttpMethod.Post, "/api/v1/events", EventApi.Timed(Guid.CreateVersion7()));
        await ProblemResponse.AssertProblemAsync(unknown, HttpStatusCode.NotFound, ErrorCodes.NotFound);

        var id = await _mia.CreateEventIdAsync(EventApi.Timed(_club));
        await _host.ExecuteSqlAsync($"UPDATE calendars SET frozen_at = now() WHERE id = {_club}");
        using var frozen = await _mia.SendJsonAsync(HttpMethod.Post, "/api/v1/events", EventApi.Timed(_club));
        await ProblemResponse.AssertProblemAsync(frozen, HttpStatusCode.Conflict, ErrorCodes.CalendarFrozen);
        using var patch = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{id}", new { title = "x" }, "*");
        await ProblemResponse.AssertProblemAsync(patch, HttpStatusCode.Conflict, ErrorCodes.CalendarFrozen);
        using var delete = await _mia.SendJsonAsync(HttpMethod.Delete, $"/api/v1/events/{id}", ifMatch: "*");
        await ProblemResponse.AssertProblemAsync(delete, HttpStatusCode.Conflict, ErrorCodes.CalendarFrozen);
        Assert.Equal("Board meeting", (string?)(await _vic.GetEventAsync(id)).Body["title"]); // still visible
    }

    [Fact]
    public async Task Stale_if_match_is_412_and_missing_if_match_428()
    {
        var id = await _mia.CreateEventIdAsync(EventApi.Timed(_club));
        var (_, etag) = await _mia.GetEventAsync(id);

        using var missing = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{id}", new { title = "A" });
        await ProblemResponse.AssertProblemAsync(missing, HttpStatusCode.PreconditionRequired, ErrorCodes.PreconditionRequired);

        using var first = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{id}", new { title = "A" }, etag, "application/merge-patch+json");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var newETag = first.Headers.ETag!.ToString();
        Assert.NotEqual(etag, newETag);
        Assert.Equal(newETag, (await _mia.GetEventAsync(id)).ETag);

        // Someone else's (or one's own earlier) ETag is stale now: 412, nothing changes.
        using var stale = await _adam.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{id}", new { title = "B" }, etag);
        await ProblemResponse.AssertProblemAsync(stale, HttpStatusCode.PreconditionFailed, ErrorCodes.PreconditionFailed);
        using var staleDelete = await _adam.SendJsonAsync(HttpMethod.Delete, $"/api/v1/events/{id}", ifMatch: etag);
        await ProblemResponse.AssertProblemAsync(staleDelete, HttpStatusCode.PreconditionFailed, ErrorCodes.PreconditionFailed);
        Assert.Equal("A", (string?)(await _mia.GetEventAsync(id)).Body["title"]);
    }

    [Fact]
    public async Task Patch_is_a_merge_patch_and_bumps_the_sequence_on_time_changes()
    {
        var (created, etag) = await _mia.CreateEventAsync(new
        {
            calendarId = _club,
            title = "Training",
            description = "Bring shoes",
            location = "Pitch 2",
            categories = (string[])["sport"],
            start = new { dateTime = "2026-11-02T18:00:00" },
            end = new { dateTime = "2026-11-02T20:00:00" },
        });
        var id = (Guid)created["id"]!;

        using var rename = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{id}", new { title = "Practice", description = "", location = (string?)null, categories = Array.Empty<string>() }, etag, "application/merge-patch+json");
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        var renamed = await rename.JsonAsync();
        Assert.Equal("Practice", (string?)renamed["title"]);
        Assert.Null(renamed["description"]); // "" removes it
        Assert.Equal("Pitch 2", (string?)renamed["location"]); // null leaves it unchanged
        Assert.Empty(renamed["categories"]!.AsArray());
        Assert.Equal(0, (int)renamed["sequence"]!); // no significant change

        using var move = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{id}", new { start = new { dateTime = "2026-11-02T19:00:00" } }, "*");
        var moved = await move.JsonAsync();
        Assert.Equal("2026-11-02T19:00:00", (string?)moved["start"]!["dateTime"]);
        Assert.Equal("2026-11-02T20:00:00", (string?)moved["end"]!["dateTime"]); // the end stays
        Assert.Equal(1, (int)moved["sequence"]!);

        using var toAllDay = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{id}", new { start = new { date = "2026-11-03" } }, "*");
        Assert.Equal("end", (string?)(await ProblemResponse.AssertProblemAsync(toAllDay, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed))["errors"]!.AsObject().Single().Key);
        using var allDay = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{id}", new { start = new { date = "2026-11-03" }, end = new { date = "2026-11-04" } }, "*");
        var switched = await allDay.JsonAsync();
        Assert.True((bool)switched["allDay"]!);
        Assert.Equal(2, (int)switched["sequence"]!);

        using var unchanged = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{id}", new { title = "Practice" }, "*");
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);

        using var recurrence = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{id}", new { recurrence = new { rrule = "FREQ=DAILY" } }, "*");
        await ProblemResponse.AssertProblemAsync(recurrence, HttpStatusCode.UnprocessableEntity, ErrorCodes.RecurrenceNotSupported);

        Assert.Equal(["event.created", "event.updated", "event.updated", "event.updated"], (await _host.AuditEventsAsync("event", id)).Select(a => a.Action));
        Assert.Equal(4, (await _host.ChangesAsync(_club)).Count(c => c.EventId == id && c.Change == CalendarChangeKind.Upsert));
    }

    [Fact]
    public async Task Levels_decide_who_edits_and_free_busy_viewers_get_times_only()
    {
        var adams = await _adam.CreateEventIdAsync(EventApi.Timed(_club, title: "Board meeting"));

        // Mia (contribute) reads other people's events, edits only her own (creator floor).
        Assert.Equal("read", (string?)(await _mia.GetEventAsync(adams)).Body["myLevel"]);
        using var miaEdits = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{adams}", new { title = "Mine now" }, "*");
        var problem = await ProblemResponse.AssertProblemAsync(miaEdits, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);
        Assert.Equal("edit", (string?)problem["required"]);
        Assert.Equal("read", (string?)problem["actual"]);
        using var miaDeletes = await _mia.SendJsonAsync(HttpMethod.Delete, $"/api/v1/events/{adams}", ifMatch: "*");
        await ProblemResponse.AssertProblemAsync(miaDeletes, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);

        // A calendar editor edits everything (level edit, no floor).
        var (editorId, editor) = await PersonAsync("ed");
        await _host.GrantAsync(_club, editorId, CalendarLevel.Edit);
        Assert.Equal("edit", (string?)(await editor.GetEventAsync(adams)).Body["myLevel"]);
        using var edited = await editor.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{adams}", new { location = "Clubhouse" }, "*");
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        // Eve (free_busy) sees times only — no other member at all — and transparent events not at all.
        var (busy, busyETag) = await _eve.GetEventAsync(adams);
        Assert.Equal(
            ["allDay", "calendarId", "end", "id", "myLevel", "start", "title", "transparency"],
            busy.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal));
        Assert.Null((string?)busy["title"]);
        Assert.Equal("free_busy", (string?)busy["myLevel"]);
        Assert.Equal("2026-11-02T18:00:00", (string?)busy["start"]!["dateTime"]);
        Assert.NotEqual((await _adam.GetEventAsync(adams)).ETag, busyETag);
        using var eveEdits = await _eve.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{adams}", new { title = "x" }, "*");
        await ProblemResponse.AssertProblemAsync(eveEdits, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);

        var free = await _adam.CreateEventIdAsync(EventApi.Timed(_club, title: "Lunch", transparency: "transparent"));
        using var eveFree = await _eve.GetAsync(new Uri($"/api/v1/events/{free}", UriKind.Relative), Ct);
        await ProblemResponse.AssertProblemAsync(eveFree, HttpStatusCode.NotFound, ErrorCodes.NotFound);
        Assert.Equal("read", (string?)(await _vic.GetEventAsync(free)).Body["myLevel"]);

        // Strangers: 404 like unknown events.
        using var stranger = await _stranger.GetAsync(new Uri($"/api/v1/events/{adams}", UriKind.Relative), Ct);
        await ProblemResponse.AssertProblemAsync(stranger, HttpStatusCode.NotFound, ErrorCodes.NotFound);
        using var strangerPatch = await _stranger.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{adams}", new { title = "x" }, "*");
        await ProblemResponse.AssertProblemAsync(strangerPatch, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Without_the_creator_floor_creators_only_read_their_events()
    {
        var (_, etag) = await _olga.GetCalendarAsync(_club);
        using var setting = await _olga.SendJsonAsync(HttpMethod.Patch, $"/api/v1/calendars/{_club}", new { creatorsManageOwnEvents = false }, etag);
        Assert.Equal(HttpStatusCode.OK, setting.StatusCode);

        var (ev, _) = await _mia.CreateEventAsync(EventApi.Timed(_club));

        Assert.Equal("read", (string?)ev["myLevel"]);
    }

    [Fact]
    public async Task The_etag_follows_the_callers_level()
    {
        var id = await _adam.CreateEventIdAsync(EventApi.Timed(_club));
        var (before, beforeETag) = await _eve.GetEventAsync(id);
        Assert.Equal("free_busy", (string?)before["myLevel"]);

        await _host.ExecuteSqlAsync($"UPDATE calendar_grants SET level = 2 WHERE calendar_id = {_club} AND principal_id = {_eveId}");

        var (after, afterETag) = await _eve.GetEventAsync(id);
        Assert.Equal("read", (string?)after["myLevel"]);
        Assert.Equal("Board meeting", (string?)after["title"]);
        Assert.NotEqual(beforeETag, afterETag);
    }

    [Fact]
    public async Task Delete_is_soft_logged_and_audited()
    {
        var (created, etag) = await _mia.CreateEventAsync(new
        {
            calendarId = _club,
            title = "Imported",
            uid = "abc-123@lions.example",
            start = new { date = "2026-11-02" },
            end = new { date = "2026-11-03" },
        });
        var id = (Guid)created["id"]!;
        Assert.Equal("abc-123@lions.example", (string?)created["uid"]);

        using var duplicate = await _mia.SendJsonAsync(HttpMethod.Post, "/api/v1/events", new { calendarId = _club, title = "Again", uid = "abc-123@lions.example", start = new { date = "2026-11-02" }, end = new { date = "2026-11-03" } });
        await ProblemResponse.AssertProblemAsync(duplicate, HttpStatusCode.Conflict, ErrorCodes.UidConflict);

        using var delete = await _mia.SendJsonAsync(HttpMethod.Delete, $"/api/v1/events/{id}", ifMatch: etag);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        using var gone = await _mia.GetAsync(new Uri($"/api/v1/events/{id}", UriKind.Relative), Ct);
        await ProblemResponse.AssertProblemAsync(gone, HttpStatusCode.NotFound, ErrorCodes.NotFound);
        using var again = await _mia.SendJsonAsync(HttpMethod.Delete, $"/api/v1/events/{id}", ifMatch: "*");
        await ProblemResponse.AssertProblemAsync(again, HttpStatusCode.NotFound, ErrorCodes.NotFound);

        var stored = (await _host.StoredEventAsync(id))!;
        Assert.NotNull(stored.DeletedAt); // kept for sync and restore
        Assert.Equal([CalendarChangeKind.Upsert, CalendarChangeKind.Delete], (await _host.ChangesAsync(_club)).Where(c => c.EventId == id).Select(c => c.Change));
        var audit = await _host.AuditEventsAsync("event", id);
        Assert.Equal("event.deleted", audit[^1].Action);
        Assert.Equal("Imported", (string?)JsonNode.Parse(audit[^1].Before!)!["title"]);

        // The UID of a deleted event may be used again.
        await _mia.CreateEventAsync(new { calendarId = _club, title = "Again", uid = "abc-123@lions.example", start = new { date = "2026-11-02" }, end = new { date = "2026-11-03" } });
    }

    [Fact]
    public async Task Deleting_a_calendar_deletes_its_events_and_sync_log()
    {
        var personal = await _mia.CreateCalendarAsync("Mine");
        var id = await _mia.CreateEventIdAsync(EventApi.Timed(personal));

        using var delete = await _mia.SendJsonAsync(HttpMethod.Delete, $"/api/v1/calendars/{personal}", ifMatch: "*");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        Assert.Null(await _host.StoredEventAsync(id));
        Assert.Empty(await _host.ChangesAsync(personal));
        using var gone = await _mia.GetAsync(new Uri($"/api/v1/events/{id}", UriKind.Relative), Ct);
        await ProblemResponse.AssertProblemAsync(gone, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Concurrent_updates_of_the_same_version_conflict()
    {
        var id = await _mia.CreateEventIdAsync(EventApi.Timed(_club));
        var (_, etag) = await _mia.GetEventAsync(id);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async i =>
        {
            using var response = await _adam.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{id}", new { title = $"T{i}" }, etag);
            return response.StatusCode;
        }));

        Assert.Contains(HttpStatusCode.OK, results);
        Assert.All(results, s => Assert.Contains(s, new[] { HttpStatusCode.OK, HttpStatusCode.PreconditionFailed }));
        var titles = await _host.QueryAsync(db => db.Events.AsNoTracking().Where(e => e.Id == id).Select(e => e.Title).ToListAsync(Ct));
        Assert.StartsWith("T", Assert.Single(titles));
    }

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
