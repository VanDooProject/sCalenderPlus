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
/// Cover titles (privacy level C): <c>free_busy</c> viewers see an event's <c>coverTitle</c> instead of "Busy" and
/// never its title, description or location; <c>read</c> and above see both. Setup: group "Lions" (Olga owner, Mia
/// member, Vic viewer = read) owning the calendar "Club", Eve outside with a free_busy grant.
/// </summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class CoverTitleTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string NovemberOccurrences = "from=2026-11-01T00:00:00Z&to=2026-12-01T00:00:00Z&expand=occurrences";

    private readonly List<HttpClient> _clients = [];
    private ApiTestHost _host = null!;
    private HttpClient _mia = null!;
    private HttpClient _vic = null!;
    private HttpClient _eve = null!;
    private Guid _lions;
    private Guid _club;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await ApiTestHost.StartAsync(postgres);
        var (_, olga) = await PersonAsync("olga");
        _lions = await olga.CreateGroupAsync();
        (_, _mia) = await MemberAsync("mia", GroupRole.Member);
        (_, _vic) = await MemberAsync("vic", GroupRole.Viewer);
        var (eveId, eve) = await PersonAsync("eve");
        _eve = eve;
        _club = await olga.CreateCalendarAsync("Club", _lions);
        await _host.GrantAsync(_club, eveId, CalendarLevel.FreeBusy);
    }

    public async ValueTask DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Free_busy_viewers_see_the_cover_title_and_never_the_title()
    {
        var (created, _) = await _mia.CreateEventAsync(new
        {
            calendarId = _club,
            title = "Therapy",
            coverTitle = "  Appointment ",
            description = "Room 4",
            location = "Dr. Smith",
            start = new { dateTime = "2026-11-02T18:00:00", timeZone = "Europe/Berlin" },
            end = new { dateTime = "2026-11-02T19:00:00" },
        });
        var id = (Guid)created["id"]!;
        Assert.Equal("Appointment", (string?)created["coverTitle"]);
        var plain = await _mia.CreateEventIdAsync(EventApi.Timed(_club, title: "Board meeting", start: "2026-11-03T18:00:00", end: "2026-11-03T19:00:00"));

        // free_busy: the busy projection plus the cover title; the real title stays null.
        var (busy, _) = await _eve.GetEventAsync(id);
        Assert.Equal(
            ["allDay", "calendarId", "coverTitle", "end", "id", "myLevel", "start", "title", "transparency"],
            busy.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal));
        Assert.Null((string?)busy["title"]);
        Assert.Equal("Appointment", (string?)busy["coverTitle"]);
        Assert.Equal("free_busy", (string?)busy["myLevel"]);
        Assert.DoesNotContain("Therapy", busy.ToJsonString(), StringComparison.Ordinal);
        Assert.False((await _eve.GetEventAsync(plain)).Body.AsObject().ContainsKey("coverTitle")); // none set: "Busy"

        var window = await WindowAsync(_eve, "from=2026-11-01T00:00:00Z&to=2026-12-01T00:00:00Z");
        Assert.Equal(["Appointment", null], window.Select(e => (string?)e!["coverTitle"]));
        Assert.All(window, e => Assert.Null((string?)e!["title"]));
        Assert.DoesNotContain("Therapy", window.ToJsonString(), StringComparison.Ordinal);

        // read: the real title and the cover title (what outsiders see).
        var (read, _) = await _vic.GetEventAsync(id);
        Assert.Equal("read", (string?)read["myLevel"]);
        Assert.Equal("Therapy", (string?)read["title"]);
        Assert.Equal("Appointment", (string?)read["coverTitle"]);
        Assert.Equal("Room 4", (string?)read["description"]);

        var audit = Assert.Single(await _host.AuditEventsAsync("event", id));
        Assert.Equal("Appointment", (string?)JsonNode.Parse(audit.After!)!["coverTitle"]);
    }

    [Fact]
    public async Task Cover_titles_are_validated_like_titles()
    {
        using (var tooLong = await _mia.SendJsonAsync(HttpMethod.Post, "/api/v1/events", WithCover(new string('x', 201))))
        {
            var problem = await ProblemResponse.AssertProblemAsync(tooLong, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
            Assert.NotNull(problem["errors"]!["coverTitle"]);
        }

        var (longest, _) = await _mia.CreateEventAsync(WithCover(new string('x', 200)));
        Assert.Equal(200, ((string?)longest["coverTitle"])!.Length);
        var (blank, _) = await _mia.CreateEventAsync(WithCover("   "));
        Assert.False(blank.AsObject().ContainsKey("coverTitle")); // trimmed to empty: none

        // 200 characters after trimming pass the use case, more do not (also on PATCH).
        using var patch = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{(Guid)blank["id"]!}", new { coverTitle = new string('y', 201) }, "*");
        var patchProblem = await ProblemResponse.AssertProblemAsync(patch, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.NotNull(patchProblem["errors"]!["coverTitle"]);
    }

    [Fact]
    public async Task Merge_patch_sets_keeps_and_null_removes_the_cover_title()
    {
        var id = await _mia.CreateEventIdAsync(EventApi.Timed(_club, title: "Therapy"));
        var (_, readETag) = await _vic.GetEventAsync(id);
        var (_, busyETag) = await _eve.GetEventAsync(id);

        var set = await PatchAsync(id, new { coverTitle = "Appointment" });
        Assert.Equal("Appointment", (string?)set["coverTitle"]);
        var (busy, busyETagAfter) = await _eve.GetEventAsync(id);
        Assert.Equal("Appointment", (string?)busy["coverTitle"]);
        Assert.NotEqual(busyETag, busyETagAfter); // busy viewers see the change
        Assert.NotEqual(readETag, (await _vic.GetEventAsync(id)).ETag);

        var kept = await PatchAsync(id, new { title = "Therapy session" });
        Assert.Equal("Appointment", (string?)kept["coverTitle"]); // absent: unchanged

        var removed = await PatchAsync(id, new { coverTitle = (string?)null });
        Assert.False(removed.AsObject().ContainsKey("coverTitle"));
        Assert.False((await _eve.GetEventAsync(id)).Body.AsObject().ContainsKey("coverTitle"));

        var audits = await _host.AuditEventsAsync("event", id);
        Assert.Equal(["event.created", "event.updated", "event.updated", "event.updated"], audits.Select(a => a.Action));
        Assert.Equal("Appointment", (string?)JsonNode.Parse(audits[3].Before!)!["coverTitle"]);
        Assert.Null((string?)JsonNode.Parse(audits[3].After!)!["coverTitle"]);
        Assert.Equal(4, (await _host.ChangesAsync(_club)).Count(c => c.EventId == id && c.Change == CalendarChangeKind.Upsert));
        Assert.Equal(0, (int)removed["sequence"]!); // not a significant (time/status) change
    }

    [Fact]
    public async Task Occurrences_inherit_override_or_drop_the_series_cover_title()
    {
        var (series, _) = await _mia.CreateEventAsync(new
        {
            calendarId = _club,
            title = "Therapy",
            coverTitle = "Sports",
            start = new { dateTime = "2026-11-02T18:00:00", timeZone = "Europe/Berlin" },
            end = new { dateTime = "2026-11-02T19:00:00" },
            recurrence = new { rrule = "FREQ=WEEKLY;COUNT=6" },
        });
        var id = (Guid)series["id"]!;

        var match = await PatchOccurrenceAsync(id, "2026-11-09T17:00:00Z", new { coverTitle = "Match" });
        Assert.Equal(("Therapy", "Match"), ((string?)match["title"], (string?)match["coverTitle"]));
        var none = await PatchOccurrenceAsync(id, "2026-11-16T17:00:00Z", new { coverTitle = (string?)null });
        Assert.False(none.AsObject().ContainsKey("coverTitle"));
        var renamed = await PatchOccurrenceAsync(id, "2026-11-23T17:00:00Z", new { title = "Group therapy" });
        Assert.Equal("Sports", (string?)renamed["coverTitle"]); // inherited
        var back = await PatchOccurrenceAsync(id, "2026-11-30T17:00:00Z", new { coverTitle = "Sports" });
        Assert.False((bool?)back["modified"] ?? false); // equal to the series' value: follows the series

        // free_busy, occurrences: cover titles with exceptions applied, never a title.
        var occurrences = await WindowAsync(_eve, NovemberOccurrences);
        Assert.Equal(["Sports", "Match", null, "Sports", "Sports"], occurrences.Select(o => (string?)o!["coverTitle"]));
        Assert.All(occurrences, o => Assert.Null((string?)o!["title"]));
        Assert.DoesNotContain("therapy", occurrences.ToJsonString(), StringComparison.OrdinalIgnoreCase);

        // free_busy, master: the exceptions that change what busy viewers see (not the renamed one).
        var (busyMaster, _) = await _eve.GetEventAsync(id);
        Assert.Equal("Sports", (string?)busyMaster["coverTitle"]);
        AssertJson(
            """[{ "recurrenceId": "2026-11-09T17:00:00Z", "coverTitle": "Match" }, { "recurrenceId": "2026-11-16T17:00:00Z", "coverTitle": "" }]""",
            busyMaster["exceptions"]);

        // read: titles and cover titles of the exceptions.
        var (readMaster, _) = await _vic.GetEventAsync(id);
        AssertJson(
            """
            [{ "recurrenceId": "2026-11-09T17:00:00Z", "coverTitle": "Match" }, { "recurrenceId": "2026-11-16T17:00:00Z", "coverTitle": "" },
             { "recurrenceId": "2026-11-23T17:00:00Z", "title": "Group therapy" }]
            """,
            readMaster["exceptions"]);

        // "This and following" carries the cover title (and the moved exceptions' own ones) into the new series.
        using var split = await _mia.SendJsonAsync(HttpMethod.Post, $"/api/v1/events/{id}/split", new { recurrenceId = "2026-11-16T17:00:00Z" }, "*");
        Assert.Equal(HttpStatusCode.Created, split.StatusCode);
        var following = await split.JsonAsync();
        Assert.Equal("Sports", (string?)following["coverTitle"]);
        var after = await WindowAsync(_eve, NovemberOccurrences);
        Assert.Equal(["Sports", "Match", null, "Sports", "Sports"], after.Select(o => (string?)o!["coverTitle"]));

        // Cancelling clears the occurrence's own values.
        using var cancel = await _mia.SendJsonAsync(HttpMethod.Delete, Occurrence(id, "2026-11-09T17:00:00Z"), null, "*");
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        AssertJson("""[{ "recurrenceId": "2026-11-09T17:00:00Z", "cancelled": true }]""", (await _eve.GetEventAsync(id)).Body["exceptions"]);
    }

    private object WithCover(string coverTitle) =>
        new
        {
            calendarId = _club,
            title = "Therapy",
            coverTitle,
            start = new { dateTime = "2026-11-02T18:00:00", timeZone = "Europe/Berlin" },
            end = new { dateTime = "2026-11-02T19:00:00" },
        };

    private async Task<JsonNode> PatchAsync(Guid id, object body)
    {
        using var response = await _mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{id}", body, "*");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.JsonAsync();
    }

    private async Task<JsonNode> PatchOccurrenceAsync(Guid id, string recurrenceId, object body)
    {
        using var response = await _mia.SendJsonAsync(HttpMethod.Patch, Occurrence(id, recurrenceId), body, "*");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.JsonAsync();
    }

    private static string Occurrence(Guid id, string recurrenceId) => $"/api/v1/events/{id}/occurrences/{recurrenceId}";

    private static void AssertJson(string expected, JsonNode? actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), actual), $"Expected {expected}\nActual   {actual?.ToJsonString()}");

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
