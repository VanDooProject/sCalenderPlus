using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Application.Events;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Calendars;
using SCalenderPlus.IntegrationTests.Groups;
using SCalenderPlus.IntegrationTests.Infrastructure;
using SCalenderPlus.IntegrationTests.Problems;

namespace SCalenderPlus.IntegrationTests.Events;

/// <summary>
/// Issue #45: <c>GET /events?from&amp;to</c> with permission filtering. Setup follows permissions.md §5: calendar
/// "FC Lions – Club" of group Lions (Olga owner, Adam admin, Mia member, Vic viewer), Eve outside. Overrides do not
/// exist before M2-D, so the tests plug the worked examples' overrides in through <see cref="IEventOverrideSource"/>
/// (the seam the <c>event_overrides</c> table will fill) and mark the events <c>has_overrides</c>.
/// </summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class EventWindowTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string November = "from=2026-11-01T00:00:00Z&to=2026-12-01T00:00:00Z";

    private readonly List<HttpClient> _clients = [];
    private readonly FakeOverrides _overrides = new();
    private ApiTestHost _host = null!;
    private HttpClient _olga = null!;
    private HttpClient _adam = null!;
    private HttpClient _mia = null!;
    private HttpClient _vic = null!;
    private HttpClient _eve = null!;
    private Guid _lions;
    private Guid _eveId;
    private Guid _club;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await ApiTestHost.StartAsync(postgres, services: s => s.AddSingleton<IEventOverrideSource>(_overrides));
        (_, _olga) = await PersonAsync("olga");
        _lions = await _olga.CreateGroupAsync();
        (_, _adam) = await MemberAsync("adam", GroupRole.Admin);
        (_, _mia) = await MemberAsync("mia", GroupRole.Member);
        (_, _vic) = await MemberAsync("vic", GroupRole.Viewer);
        (_eveId, _eve) = await PersonAsync("eve");
        _club = await _olga.CreateCalendarAsync("FC Lions – Club", _lions);
    }

    public async ValueTask DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Example_B_users_see_the_expected_fields()
    {
        // Example B: "Board meeting" by Adam, overrides everyone → free_busy, group:Lions[admin] → read.
        var board = await _adam.CreateEventIdAsync(new
        {
            calendarId = _club,
            title = "Board meeting",
            description = "Budget",
            location = "Clubhouse",
            categories = (string[])["board"],
            start = new { dateTime = "2026-11-02T18:00:00" },
            end = new { dateTime = "2026-11-02T20:00:00" },
        });
        await WithOverridesAsync(board, new EventOverride(Principal.Everyone, EventLevel.FreeBusy), new EventOverride(Principal.Group(_lions, GroupRole.Admin), EventLevel.Read));
        var training = await _mia.CreateEventIdAsync(EventApi.Timed(_club, title: "Training", start: "2026-11-03T17:00:00", end: "2026-11-03T18:30:00"));

        foreach (var (client, expected) in new[] { (_olga, "manage"), (_adam, "manage") })
        {
            var item = Single(await WindowAsync(client, November), board);
            Assert.Equal(expected, (string?)item["myLevel"]);
            Assert.Equal("Board meeting", (string?)item["title"]);
            Assert.Equal("Clubhouse", (string?)item["location"]);
        }

        // Mia and Vic: restricted to free_busy below their calendar level — times only, nothing else.
        foreach (var client in new[] { _mia, _vic })
        {
            var items = await WindowAsync(client, November);
            var item = Single(items, board);
            Assert.Equal(
                ["allDay", "calendarId", "end", "id", "myLevel", "start", "title", "transparency"],
                item.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal));
            Assert.Equal("free_busy", (string?)item["myLevel"]);
            Assert.Null((string?)item["title"]);
            Assert.Equal(new DateTimeOffset(2026, 11, 2, 17, 0, 0, TimeSpan.Zero), (DateTimeOffset)item["start"]!["utc"]!);
            Assert.DoesNotContain("Budget", items.ToJsonString(), StringComparison.Ordinal);
            Assert.DoesNotContain("board", items.ToJsonString(), StringComparison.Ordinal);
            Assert.DoesNotContain("@scalenderplus", item.ToJsonString(), StringComparison.Ordinal);

            // The single-event endpoint shows the same projection.
            Assert.Equal(item.ToJsonString(), (await client.GetEventAsync(board)).Body.ToJsonString());
        }

        Assert.Equal("read", (string?)Single(await WindowAsync(_vic, November), training)["myLevel"]);
        Assert.Equal("manage", (string?)Single(await WindowAsync(_mia, November), training)["myLevel"]); // creator floor

        // Eve has no calendar level: `everyone` does not match her, she sees nothing.
        Assert.Empty(await WindowAsync(_eve, November));
    }

    [Fact]
    public async Task Events_shared_with_a_user_appear_as_shared_with_me()
    {
        // Example E: override user:Eve → read on an event of a calendar Eve cannot see.
        var shared = await _adam.CreateEventIdAsync(EventApi.Timed(_club, title: "Open day"));
        await _adam.CreateEventIdAsync(EventApi.Timed(_club, title: "Not shared"));
        await WithOverridesAsync(shared, new EventOverride(Principal.User(_eveId), EventLevel.Read));
        _overrides.Naming[_eveId] = [shared];

        var item = Assert.Single(await WindowAsync(_eve, November))!;

        Assert.Equal(shared, (Guid)item["id"]!);
        Assert.Equal("Open day", (string?)item["title"]);
        Assert.Equal("read", (string?)item["myLevel"]);
        Assert.True((bool)item["sharedWithMe"]!);
        Assert.Equal("read", (string?)(await _eve.GetEventAsync(shared)).Body["myLevel"]);
        Assert.Null(Single(await WindowAsync(_vic, November), shared)["sharedWithMe"]); // Vic sees the calendar
        Assert.Empty(await WindowAsync(_eve, "from=2026-12-01T00:00:00Z&to=2026-12-31T00:00:00Z")); // outside the window
    }

    public static TheoryData<string, string> ViewerDays => new()
    {
        // Where 2026-11-02 begins for a viewer in Honolulu (UTC−10) and on Fakaofo (UTC+13).
        { "Pacific/Honolulu", "2026-11-02T10:00:00Z" },
        { "Pacific/Fakaofo", "2026-11-01T11:00:00Z" },
    };

    [Theory]
    [MemberData(nameof(ViewerDays))]
    public async Task All_day_events_are_visible_to_viewers_in_extreme_zones(string zone, string dayStart)
    {
        var allDay = await _mia.CreateEventIdAsync(EventApi.AllDay(_club, start: "2026-11-02", end: "2026-11-03"));
        var day = DateTimeOffset.Parse(dayStart, System.Globalization.CultureInfo.InvariantCulture);
        var oneDay = TimeSpan.FromDays(1);

        // The viewer's whole day and its last hours show the event, with and without the viewer's zone.
        foreach (var (from, to) in new[] { (day, day + oneDay), (day + TimeSpan.FromHours(15), day + oneDay), (day, day + TimeSpan.FromHours(1)) })
        {
            Single(await WindowAsync(_vic, $"from={Q(from)}&to={Q(to)}&timeZone={zone}"), allDay);
            Single(await WindowAsync(_vic, $"from={Q(from)}&to={Q(to)}"), allDay);
        }

        // With the zone, the viewer's previous and next days do not.
        Assert.Empty(await WindowAsync(_vic, $"from={Q(day - oneDay)}&to={Q(day)}&timeZone={zone}"));
        Assert.Empty(await WindowAsync(_vic, $"from={Q(day + oneDay)}&to={Q(day + oneDay + oneDay)}&timeZone={zone}"));
    }

    [Fact]
    public async Task The_window_filters_by_calendar_level_transparency_deletion_and_calendar_ids()
    {
        var personal = await _vic.CreateCalendarAsync("Vic");
        var opaque = await _adam.CreateEventIdAsync(EventApi.Timed(_club, title: "Match", start: "2026-11-05T10:00:00", end: "2026-11-05T12:00:00"));
        var free = await _adam.CreateEventIdAsync(EventApi.Timed(_club, title: "Lunch", start: "2026-11-04T12:00:00", end: "2026-11-04T13:00:00", transparency: "transparent"));
        var deleted = await _adam.CreateEventIdAsync(EventApi.Timed(_club, title: "Cancelled"));
        var own = await _vic.CreateEventIdAsync(EventApi.Timed(personal, title: "Dentist", start: "2026-11-01T08:00:00", end: "2026-11-01T09:00:00"));
        var outside = await _adam.CreateEventIdAsync(EventApi.Timed(_club, title: "December", start: "2026-12-01T01:00:00", end: "2026-12-01T02:00:00")); // 00:00Z: at the window end
        using (var delete = await _adam.SendJsonAsync(HttpMethod.Delete, $"/api/v1/events/{deleted}", ifMatch: "*"))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        // Vic (read on the club, owner of "Vic"): everything live in the window, ordered by start, with etags.
        var items = await WindowAsync(_vic, November);
        Assert.Equal([own, free, opaque], items.Select(i => (Guid)i!["id"]!));
        foreach (var item in items)
        {
            Assert.Equal((await _vic.GetEventAsync((Guid)item!["id"]!)).ETag, (string?)item["etag"]);
        }

        Assert.Equal([own], (await WindowAsync(_vic, $"{November}&calendarIds={personal}")).Select(i => (Guid)i!["id"]!));
        Assert.Equal([own, free, opaque], (await WindowAsync(_vic, $"{November}&calendarIds={personal}&calendarIds={_club}&calendarIds={Guid.CreateVersion7()}")).Select(i => (Guid)i!["id"]!));
        Assert.Empty(await WindowAsync(_mia, $"{November}&calendarIds={personal}")); // not Mia's calendar: ignored, no leak

        // A free_busy grant: the transparent event is left out entirely, the others are busy blocks without etags.
        await _host.GrantAsync(_club, _eveId, CalendarLevel.FreeBusy);
        var busy = await WindowAsync(_eve, November);
        Assert.Equal([opaque], busy.Select(i => (Guid)i!["id"]!));
        Assert.Null(busy[0]!["etag"]);
        Assert.DoesNotContain(outside, (await WindowAsync(_adam, November)).Select(i => (Guid)i!["id"]!));
    }

    public static TheoryData<string, string, string> InvalidWindows => new()
    {
        { "to=2026-12-01T00:00:00Z", ErrorCodes.ValidationFailed, "from" },
        { "from=2026-12-01T00:00:00Z", ErrorCodes.ValidationFailed, "to" },
        { "from=2026-12-01T00:00:00Z&to=2026-12-01T00:00:00Z", ErrorCodes.ValidationFailed, "to" },
        { "from=2026-01-01T00:00:00Z&to=2027-02-01T00:00:01Z", ErrorCodes.ValidationFailed, "to" },
        { "from=2026-11-01T00:00:00Z&to=2026-12-01T00:00:00Z&timeZone=Mars/Olympus", ErrorCodes.TimeZoneInvalid, "timeZone" },
    };

    [Theory]
    [MemberData(nameof(InvalidWindows))]
    public async Task Invalid_windows_are_refused(string query, string code, string field)
    {
        using var response = await _vic.GetAsync(new Uri("/api/v1/events?" + query, UriKind.Relative), Ct);

        var problem = await ProblemResponse.AssertProblemAsync(response, code == ErrorCodes.ValidationFailed ? HttpStatusCode.BadRequest : HttpStatusCode.UnprocessableEntity, code);
        Assert.NotNull(problem["errors"]![field]);
    }

    [Fact]
    public async Task A_window_of_13_months_is_allowed()
    {
        using var response = await _vic.GetAsync(new Uri("/api/v1/events?from=2026-01-31T00:00:00Z&to=2027-02-28T00:00:00Z", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((bool)(await response.JsonAsync())["truncated"]!);
    }

    private static string Q(DateTimeOffset instant) => Uri.EscapeDataString(instant.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture));

    private static JsonNode Single(JsonArray items, Guid id) => Assert.Single(items, i => (Guid)i!["id"]! == id)!;

    private static async Task<JsonArray> WindowAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(new Uri("/api/v1/events?" + query, UriKind.Relative), Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.JsonAsync())["items"]!.AsArray();
    }

    private async Task WithOverridesAsync(Guid eventId, params EventOverride[] overrides)
    {
        _overrides.Overrides[eventId] = overrides;
        await _host.ExecuteSqlAsync($"UPDATE events SET has_overrides = true WHERE id = {eventId}");
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

    /// <summary>Stands in for the <c>event_overrides</c> table of M2-D.</summary>
    private sealed class FakeOverrides : IEventOverrideSource
    {
        public ConcurrentDictionary<Guid, IReadOnlyList<EventOverride>> Overrides { get; } = new();

        public ConcurrentDictionary<Guid, IReadOnlyList<Guid>> Naming { get; } = new();

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<EventOverride>>> ForEventsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<EventOverride>>>(
                eventIds.Where(Overrides.ContainsKey).ToDictionary(id => id, id => Overrides[id]));

        public Task<IReadOnlyList<Guid>> EventsNamingAsync(PrincipalContext principal, CancellationToken cancellationToken = default) =>
            Task.FromResult(principal.UserId is { } user && Naming.TryGetValue(user, out var ids) ? ids : (IReadOnlyList<Guid>)[]);
    }
}
