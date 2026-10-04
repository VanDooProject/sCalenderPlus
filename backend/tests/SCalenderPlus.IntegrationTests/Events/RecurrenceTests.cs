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
/// Issue #50: recurring events — RRULE/RDATE/EXDATE storage, <c>series_until_utc</c>, expansion in the series'
/// zone (DST-stable local time), all-day series, the window with and without <c>expand=occurrences</c>, the busy
/// projection and "Shared with me" for series. Setup: group Lions (Olga owner, Mia member, Vic viewer) owning
/// "Club" (Europe/Berlin), Eve outside with a free_busy grant.
/// </summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class RecurrenceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly List<HttpClient> _clients = [];
    private ApiTestHost _host = null!;
    private HttpClient _olga = null!;
    private HttpClient _mia = null!;
    private HttpClient _vic = null!;
    private HttpClient _eve = null!;
    private Guid _eveId;
    private Guid _lions;
    private Guid _club;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await ApiTestHost.StartAsync(postgres);
        (_, _olga) = await PersonAsync("olga");
        _lions = await _olga.CreateGroupAsync();
        (_, _mia) = await MemberAsync("mia", GroupRole.Member);
        (_, _vic) = await MemberAsync("vic", GroupRole.Viewer);
        (_eveId, _eve) = await PersonAsync("eve");
        _club = await _olga.CreateCalendarAsync("Club", _lions);
        await _host.GrantAsync(_club, _eveId, CalendarLevel.FreeBusy);
    }

    public async ValueTask DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task A_weekly_series_keeps_its_local_time_across_DST()
    {
        // Mondays 18:00–20:00 Berlin from 19 Oct 2026; summer time ends on 25 Oct.
        var (series, etag) = await _mia.CreateEventAsync(Weekly("rrule:freq=weekly;count=4", "2026-10-19T18:00:00", "2026-10-19T20:00:00"));
        var id = (Guid)series["id"]!;
        Assert.Equal("FREQ=WEEKLY;COUNT=4", (string?)series["recurrence"]!["rrule"]); // canonical
        Assert.Null(series["recurrence"]!["rdates"]);
        Assert.Null(series["exceptions"]);
        Assert.Equal("2026-10-19T18:00:00", (string?)series["start"]!["dateTime"]); // the master is the first occurrence

        var items = await WindowAsync(_vic, "from=2026-10-01T00:00:00Z&to=2026-12-01T00:00:00Z&expand=occurrences");

        Assert.Equal(4, items.Count);
        Assert.All(items, i => Assert.EndsWith("T18:00:00", (string?)i!["start"]!["dateTime"], StringComparison.Ordinal));
        Assert.All(items, i => Assert.EndsWith("T20:00:00", (string?)i!["end"]!["dateTime"], StringComparison.Ordinal));
        Assert.Equal(
            ["2026-10-19T16:00:00Z", "2026-10-26T17:00:00Z", "2026-11-02T17:00:00Z", "2026-11-09T17:00:00Z"],
            items.Select(i => (string?)i!["recurrenceId"]));
        Assert.Equal($"{id}:2026-10-26T17:00:00Z", (string?)items[1]!["occurrenceId"]);
        Assert.All(items, i => Assert.Equal(id, (Guid)i!["id"]!));
        Assert.All(items, i => Assert.Equal("FREQ=WEEKLY;COUNT=4", (string?)i!["recurrence"]!["rrule"]));
        Assert.All(items, i => Assert.Null(i!["modified"]));

        // Occurrences carry the series' ETag (the If-Match of occurrence edits); for the editor it is the GET ETag.
        var mine = await WindowAsync(_mia, "from=2026-10-01T00:00:00Z&to=2026-12-01T00:00:00Z&expand=occurrences");
        Assert.All(mine, i => Assert.Equal(etag, (string?)i!["etag"]));
        Assert.Equal(etag, (await _mia.GetEventAsync(id)).ETag);

        // Without expand: the master (sync clients), once.
        var masters = await WindowAsync(_vic, "from=2026-11-01T00:00:00Z&to=2026-12-01T00:00:00Z");
        var master = Assert.Single(masters);
        Assert.Null(master!["occurrenceId"]);
        Assert.Equal("2026-10-19T18:00:00", (string?)master["start"]!["dateTime"]);

        var stored = (await _host.StoredEventAsync(id))!;
        Assert.Equal(Instant.FromUtc(2026, 11, 9, 19, 0), stored.SeriesUntilUtc); // COUNT → end of the last occurrence
        Assert.Empty(await WindowAsync(_vic, "from=2026-11-10T00:00:00Z&to=2026-12-01T00:00:00Z"));
    }

    [Fact]
    public async Task UNTIL_and_open_ended_series_bound_the_series()
    {
        var until = await _mia.CreateEventIdAsync(Weekly("FREQ=DAILY;UNTIL=20261130", "2026-11-01T08:00:00", "2026-11-01T09:00:00"));
        var open = await _mia.CreateEventIdAsync(Weekly("FREQ=YEARLY", "2026-11-03T08:00:00", "2026-11-03T09:00:00"));

        // UNTIL as a date in a timed series: through the end of that day in the series' zone, stored in UTC.
        Assert.Equal("FREQ=DAILY;UNTIL=20261130T225959Z", (string?)(await _mia.GetEventAsync(until)).Body["recurrence"]!["rrule"]);
        Assert.Equal(Instant.FromUtc(2026, 11, 30, 8, 0), (await _host.StoredEventAsync(until))!.SeriesUntilUtc);
        Assert.Null((await _host.StoredEventAsync(open))!.SeriesUntilUtc); // infinite

        Assert.Equal(30, (await WindowAsync(_vic, "from=2026-11-01T00:00:00Z&to=2026-12-01T00:00:00Z&expand=occurrences")).Count(i => (Guid)i!["id"]! == until));
        var far = await WindowAsync(_vic, "from=2040-10-01T00:00:00Z&to=2040-12-01T00:00:00Z&expand=occurrences");
        Assert.Equal("2040-11-03T07:00:00Z", (string?)Assert.Single(far)!["recurrenceId"]);
    }

    [Fact]
    public async Task All_day_series_with_RDATEs_and_EXDATEs_are_floating_dates()
    {
        var id = await _mia.CreateEventIdAsync(new
        {
            calendarId = _club,
            title = "Tournament",
            start = new { date = "2026-11-07" },
            end = new { date = "2026-11-09" },
            recurrence = new { rrule = "FREQ=WEEKLY;UNTIL=20261205", exdates = (string[])["2026-11-14"], rdates = (string[])["2026-11-25"] },
        });
        var (series, _) = await _mia.GetEventAsync(id);
        Assert.Equal("FREQ=WEEKLY;UNTIL=20261205", (string?)series["recurrence"]!["rrule"]);
        Assert.Equal(["2026-11-25"], series["recurrence"]!["rdates"]!.AsArray().Select(d => (string?)d));
        Assert.Equal(["2026-11-14"], series["recurrence"]!["exdates"]!.AsArray().Select(d => (string?)d));

        var items = await WindowAsync(_vic, "from=2026-11-01T00:00:00Z&to=2026-12-31T00:00:00Z&expand=occurrences&timeZone=Pacific/Honolulu");

        Assert.Equal(["2026-11-07", "2026-11-21", "2026-11-25", "2026-11-28", "2026-12-05"], items.Select(i => (string?)i!["recurrenceId"]));
        Assert.All(items, i => Assert.True((bool)i!["allDay"]!));
        Assert.Equal("2026-11-27", (string?)items[2]!["end"]!["date"]);
        Assert.Equal(EventTimes.PaddedEnd(new LocalDate(2026, 12, 7)), (await _host.StoredEventAsync(id))!.SeriesUntilUtc);

        // Placed by date in the viewer's zone: a window that is 7 Nov in Kiritimati only.
        var kiritimati = await WindowAsync(_vic, "from=2026-11-06T10:00:00Z&to=2026-11-06T12:00:00Z&expand=occurrences&timeZone=Pacific/Kiritimati");
        Assert.Equal("2026-11-07", (string?)Assert.Single(kiritimati)!["recurrenceId"]);
        Assert.Empty(await WindowAsync(_vic, "from=2026-11-06T10:00:00Z&to=2026-11-06T12:00:00Z&expand=occurrences&timeZone=Europe/Berlin"));
    }

    public static TheoryData<object, string, string> InvalidRecurrences => new()
    {
        { new { rrule = "FREQ=HOURLY" }, ErrorCodes.RecurrenceNotSupported, "recurrence.rrule" },
        { new { rrule = "FREQ=YEARLY;BYWEEKNO=20" }, ErrorCodes.RecurrenceNotSupported, "recurrence.rrule" },
        { new { rrule = "FREQ=DAILY;BYHOUR=9" }, ErrorCodes.RecurrenceNotSupported, "recurrence.rrule" },
        { new { rrule = "FREQ=WEEKLY;INTERVAL=0" }, ErrorCodes.RecurrenceInvalid, "recurrence.rrule" },
        { new { rrule = "FREQ=WEEKLY;COUNT=2;UNTIL=20261231" }, ErrorCodes.RecurrenceInvalid, "recurrence.rrule" },
        { new { rrule = "FREQ=WEEKLY;UNTIL=20261001" }, ErrorCodes.RecurrenceInvalid, "recurrence.rrule" },
        { new { rrule = (string?)null }, ErrorCodes.RecurrenceInvalid, "recurrence.rrule" },
        { new { rrule = "FREQ=WEEKLY", rdates = (string[])["2026-10-01T18:00:00"] }, ErrorCodes.RecurrenceInvalid, "recurrence.rdates" },
        { new { rrule = "FREQ=WEEKLY", exdates = (string[])["2026-11-09"] }, ErrorCodes.RecurrenceInvalid, "recurrence.exdates" },
        { new { rrule = "FREQ=WEEKLY", exdates = (string[])["2026-11-09T17:00:00Z"] }, ErrorCodes.RecurrenceInvalid, "recurrence.exdates" },
    };

    [Theory]
    [MemberData(nameof(InvalidRecurrences))]
    public async Task Unsupported_and_malformed_recurrence_is_refused_with_the_field(object recurrence, string code, string field)
    {
        using var response = await _mia.SendJsonAsync(HttpMethod.Post, "/api/v1/events", new
        {
            calendarId = _club,
            title = "x",
            start = new { dateTime = "2026-11-02T18:00:00" },
            end = new { dateTime = "2026-11-02T19:00:00" },
            recurrence,
        });

        var problem = await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, code);
        Assert.NotNull(problem["errors"]![field]);
    }

    [Fact]
    public async Task A_single_event_becomes_a_series_and_back()
    {
        var (created, _) = await _mia.CreateEventAsync(EventApi.Timed(_club));
        var id = (Guid)created["id"]!;

        using var toSeries = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{id}", new { recurrence = new { rrule = "FREQ=WEEKLY;COUNT=3" } }, "*");
        var series = await toSeries.JsonAsync();
        Assert.Equal("FREQ=WEEKLY;COUNT=3", (string?)series["recurrence"]!["rrule"]);
        Assert.Equal(1, (int)series["sequence"]!);
        await CancelAsync(_mia, id, "2026-11-09T17:00:00Z");

        // recurrence: null stops recurring: the first occurrence stays, the exception goes (reported).
        using var toSingle = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{id}", JsonNode.Parse("""{"recurrence":null}"""), "*");
        var single = await toSingle.JsonAsync();
        Assert.Null(single["recurrence"]);
        Assert.Equal(["2026-11-09T17:00:00Z"], single["droppedExceptions"]!.AsArray().Select(d => (string?)d));
        var stored = (await _host.StoredEventAsync(id))!;
        Assert.Null(stored.Rrule);
        Assert.Null(stored.SeriesUntilUtc);
        Assert.Equal(0, await _host.QueryAsync(db => Task.FromResult(db.EventExceptions.Count(x => x.EventId == id))));
        Assert.Equal("RRULE:FREQ=WEEKLY;COUNT=3", (string?)JsonNode.Parse((await _host.AuditEventsAsync("event", id))[^1].Before!)!["recurrence"]);

        // An absent recurrence leaves it unchanged.
        using var unchanged = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{id}", new { title = "Practice" }, "*");
        Assert.Null((await unchanged.JsonAsync())["recurrence"]);
    }

    [Fact]
    public async Task Busy_viewers_see_occurrence_times_only_and_no_transparent_occurrences()
    {
        var id = await _mia.CreateEventIdAsync(Weekly("FREQ=WEEKLY;COUNT=3", "2026-11-02T18:00:00", "2026-11-02T20:00:00", title: "Secret"));
        using (var free = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{id}/occurrences/2026-11-09T17:00:00Z", new { transparency = "transparent", title = "Off" }, "*"))
        {
            Assert.Equal(HttpStatusCode.OK, free.StatusCode);
        }

        var items = await WindowAsync(_eve, "from=2026-11-01T00:00:00Z&to=2026-12-01T00:00:00Z&expand=occurrences");

        Assert.Equal(["2026-11-02T17:00:00Z", "2026-11-16T17:00:00Z"], items.Select(i => (string?)i!["recurrenceId"]));
        Assert.All(items, i => Assert.Equal(
            ["allDay", "calendarId", "end", "id", "myLevel", "occurrenceId", "recurrence", "recurrenceId", "start", "title", "transparency"],
            i!.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal)));
        Assert.All(items, i => Assert.Null((string?)i!["title"]));

        // The master's exceptions in the busy projection: times, transparency and cancellation only.
        var (master, _) = await _eve.GetEventAsync(id);
        var exception = Assert.Single(master["exceptions"]!.AsArray())!;
        Assert.Equal(["recurrenceId", "transparency"], exception.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal));

        // Readers see the changed occurrence with its own title.
        var read = await WindowAsync(_vic, "from=2026-11-09T00:00:00Z&to=2026-11-10T00:00:00Z&expand=occurrences");
        Assert.Equal("Off", (string?)Assert.Single(read)!["title"]);
        Assert.True((bool)read[0]!["modified"]!);
    }

    [Fact]
    public async Task Busy_projection_of_a_master_lists_only_exceptions_that_change_busy_time()
    {
        var id = await _mia.CreateEventIdAsync(Weekly("FREQ=WEEKLY;COUNT=4", "2026-11-02T18:00:00", "2026-11-02T20:00:00", title: "Secret"));
        async Task PatchAsync(string recurrenceId, object body)
        {
            using var response = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{id}/occurrences/{recurrenceId}", body, "*");
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        }

        await PatchAsync("2026-11-09T17:00:00Z", new { title = "Renamed" }); // details only: not busy-relevant
        await PatchAsync("2026-11-16T17:00:00Z", new { transparency = "transparent", start = new { dateTime = "2026-11-17T07:00:00" }, end = new { dateTime = "2026-11-17T09:00:00" } });
        await PatchAsync("2026-11-23T17:00:00Z", new { start = new { dateTime = "2026-11-24T18:00:00" }, end = new { dateTime = "2026-11-24T20:00:00" } });

        var (master, _) = await _eve.GetEventAsync(id);

        var exceptions = master["exceptions"]!.AsArray();
        Assert.Equal(["2026-11-16T17:00:00Z", "2026-11-23T17:00:00Z"], exceptions.Select(x => (string?)x!["recurrenceId"]));
        Assert.Equal(["recurrenceId", "transparency"], exceptions[0]!.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal)); // free: no times
        Assert.Equal("2026-11-24T18:00:00", (string?)exceptions[1]!["start"]!["dateTime"]);
        var window = await WindowAsync(_eve, "from=2026-11-01T00:00:00Z&to=2026-12-01T00:00:00Z");
        Assert.Equal(2, Assert.Single(window)!["exceptions"]!.AsArray().Count);

        // Readers see every exception with its details.
        var (full, _) = await _vic.GetEventAsync(id);
        Assert.Equal(3, full["exceptions"]!.AsArray().Count);
        Assert.Equal("2026-11-17T07:00:00", (string?)full["exceptions"]![1]!["start"]!["dateTime"]);
    }

    [Fact]
    public async Task Zero_length_series_are_found_at_their_last_occurrence()
    {
        // Reminder-like series without duration: the series ends where its last occurrence starts.
        var once = await _mia.CreateEventIdAsync(Weekly("FREQ=WEEKLY;COUNT=1", "2026-11-02T18:00:00", "2026-11-02T18:00:00", title: "Once"));
        var twice = await _mia.CreateEventIdAsync(Weekly("FREQ=WEEKLY;COUNT=2", "2026-11-02T18:00:00", "2026-11-02T18:00:00", title: "Twice"));

        var first = await WindowAsync(_vic, "from=2026-11-02T17:00:00Z&to=2026-11-03T00:00:00Z&expand=occurrences");
        Assert.Equal([once, twice], first.Select(i => (Guid)i!["id"]!).Order());
        var last = await WindowAsync(_vic, "from=2026-11-09T17:00:00Z&to=2026-11-10T00:00:00Z");
        Assert.Equal(twice, (Guid)Assert.Single(last)!["id"]!);
    }

    [Fact]
    public async Task Series_shared_with_me_reach_me_beyond_their_first_occurrence()
    {
        var personal = await _mia.CreateCalendarAsync("Mia");
        var id = await _mia.CreateEventIdAsync(Weekly("FREQ=MONTHLY;BYDAY=1MO", "2026-01-05T18:00:00", "2026-01-05T19:00:00", calendarId: personal));
        await _host.AddMemberAsync(_lions, _eveId, GroupRole.Viewer); // Mia may name Eve: they share Lions
        await _mia.SetOverridesAsync(id, EventApi.User(_eveId, "read"));

        var items = await WindowAsync(_eve, "from=2027-03-01T00:00:00Z&to=2027-04-01T00:00:00Z&expand=occurrences");

        var item = Assert.Single(items, i => (Guid)i!["id"]! == id)!;
        Assert.Equal("2027-03-01T17:00:00Z", (string?)item["recurrenceId"]);
        Assert.True((bool)item["sharedWithMe"]!);
        Assert.Equal("read", (string?)item["myLevel"]);
    }

    [Fact]
    public async Task The_window_rejects_unknown_expansions()
    {
        using var response = await _vic.GetAsync(new Uri("/api/v1/events?from=2026-11-01T00:00:00Z&to=2026-12-01T00:00:00Z&expand=everything", UriKind.Relative), Ct);

        Assert.NotNull((await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed))["errors"]!["expand"]);
    }

    [Fact]
    public async Task Daily_series_fill_a_13_month_window_within_the_caps()
    {
        await _mia.CreateEventIdAsync(Weekly("FREQ=DAILY", "2026-01-01T08:00:00", "2026-01-01T08:30:00"));
        await _mia.CreateEventIdAsync(Weekly("FREQ=DAILY", "2026-01-01T09:00:00", "2026-01-01T09:30:00"));

        using var response = await _vic.GetAsync(new Uri("/api/v1/events?from=2026-01-31T00:00:00Z&to=2027-02-28T00:00:00Z&expand=occurrences", UriKind.Relative), Ct);
        var window = await response.JsonAsync();

        Assert.False((bool)window["truncated"]!);
        Assert.Equal(2 * 393, window["items"]!.AsArray().Count);
        var starts = window["items"]!.AsArray().Select(i => (DateTimeOffset)i!["start"]!["utc"]!).ToList();
        Assert.Equal(starts.Order(), starts);
    }

    private object Weekly(string rrule, string start, string end, string title = "Training", Guid? calendarId = null) =>
        new
        {
            calendarId = calendarId ?? _club,
            title,
            start = new { dateTime = start, timeZone = "Europe/Berlin" },
            end = new { dateTime = end },
            recurrence = new { rrule },
        };

    private static async Task CancelAsync(HttpClient client, Guid id, string recurrenceId)
    {
        using var response = await client.SendJsonAsync(HttpMethod.Delete, $"/api/v1/events/{id}/occurrences/{recurrenceId}", null, "*");
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
    }

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
