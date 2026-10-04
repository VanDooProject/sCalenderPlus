using System.Net;
using System.Text.Json.Nodes;
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
/// Issue #51: occurrence edits — <b>this</b> (exceptions: changed, moved, cancelled), <b>this and following</b>
/// (split: creator kept, overrides copied, exceptions moved and re-keyed, RELATED-TO) and <b>all</b> (re-keying,
/// dropped exceptions), If-Match, sync log, audit, and share revocation covering series. Golden tests on Mia's
/// weekly "Training", Mondays 18:00–19:30 Berlin from 2 Nov 2026 (recurrence ids 17:00Z). Setup: group Lions
/// (Olga owner, Adam admin, Mia member, Vic viewer), Ed with an edit grant, Eve outside (in Adam's group "Friends").
/// </summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class OccurrenceEditTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string November = "from=2026-11-01T00:00:00Z&to=2026-12-01T00:00:00Z&expand=occurrences";

    private readonly List<HttpClient> _clients = [];
    private ApiTestHost _host = null!;
    private HttpClient _olga = null!;
    private HttpClient _adam = null!;
    private HttpClient _mia = null!;
    private HttpClient _vic = null!;
    private HttpClient _ed = null!;
    private HttpClient _eve = null!;
    private Guid _lions;
    private Guid _miaId;
    private Guid _vicId;
    private Guid _eveId;
    private Guid _club;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await ApiTestHost.StartAsync(postgres);
        (_, _olga) = await PersonAsync("olga");
        _lions = await _olga.CreateGroupAsync();
        (var adamId, _adam) = await MemberAsync("adam", GroupRole.Admin);
        (_miaId, _mia) = await MemberAsync("mia", GroupRole.Member);
        (_vicId, _vic) = await MemberAsync("vic", GroupRole.Viewer);
        (var edId, _ed) = await PersonAsync("ed");
        (_eveId, _eve) = await PersonAsync("eve");
        var friends = await _eve.CreateGroupAsync("Friends");
        await _host.AddMemberAsync(friends, adamId, GroupRole.Member);
        _club = await _olga.CreateCalendarAsync("Club", _lions);
        await _host.GrantAsync(_club, edId, CalendarLevel.Edit);
    }

    public async ValueTask DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task This_occurrence_changes_moves_and_cancels_single_occurrences()
    {
        var (id, etag) = await TrainingAsync("FREQ=WEEKLY;COUNT=6");

        // Changed fields: 9 Nov.
        using (var stale = await _mia.SendJsonAsync(HttpMethod.Patch, Occurrence(id, "2026-11-09T17:00:00Z"), new { title = "x" }, "\"stale\""))
        {
            await ProblemResponse.AssertProblemAsync(stale, HttpStatusCode.PreconditionFailed, ErrorCodes.PreconditionFailed);
        }

        using (var missing = await _mia.SendJsonAsync(HttpMethod.Patch, Occurrence(id, "2026-11-09T17:00:00Z"), new { title = "x" }))
        {
            await ProblemResponse.AssertProblemAsync(missing, HttpStatusCode.PreconditionRequired, ErrorCodes.PreconditionRequired);
        }

        using var changed = await _mia.SendJsonAsync(HttpMethod.Patch, Occurrence(id, "2026-11-09T17:00:00Z"), new { title = "Training (away)", location = "Away ground" }, etag);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var nine = await changed.JsonAsync();
        Assert.Equal($"{id}:2026-11-09T17:00:00Z", (string?)nine["occurrenceId"]);
        Assert.Equal("Training (away)", (string?)nine["title"]);
        Assert.Equal("Away ground", (string?)nine["location"]);
        Assert.True((bool)nine["modified"]!);
        Assert.Equal("2026-11-09T18:00:00", (string?)nine["start"]!["dateTime"]);
        etag = changed.Headers.ETag!.ToString();
        Assert.Equal(etag, (await _mia.GetEventAsync(id)).ETag); // the series' new ETag

        // Moved: 16 Nov → Tuesday 17 Nov 19:00–20:30 (start and end).
        using var moved = await _mia.SendJsonAsync(HttpMethod.Patch, Occurrence(id, "2026-11-16T17:00:00Z"), new { start = new { dateTime = "2026-11-17T19:00:00" }, end = new { dateTime = "2026-11-17T20:30:00" } }, etag);
        var sixteen = await moved.JsonAsync();
        Assert.Equal("2026-11-16T17:00:00Z", (string?)sixteen["recurrenceId"]); // the key stays the original start
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 18, 0, 0, TimeSpan.Zero), (DateTimeOffset)sixteen["start"]!["utc"]!);
        Assert.Equal("Training", (string?)sixteen["title"]);
        etag = moved.Headers.ETag!.ToString();

        // Cancelled: 23 Nov.
        using (var cancelled = await _mia.SendJsonAsync(HttpMethod.Delete, Occurrence(id, "2026-11-23T17:00:00Z"), null, etag))
        {
            Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        }

        // Golden: the occurrences of November and the master's exceptions.
        var items = await WindowAsync(_vic, November);
        Assert.Equal(
            [("2026-11-02T17:00:00Z", "2026-11-02T18:00:00", "Training"), ("2026-11-09T17:00:00Z", "2026-11-09T18:00:00", "Training (away)"), ("2026-11-16T17:00:00Z", "2026-11-17T19:00:00", "Training"), ("2026-11-30T17:00:00Z", "2026-11-30T18:00:00", "Training")],
            items.Select(i => ((string?)i!["recurrenceId"], (string?)i["start"]!["dateTime"], (string?)i["title"])));
        var (master, _) = await _vic.GetEventAsync(id);
        AssertJson(
            """[{"recurrenceId":"2026-11-09T17:00:00Z","title":"Training (away)","location":"Away ground"},{"recurrenceId":"2026-11-16T17:00:00Z","start":{"dateTime":"2026-11-17T19:00:00","timeZone":"Europe/Berlin","utc":"2026-11-17T18:00:00+00:00"},"end":{"dateTime":"2026-11-17T20:30:00","timeZone":"Europe/Berlin","utc":"2026-11-17T19:30:00+00:00"}},{"recurrenceId":"2026-11-23T17:00:00Z","cancelled":true}]""",
            master["exceptions"]);
        Assert.Equal(2, (int)master["sequence"]!); // the move and the cancellation

        // Cancelled occurrences are gone for edits; others answer 404 too.
        foreach (var path in new[] { Occurrence(id, "2026-11-23T17:00:00Z"), Occurrence(id, "2026-11-10T17:00:00Z"), Occurrence(id, "2026-12-14T17:00:00Z"), Occurrence(id, "not-a-time") })
        {
            using var gone = await _mia.SendJsonAsync(HttpMethod.Patch, path, new { title = "x" }, "*");
            await ProblemResponse.AssertProblemAsync(gone, HttpStatusCode.NotFound, ErrorCodes.NotFound);
        }

        using (var again = await _mia.SendJsonAsync(HttpMethod.Delete, Occurrence(id, "2026-11-23T17:00:00Z"), null, "*"))
        {
            await ProblemResponse.AssertProblemAsync(again, HttpStatusCode.NotFound, ErrorCodes.NotFound);
        }

        // Times keep the series' kind and zone.
        using (var allDay = await _mia.SendJsonAsync(HttpMethod.Patch, Occurrence(id, "2026-11-30T17:00:00Z"), new { start = new { date = "2026-11-30" }, end = new { date = "2026-12-01" } }, "*"))
        {
            await ProblemResponse.AssertProblemAsync(allDay, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        }

        using (var zone = await _mia.SendJsonAsync(HttpMethod.Patch, Occurrence(id, "2026-11-30T17:00:00Z"), new { start = new { dateTime = "2026-11-30T18:00:00", timeZone = "Europe/London" } }, "*"))
        {
            await ProblemResponse.AssertProblemAsync(zone, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        }

        // Moving the last occurrence beyond the series' end extends it.
        using (var later = await _mia.SendJsonAsync(HttpMethod.Patch, Occurrence(id, "2026-12-07T17:00:00Z"), new { start = new { dateTime = "2026-12-20T10:00:00" }, end = new { dateTime = "2026-12-20T11:00:00" } }, "*"))
        {
            Assert.Equal(HttpStatusCode.OK, later.StatusCode);
        }

        Assert.Equal(Instant.FromUtc(2026, 12, 20, 10, 0), (await _host.StoredEventAsync(id))!.SeriesUntilUtc);
        Assert.Equal("2026-12-07T17:00:00Z", (string?)Assert.Single(await WindowAsync(_vic, "from=2026-12-15T00:00:00Z&to=2027-01-01T00:00:00Z&expand=occurrences"))!["recurrenceId"]);

        // Values equal to the series' remove the override; an exception without overrides goes.
        using (var back = await _mia.SendJsonAsync(HttpMethod.Patch, Occurrence(id, "2026-11-09T17:00:00Z"), new { title = "Training", location = "" }, "*"))
        {
            var reverted = await back.JsonAsync();
            Assert.Null(reverted["modified"]);
            Assert.Null(reverted["location"]);
        }

        Assert.Equal(3, await _host.QueryAsync(db => Task.FromResult(db.EventExceptions.Count(x => x.EventId == id))));
        Assert.Equal(
            ["event.created", "event.occurrence.updated", "event.occurrence.updated", "event.occurrence.cancelled", "event.occurrence.updated", "event.occurrence.updated"],
            (await _host.AuditEventsAsync("event", id)).Select(a => a.Action));
        Assert.Equal(6, (await _host.ChangesAsync(_club)).Count(c => c.EventId == id && c.Change == CalendarChangeKind.Upsert));

        // Not a series: no occurrences.
        var single = await _mia.CreateEventIdAsync(EventApi.Timed(_club));
        using var notSeries = await _mia.SendJsonAsync(HttpMethod.Delete, Occurrence(single, "2026-11-02T17:00:00Z"), null, "*");
        await ProblemResponse.AssertProblemAsync(notSeries, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task This_and_following_splits_the_series_and_an_edit_user_does_not_become_the_creator()
    {
        var (id, _) = await TrainingAsync("FREQ=WEEKLY;COUNT=10"); // 2 Nov … 4 Jan
        await PatchOccurrenceAsync(id, "2026-11-09T17:00:00Z", new { title = "Training (away)" });
        await PatchOccurrenceAsync(id, "2026-11-30T17:00:00Z", new { start = new { dateTime = "2026-12-01T18:00:00" }, end = new { dateTime = "2026-12-01T19:30:00" } });
        await CancelAsync(id, "2026-12-14T17:00:00Z");
        await _adam.SetOverridesAsync(id, EventApi.User(_vicId, "none"), EventApi.User(_eveId, "read"));
        var original = await _host.StoredEventAsync(id);
        var etag = (await _ed.GetEventAsync(id)).ETag;
        Assert.Equal("edit", (string?)(await _ed.GetEventAsync(id)).Body["myLevel"]);
        var eveVersion = await _host.UserAclVersionAsync(_eveId);

        // Ed (edit through a grant, not the creator) changes "this and following" from 23 Nov: later and renamed.
        using var split = await _ed.SendJsonAsync(HttpMethod.Post, $"/api/v1/events/{id}/split", new
        {
            recurrenceId = "2026-11-23T17:00:00Z",
            title = "Winter training",
            start = new { dateTime = "2026-11-23T19:00:00" },
            end = new { dateTime = "2026-11-23T20:30:00" },
        }, etag);

        Assert.Equal(HttpStatusCode.Created, split.StatusCode);
        var created = await split.JsonAsync();
        var newId = (Guid)created["id"]!;
        Assert.Equal($"/api/v1/events/{newId}", split.Headers.Location!.OriginalString);
        Assert.Equal($"{newId}@scalenderplus", (string?)created["uid"]);
        Assert.Equal(original!.Uid, (string?)created["relatedTo"]);
        Assert.Equal(_miaId, (Guid)created["createdBy"]!["id"]!); // the creator stays Mia
        Assert.Equal("edit", (string?)created["myLevel"]);
        Assert.Equal("Winter training", (string?)created["title"]);
        Assert.Equal("2026-11-23T19:00:00", (string?)created["start"]!["dateTime"]);
        Assert.Equal("FREQ=WEEKLY;COUNT=7", (string?)created["recurrence"]!["rrule"]);
        Assert.Null(created["droppedExceptions"]);
        Assert.Equal(_miaId, (await _host.StoredEventAsync(newId))!.CreatorUserId);
        Assert.Equal("manage", (string?)(await _mia.GetEventAsync(newId)).Body["myLevel"]); // her creator floor

        // The later exceptions moved along, re-keyed by the new start time (+1 h); the moved one keeps its times.
        AssertJson(
            """[{"recurrenceId":"2026-11-30T18:00:00Z","start":{"dateTime":"2026-12-01T18:00:00","timeZone":"Europe/Berlin","utc":"2026-12-01T17:00:00+00:00"},"end":{"dateTime":"2026-12-01T19:30:00","timeZone":"Europe/Berlin","utc":"2026-12-01T18:30:00+00:00"}},{"recurrenceId":"2026-12-14T18:00:00Z","cancelled":true}]""",
            created["exceptions"]);
        var (rest, _) = await _mia.GetEventAsync(id);
        Assert.Equal("FREQ=WEEKLY;COUNT=3", (string?)rest["recurrence"]!["rrule"]);
        Assert.Equal("2026-11-09T17:00:00Z", (string?)Assert.Single(rest["exceptions"]!.AsArray())!["recurrenceId"]);
        Assert.Equal(original.Sequence + 1, (int)rest["sequence"]!);
        Assert.Equal(Instant.FromUtc(2026, 11, 16, 18, 30), (await _host.StoredEventAsync(id))!.SeriesUntilUtc);

        // Golden: both series together.
        var items = await WindowAsync(_mia, "from=2026-11-01T00:00:00Z&to=2027-01-10T00:00:00Z&expand=occurrences");
        Assert.Equal(
            [
                (id, "2026-11-02T18:00:00", "Training"), (id, "2026-11-09T18:00:00", "Training (away)"), (id, "2026-11-16T18:00:00", "Training"),
                (newId, "2026-11-23T19:00:00", "Winter training"), (newId, "2026-12-01T18:00:00", "Winter training"), (newId, "2026-12-07T19:00:00", "Winter training"),
                (newId, "2026-12-21T19:00:00", "Winter training"), (newId, "2026-12-28T19:00:00", "Winter training"), (newId, "2027-01-04T19:00:00", "Winter training"),
            ],
            items.Select(i => ((Guid)i!["id"]!, (string?)i["start"]!["dateTime"], (string?)i["title"])));

        // The overrides were copied (no new privacy decision): Vic still cannot see it, Eve reads it as shared.
        var (overrides, _) = await _adam.GetOverridesAsync(newId);
        Assert.Equal(2, overrides["items"]!.AsArray().Count);
        Assert.Equal("none", await _vic.LevelOnAsync(newId));
        Assert.Equal("read", await _eve.LevelOnAsync(newId));
        Assert.True(await _host.UserAclVersionAsync(_eveId) > eveVersion);

        // Sync log and audit.
        var changes = await _host.ChangesAsync(_club);
        Assert.Equal([CalendarChangeKind.Upsert, CalendarChangeKind.Acl], changes.Where(c => c.EventId == newId).Select(c => c.Change));
        Assert.Equal(CalendarChangeKind.Upsert, changes.Last(c => c.EventId == id).Change);
        var splitAudit = (await _host.AuditEventsAsync("event", id))[^1];
        Assert.Equal("event.split", splitAudit.Action);
        Assert.Equal(newId, (Guid)JsonNode.Parse(splitAudit.After!)!["newEventId"]!);
        var createdAudit = Assert.Single(await _host.AuditEventsAsync("event", newId));
        Assert.Equal("event.created", createdAudit.Action);
        Assert.Equal(id, (Guid)JsonNode.Parse(createdAudit.After!)!["splitFrom"]!);
    }

    [Fact]
    public async Task Splitting_open_ended_series_and_refusals()
    {
        var (id, etag) = await TrainingAsync("FREQ=WEEKLY");

        using (var first = await _mia.SendJsonAsync(HttpMethod.Post, $"/api/v1/events/{id}/split", new { recurrenceId = "2026-11-02T17:00:00Z" }, "*"))
        {
            Assert.NotNull((await ProblemResponse.AssertProblemAsync(first, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed))["errors"]!["recurrenceId"]);
        }

        using (var none = await _mia.SendJsonAsync(HttpMethod.Post, $"/api/v1/events/{id}/split", new { recurrenceId = "2026-11-03T17:00:00Z" }, "*"))
        {
            await ProblemResponse.AssertProblemAsync(none, HttpStatusCode.NotFound, ErrorCodes.NotFound);
        }

        using (var reader = await _vic.SendJsonAsync(HttpMethod.Post, $"/api/v1/events/{id}/split", new { recurrenceId = "2026-11-16T17:00:00Z" }, "*"))
        {
            await ProblemResponse.AssertProblemAsync(reader, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);
        }

        using var split = await _mia.SendJsonAsync(HttpMethod.Post, $"/api/v1/events/{id}/split", new { recurrenceId = "2026-11-16T17:00:00Z", location = "Hall" }, etag);
        var created = await split.JsonAsync();

        Assert.Equal("FREQ=WEEKLY", (string?)created["recurrence"]!["rrule"]);
        Assert.Equal("Hall", (string?)created["location"]);
        Assert.Equal("FREQ=WEEKLY;UNTIL=20261116T165959Z", (string?)(await _mia.GetEventAsync(id)).Body["recurrence"]!["rrule"]);
        Assert.Equal(Instant.FromUtc(2026, 11, 9, 18, 30), (await _host.StoredEventAsync(id))!.SeriesUntilUtc);
        Assert.Null((await _host.StoredEventAsync((Guid)created["id"]!))!.SeriesUntilUtc);
    }

    [Fact]
    public async Task All_occurrences_re_keys_exceptions_and_drops_those_without_an_occurrence()
    {
        var (id, _) = await TrainingAsync("FREQ=WEEKLY;COUNT=4");
        await PatchOccurrenceAsync(id, "2026-11-09T17:00:00Z", new { title = "Training (away)" });
        await PatchOccurrenceAsync(id, "2026-11-16T17:00:00Z", new { start = new { dateTime = "2026-11-17T19:00:00" }, end = new { dateTime = "2026-11-17T20:30:00" } });
        await CancelAsync(id, "2026-11-23T17:00:00Z");

        // One hour later, every week: the exceptions follow (+1 h); the moved occurrence keeps its time.
        using var later = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{id}", new { start = new { dateTime = "2026-11-02T19:00:00" }, end = new { dateTime = "2026-11-02T20:30:00" } }, "*");
        var series = await later.JsonAsync();
        Assert.Null(series["droppedExceptions"]);
        Assert.Equal(
            ["2026-11-09T18:00:00Z", "2026-11-16T18:00:00Z", "2026-11-23T18:00:00Z"],
            series["exceptions"]!.AsArray().Select(x => (string?)x!["recurrenceId"]));
        Assert.Equal(
            [("2026-11-02T19:00:00", "Training"), ("2026-11-09T19:00:00", "Training (away)"), ("2026-11-17T19:00:00", "Training")],
            (await WindowAsync(_vic, November)).Select(i => ((string?)i!["start"]!["dateTime"], (string?)i["title"])));

        // A shorter rule: exceptions without an occurrence are dropped and reported.
        using var shorter = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{id}", new { recurrence = new { rrule = "FREQ=WEEKLY;COUNT=2" } }, "*");
        var two = await shorter.JsonAsync();
        Assert.Equal(["2026-11-16T18:00:00Z", "2026-11-23T18:00:00Z"], two["droppedExceptions"]!.AsArray().Select(d => (string?)d));
        Assert.Equal("2026-11-09T18:00:00Z", (string?)Assert.Single(two["exceptions"]!.AsArray())!["recurrenceId"]);
        Assert.Equal(Instant.FromUtc(2026, 11, 9, 19, 30), (await _host.StoredEventAsync(id))!.SeriesUntilUtc);

        // Another weekday (rule without BYDAY): the exceptions move to the new day.
        using var tuesday = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{id}", new { start = new { dateTime = "2026-11-03T19:00:00" }, end = new { dateTime = "2026-11-03T20:30:00" } }, "*");
        Assert.Equal("2026-11-10T18:00:00Z", (string?)Assert.Single((await tuesday.JsonAsync())["exceptions"]!.AsArray())!["recurrenceId"]);

        // Switching to all-day drops every exception.
        using var allDay = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{id}", new { start = new { date = "2026-11-03" }, end = new { date = "2026-11-04" } }, "*");
        var dates = await allDay.JsonAsync();
        Assert.Equal(["2026-11-10T18:00:00Z"], dates["droppedExceptions"]!.AsArray().Select(d => (string?)d));
        Assert.Equal("FREQ=WEEKLY;COUNT=2", (string?)dates["recurrence"]!["rrule"]);
        Assert.Equal(["2026-11-03", "2026-11-10"], (await WindowAsync(_vic, November)).Select(i => (string?)i!["recurrenceId"]));
    }

    [Fact]
    public async Task Re_keying_consecutive_exceptions_by_a_day_passes_the_unique_key()
    {
        // Daily exceptions on 3 and 4 Nov shifted by +1 day: 3 → 4 collides with 4 until 4 → 5 (deferred constraint).
        var (id, _) = await TrainingAsync("FREQ=DAILY;COUNT=5");
        await PatchOccurrenceAsync(id, "2026-11-03T17:00:00Z", new { title = "Three" });
        await PatchOccurrenceAsync(id, "2026-11-04T17:00:00Z", new { title = "Four" });

        using var shifted = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{id}", new { start = new { dateTime = "2026-11-03T18:00:00" }, end = new { dateTime = "2026-11-03T19:30:00" } }, "*");

        Assert.Equal(HttpStatusCode.OK, shifted.StatusCode);
        Assert.Equal(
            [("2026-11-04T17:00:00Z", "Three"), ("2026-11-05T17:00:00Z", "Four")],
            (await shifted.JsonAsync())["exceptions"]!.AsArray().Select(x => ((string?)x!["recurrenceId"], (string?)x["title"])));
    }

    [Fact]
    public async Task Losing_calendar_access_revokes_shares_of_series_with_their_exceptions()
    {
        await _host.AddMemberAsync(_lions, _eveId, GroupRole.Viewer);
        var (id, _) = await TrainingAsync("FREQ=WEEKLY;COUNT=6");
        await PatchOccurrenceAsync(id, "2026-11-16T17:00:00Z", new { start = new { dateTime = "2026-11-17T19:00:00" }, end = new { dateTime = "2026-11-17T20:30:00" } });
        await _adam.SetOverridesAsync(id, EventApi.User(_eveId, "edit"));
        using (var split = await _mia.SendJsonAsync(HttpMethod.Post, $"/api/v1/events/{id}/split", new { recurrenceId = "2026-11-30T17:00:00Z" }, "*"))
        {
            Assert.Equal(HttpStatusCode.Created, split.StatusCode);
        }

        Assert.Equal(6, (await WindowAsync(_eve, "from=2026-11-01T00:00:00Z&to=2026-12-15T00:00:00Z&expand=occurrences")).Count); // 2, 9, 17 (moved), 23 Nov; 30 Nov, 7 Dec (new series)

        // Eve leaves Lions: her shares on both series go; no occurrence (moved ones included) remains for her.
        using (var leave = await _eve.SendJsonAsync(HttpMethod.Delete, $"/api/v1/groups/{_lions}/members/{_eveId}", null, "*"))
        {
            Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
        }

        Assert.Empty(await WindowAsync(_eve, "from=2026-11-01T00:00:00Z&to=2026-12-15T00:00:00Z&expand=occurrences"));
        Assert.Empty(await _host.QueryAsync(db => Task.FromResult(db.EventOverrides.Where(o => o.PrincipalId == _eveId).ToList())));
        var acl = (await _host.ChangesAsync(_club)).Where(c => c.Change == CalendarChangeKind.Acl).GroupBy(c => c.EventId).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(2, acl.Count); // set + revoked on the original; copied + revoked on the new series
        Assert.All(acl.Values, count => Assert.Equal(2, count));
    }

    private async Task<(Guid Id, string ETag)> TrainingAsync(string rrule)
    {
        var (body, etag) = await _mia.CreateEventAsync(new
        {
            calendarId = _club,
            title = "Training",
            start = new { dateTime = "2026-11-02T18:00:00", timeZone = "Europe/Berlin" },
            end = new { dateTime = "2026-11-02T19:30:00" },
            recurrence = new { rrule },
        });
        return ((Guid)body["id"]!, etag);
    }

    private async Task PatchOccurrenceAsync(Guid id, string recurrenceId, object body)
    {
        using var response = await _mia.SendJsonAsync(HttpMethod.Patch, Occurrence(id, recurrenceId), body, "*");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private async Task CancelAsync(Guid id, string recurrenceId)
    {
        using var response = await _mia.SendJsonAsync(HttpMethod.Delete, Occurrence(id, recurrenceId), null, "*");
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
    }

    private static void AssertJson(string expected, JsonNode? actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), actual), $"Expected {expected}\nActual   {actual?.ToJsonString()}");

    private static string Occurrence(Guid id, string recurrenceId) => $"/api/v1/events/{id}/occurrences/{recurrenceId}";

    private static async Task<JsonArray> WindowAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(new Uri("/api/v1/events?" + query, UriKind.Relative), Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.JsonAsync())["items"]!.AsArray();
    }

    private async Task<(Guid Id, HttpClient Client)> PersonAsync(string name)
    {
        var email = ApiTestHost.UniqueEmail(name);
        var id = await _host.CreateUserAsync(email, displayName: name);
        var client = await _host.SignedInClientAsync(email);
        _clients.Add(client);
        return (id, client);
    }

    private async Task<(Guid Id, HttpClient Client)> MemberAsync(string name, GroupRole role)
    {
        var person = await PersonAsync(name);
        await _host.AddMemberAsync(_lions, person.Id, role);
        return person;
    }
}
