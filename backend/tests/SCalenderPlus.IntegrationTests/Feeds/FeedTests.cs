using System.IO.Compression;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;
using SCalenderPlus.Infrastructure.Persistence;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Calendars;
using SCalenderPlus.IntegrationTests.Events;
using SCalenderPlus.IntegrationTests.Groups;
using SCalenderPlus.IntegrationTests.Infrastructure;

namespace SCalenderPlus.IntegrationTests.Feeds;

/// <summary>
/// Personal feeds end to end: the permission engine per event (permissions.md §7), the feed scopes, ETag/304 without
/// an event query and the in-memory cache (ical-caldav.md §1.4). Setup: group "Lions" (Olga owner, Mia member, Vic
/// viewer) owning the calendar "Club"; Eve outside with a free_busy grant on it.
/// </summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class FeedTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly List<HttpClient> _clients = [];
    private readonly CommandRecorder _commands = new();
    private ApiTestHost _host = null!;
    private HttpClient _olga = null!;
    private HttpClient _mia = null!;
    private HttpClient _vic = null!;
    private HttpClient _eve = null!;
    private Guid _vicId;
    private Guid _eveId;
    private Guid _lions;
    private Guid _club;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await ApiTestHost.StartAsync(postgres, services: s => s.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(_commands)));
        (_, _olga) = await PersonAsync("olga");
        _lions = await _olga.CreateGroupAsync();
        (_, _mia) = await MemberAsync("mia", GroupRole.Member);
        (_vicId, _vic) = await MemberAsync("vic", GroupRole.Viewer);
        (_eveId, _eve) = await PersonAsync("eve");
        _club = await _olga.CreateCalendarAsync("Club", _lions);
        await _host.GrantAsync(_club, _eveId, CalendarLevel.FreeBusy);

        // Olga and Eve share a group (Eve becomes selectable for Olga's event shares) that owns no calendar.
        var friends = await _olga.CreateGroupAsync("Friends");
        await _host.AddMemberAsync(friends, _eveId, GroupRole.Viewer);
    }

    public async ValueTask DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Every_viewer_gets_the_projection_of_their_level()
    {
        var training = await _mia.CreateEventIdAsync(EventApi.Timed(_club, title: "Training", start: FeedApi.Soon(2), end: FeedApi.Soon(2, 20)));
        var (therapy, _) = await _mia.CreateEventAsync(new
        {
            calendarId = _club,
            title = "Therapy",
            coverTitle = "Appointment",
            description = "Room 4",
            location = "Dr. Smith",
            start = new { dateTime = FeedApi.Soon(3, 10), timeZone = "Europe/Berlin" },
            end = new { dateTime = FeedApi.Soon(3, 11) },
        });
        var board = await _olga.CreateEventIdAsync(EventApi.Timed(_club, title: "Board meeting", start: FeedApi.Soon(4), end: FeedApi.Soon(4, 20)));
        await _olga.SetOverridesAsync(board, EventApi.Everyone("free_busy"));
        var hidden = await _olga.CreateEventIdAsync(EventApi.Timed(_club, title: "Secret", start: FeedApi.Soon(5), end: FeedApi.Soon(5, 20)));
        await _olga.SetOverridesAsync(hidden, EventApi.User(_vicId, "none"));
        var free = await _olga.CreateEventIdAsync(EventApi.Timed(_club, title: "Optional run", start: FeedApi.Soon(6), end: FeedApi.Soon(6, 20), transparency: "transparent"));

        var vic = await _host.FeedTextAsync(FeedApi.PathOf(await _vic.CreateFeedTokenAsync()));
        var mia = await _host.FeedTextAsync(FeedApi.PathOf(await _mia.CreateFeedTokenAsync("calendar", _club, "prefix_emoji_both")));
        var eve = await _host.FeedTextAsync(FeedApi.PathOf(await _eve.CreateFeedTokenAsync()));

        // Vic (viewer: read): read-only label; the board meeting busy (everyone → free_busy); the secret absent.
        Assert.Single(FeedApi.Events(vic, "🔒 Training"));
        Assert.Contains("X-SCALENDERPLUS-ACCESS:read", FeedApi.Events(vic, "🔒 Training")[0], StringComparison.Ordinal);
        Assert.Single(FeedApi.Events(vic, "Busy"));
        Assert.DoesNotContain("Board meeting", vic, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret", vic, StringComparison.Ordinal);
        Assert.DoesNotContain(hidden.ToString(), vic, StringComparison.Ordinal);
        Assert.Single(FeedApi.Events(vic, "🔒 Optional run"));

        // Mia (member, creator of training and therapy: manage): editable label, the stored UIDs of the calendar feed.
        var miaTraining = Assert.Single(FeedApi.Events(mia, "✏️ Training"));
        Assert.Contains($"UID:{training}@scalenderplus", miaTraining, StringComparison.Ordinal);
        Assert.Contains("X-SCALENDERPLUS-ACCESS:manage", miaTraining, StringComparison.Ordinal);
        Assert.Contains($"https://app.example.test/e/{training}/edit", miaTraining, StringComparison.Ordinal);
        Assert.Single(FeedApi.Events(mia, "Busy")); // board meeting: everyone → free_busy restricts contributors too

        // Eve (free_busy): busy blocks only — the cover title, never title, description or location; no free time.
        Assert.Single(FeedApi.Events(eve, "Appointment"));
        foreach (var secret in new[] { "Therapy", "Room 4", "Dr. Smith", "Training", "Board meeting", "Optional run", "X-SCALENDERPLUS", "app.example.test" })
        {
            Assert.DoesNotContain(secret, eve, StringComparison.Ordinal);
        }

        Assert.All(FeedApi.Events(eve), e => Assert.Contains("CLASS:CONFIDENTIAL", e, StringComparison.Ordinal));
        Assert.Equal(4, FeedApi.Events(eve).Count); // training, therapy, board, secret (Eve is not Vic); the free run left out
        Assert.Contains($"UID:{(Guid)therapy["id"]!}@scalenderplus", eve, StringComparison.Ordinal);
        Assert.DoesNotContain(free.ToString(), eve, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Feed_scopes_cover_calendars_hidden_calendars_and_shared_with_me()
    {
        var personal = await _olga.CreateCalendarAsync("Olga private");
        var shared = await _olga.CreateEventIdAsync(EventApi.Timed(personal, title: "Dinner with Eve", start: FeedApi.Soon(2), end: FeedApi.Soon(2, 21)));
        await _olga.SetOverridesAsync(shared, EventApi.User(_eveId, "read"));
        await _olga.CreateEventIdAsync(EventApi.Timed(_club, title: "Club evening", start: FeedApi.Soon(3), end: FeedApi.Soon(3, 21)));
        var evesOwn = await _eve.CreateCalendarAsync("Eve's");
        await _eve.CreateEventIdAsync(EventApi.Timed(evesOwn, title: "Dentist", start: FeedApi.Soon(4, 9), end: FeedApi.Soon(4, 10)));

        var aggregate = await _eve.CreateFeedTokenAsync();
        var sharedOnly = await _eve.CreateFeedTokenAsync("shared_with_me");
        var calendarOnly = await _eve.CreateFeedTokenAsync("calendar", evesOwn);

        var all = await _host.FeedTextAsync(FeedApi.PathOf(aggregate));
        Assert.Single(FeedApi.Events(all, "🔒 Dinner with Eve"));
        Assert.Single(FeedApi.Events(all, "Dentist"));
        Assert.Single(FeedApi.Events(all, "Busy")); // the club evening (free_busy grant)
        Assert.Contains("X-WR-CALNAME:sCalenderPlus", all, StringComparison.Ordinal);

        var onlyShared = await _host.FeedTextAsync(FeedApi.PathOf(sharedOnly));
        Assert.Single(FeedApi.Events(onlyShared));
        Assert.Single(FeedApi.Events(onlyShared, "🔒 Dinner with Eve"));
        Assert.Contains("X-WR-CALNAME:Shared with me", onlyShared, StringComparison.Ordinal);

        var own = await _host.FeedTextAsync(FeedApi.PathOf(calendarOnly));
        Assert.Single(FeedApi.Events(own));
        Assert.Contains("X-WR-CALNAME:Eve's", own, StringComparison.Ordinal);

        // Hiding the club calendar removes it from the aggregate feed (and changes its ETag).
        using var hide = await _eve.SendJsonAsync(HttpMethod.Put, $"/api/v1/calendars/{_club}/prefs", new { hidden = true }, "*");
        Assert.Equal(HttpStatusCode.OK, hide.StatusCode);
        var afterHide = await _host.FeedTextAsync(FeedApi.PathOf(aggregate));
        Assert.Empty(FeedApi.Events(afterHide, "Busy"));
        Assert.Single(FeedApi.Events(afterHide, "🔒 Dinner with Eve"));
    }

    [Fact]
    public async Task An_unchanged_feed_answers_304_without_an_event_query()
    {
        await _olga.CreateEventIdAsync(EventApi.Timed(_club, title: "Training", start: FeedApi.Soon(2), end: FeedApi.Soon(2, 20)));
        var path = FeedApi.PathOf(await _vic.CreateFeedTokenAsync());
        using var first = await _host.GetFeedAsync(path);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var etag = first.Headers.ETag!.ToString();
        Assert.StartsWith("W/\"", etag, StringComparison.Ordinal);
        Assert.True(first.Headers.CacheControl is { Private: true, MaxAge.TotalSeconds: 300 });
        Assert.Equal("utf-8", first.Content.Headers.ContentType!.CharSet);
        Assert.NotNull(first.Content.Headers.LastModified);

        _commands.Commands.Clear();
        _commands.Recording = true;
        using var second = await _host.GetFeedAsync(path, ifNoneMatch: etag);
        _commands.Recording = false;

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Equal(etag, second.Headers.ETag!.ToString());
        Assert.NotEmpty(_commands.Commands); // the token check and the ETag inputs are always read …
        Assert.Empty(_commands.EventQueries); // … but no event rows

        // If-Modified-Since works too (while the render is cached), and a stale ETag gets the feed.
        using var since = await _host.GetFeedAsync(path, ifModifiedSince: first.Content.Headers.LastModified);
        Assert.Equal(HttpStatusCode.NotModified, since.StatusCode);
        using var stale = await _host.GetFeedAsync(path, ifNoneMatch: "W/\"stale\"");
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
    }

    [Fact]
    public async Task Changes_of_events_and_of_overrides_on_shared_events_change_the_etag()
    {
        var personal = await _olga.CreateCalendarAsync("Olga private");
        var shared = await _olga.CreateEventIdAsync(EventApi.Timed(personal, title: "Dinner", start: FeedApi.Soon(2), end: FeedApi.Soon(2, 21)));
        await _olga.SetOverridesAsync(shared, EventApi.User(_eveId, "read"));
        var path = FeedApi.PathOf(await _eve.CreateFeedTokenAsync("shared_with_me"));
        var etag = await ETagAsync(path);
        Assert.Equal(etag, await ETagAsync(path)); // stable while nothing changes

        // The override changes (read → free_busy) on an event in a calendar Eve cannot see.
        await _olga.SetOverridesAsync(shared, EventApi.User(_eveId, "free_busy"));
        var afterOverride = await ETagAsync(path);
        Assert.NotEqual(etag, afterOverride);
        var feed = await _host.FeedTextAsync(path);
        Assert.Single(FeedApi.Events(feed, "Busy"));
        Assert.DoesNotContain("Dinner", feed, StringComparison.Ordinal);

        // The event itself changes.
        using var patch = await _olga.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{shared}", new { start = new { dateTime = FeedApi.Soon(2, 19), timeZone = "Europe/Berlin" } }, "*");
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.NotEqual(afterOverride, await ETagAsync(path));

        // The share is removed: the event leaves the feed.
        await _olga.SetOverridesAsync(shared);
        Assert.Empty(FeedApi.Events(await _host.FeedTextAsync(path)));
    }

    [Fact]
    public async Task An_edit_of_hidden_fields_leaves_a_busy_viewer_s_etag_and_last_modified_unchanged()
    {
        var training = await _olga.CreateEventIdAsync(EventApi.Timed(_club, title: "Training", start: FeedApi.Soon(2), end: FeedApi.Soon(2, 20)));
        var eve = FeedApi.PathOf(await _eve.CreateFeedTokenAsync("calendar", _club)); // free_busy
        var vic = FeedApi.PathOf(await _vic.CreateFeedTokenAsync("calendar", _club)); // read
        using var eveBefore = await _host.GetFeedAsync(eve);
        var eveETag = eveBefore.Headers.ETag!.ToString();
        var eveLastModified = eveBefore.Content.Headers.LastModified;
        var vicETag = await ETagAsync(vic);

        // Title, description and location change: the internal state changes (the feed is rendered again) …
        using var patch = await _olga.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{training}", new { title = "Training (secret)", description = "Gate code 1234", location = "Pitch 3" }, "*");
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        _commands.Commands.Clear();
        _commands.Recording = true;
        using var conditional = await _host.GetFeedAsync(eve, ifNoneMatch: eveETag);
        _commands.Recording = false;

        // … but the busy body is the same: same ETag, 304, same Last-Modified.
        Assert.Equal(HttpStatusCode.NotModified, conditional.StatusCode);
        Assert.NotEmpty(_commands.EventQueries); // rendered again, and still 304: the ETag is the body's
        Assert.Equal(eveETag, conditional.Headers.ETag!.ToString());
        using var eveAfter = await _host.GetFeedAsync(eve);
        Assert.Equal(eveETag, eveAfter.Headers.ETag!.ToString());
        Assert.Equal(eveLastModified, eveAfter.Content.Headers.LastModified);
        using var since = await _host.GetFeedAsync(eve, ifModifiedSince: eveLastModified);
        Assert.Equal(HttpStatusCode.NotModified, since.StatusCode);

        // Readers see the edit.
        Assert.NotEqual(vicETag, await ETagAsync(vic));
        Assert.Single(FeedApi.Events(await _host.FeedTextAsync(vic), "🔒 Training (secret)"));
    }

    [Fact]
    public async Task A_stored_url_with_line_breaks_cannot_inject_properties()
    {
        const string Injected = "https://x.example/a\r\nATTENDEE:mailto:eve@evil.example\r\nEND:VEVENT\r\nBEGIN:VEVENT\nX-EVIL:1\u0085X-NEL:1";
        var training = await _olga.CreateEventIdAsync(EventApi.Timed(_club, title: "Training", start: FeedApi.Soon(2), end: FeedApi.Soon(2, 20)));

        // The api refuses it …
        using var refused = await _olga.SendJsonAsync(HttpMethod.Patch, $"/api/v1/events/{training}", new { url = Injected }, "*");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        // … and a row stored before validation (or by an import) still renders as one URL line.
        await _host.QueryAsync(db => db.Events.Where(e => e.Id == training).ExecuteUpdateAsync(
            s => s.SetProperty(e => e.Url, Injected).SetProperty(e => e.Location, "Pitch\r\nORGANIZER:mailto:eve@evil.example"),
            Ct));
        using var response = await _host.GetFeedAsync(FeedApi.PathOf(await _vic.CreateFeedTokenAsync("calendar", _club)));
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lines = body.Split("\r\n");
        Assert.DoesNotContain(lines, l => l.StartsWith("ATTENDEE", StringComparison.Ordinal) || l.StartsWith("ORGANIZER", StringComparison.Ordinal) || l.StartsWith("X-EVIL", StringComparison.Ordinal));
        Assert.Equal(1, lines.Count(l => l == "BEGIN:VEVENT"));
        Assert.Equal(1, lines.Count(l => l == "END:VEVENT"));
        Assert.DoesNotContain('\n', body.Replace("\r\n", string.Empty, StringComparison.Ordinal));
        Assert.DoesNotContain('\u0085', body);
        Assert.Contains("URL:https://x.example/aATTENDEE:mailto:eve@evil.exampleEND:VEVENTBEGIN:VEVENTX-EVIL:1X-NEL:1\r\n", body.Replace("\r\n ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_calendars_of_shared_events_are_part_of_the_feed_state()
    {
        var personal = await _olga.CreateCalendarAsync("Olga private");
        var shared = await _olga.CreateEventIdAsync(EventApi.Timed(personal, title: "Dinner", start: FeedApi.Soon(2), end: FeedApi.Soon(2, 21)));
        await _olga.SetOverridesAsync(shared, EventApi.User(_eveId, "read"));
        var path = FeedApi.PathOf(await _eve.CreateFeedTokenAsync("shared_with_me"));
        var etag = await ETagAsync(path);

        // A setting of the calendar Eve cannot see changes the state: the feed is evaluated again (not served from
        // the cache). The body, and so the ETag, stays the same while Eve's levels do — rule 6 (creators manage
        // their own events) needs contribute on the calendar, which a stranger to it never has.
        using var setting = await _olga.SendJsonAsync(HttpMethod.Patch, $"/api/v1/calendars/{personal}", new { creatorsManageOwnEvents = false }, ifMatch: "*");
        Assert.Equal(HttpStatusCode.OK, setting.StatusCode);
        _commands.Commands.Clear();
        _commands.Recording = true;
        using var after = await _host.GetFeedAsync(path, ifNoneMatch: etag);
        _commands.Recording = false;
        Assert.Equal(HttpStatusCode.NotModified, after.StatusCode);
        Assert.NotEmpty(_commands.EventQueries);

        // Unchanged again: the cached render answers without an event query.
        _commands.Commands.Clear();
        _commands.Recording = true;
        using var unchanged = await _host.GetFeedAsync(path, ifNoneMatch: etag);
        _commands.Recording = false;
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
        Assert.Empty(_commands.EventQueries);
    }

    [Fact]
    public async Task Membership_changes_reach_the_feed_at_the_next_poll()
    {
        await _olga.CreateEventIdAsync(EventApi.Timed(_club, title: "Training", start: FeedApi.Soon(2), end: FeedApi.Soon(2, 20)));
        var path = FeedApi.PathOf(await _vic.CreateFeedTokenAsync());
        var etag = await ETagAsync(path);
        Assert.Single(FeedApi.Events(await _host.FeedTextAsync(path), "🔒 Training"));

        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri($"/api/v1/groups/{_lions}/members/{_vicId}", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("If-Match", "*");
        using var removed = await _olga.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        using var after = await _host.GetFeedAsync(path, ifNoneMatch: etag);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Empty(FeedApi.Events(await after.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task Feeds_are_compressed_when_the_client_accepts_it()
    {
        await _olga.CreateEventIdAsync(EventApi.Timed(_club, title: "Training", start: FeedApi.Soon(2), end: FeedApi.Soon(2, 20)));
        var path = FeedApi.PathOf(await _vic.CreateFeedTokenAsync());

        using var response = await _host.GetFeedAsync(path, acceptEncoding: "gzip");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("gzip", response.Content.Headers.ContentEncoding);
        await using var gzip = new GZipStream(await response.Content.ReadAsStreamAsync(Ct), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        Assert.Contains("BEGIN:VCALENDAR", await reader.ReadToEndAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Feeds_use_the_owner_s_locale_and_zone()
    {
        await _olga.CreateEventIdAsync(EventApi.Timed(_club, title: "Training", start: FeedApi.Soon(2), end: FeedApi.Soon(2, 20)));
        await _host.QueryAsync(db => db.Users.Where(u => u.Id == _eveId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Locale, "de").SetProperty(u => u.TimeZone, "America/New_York"), Ct));
        var path = FeedApi.PathOf(await _eve.CreateFeedTokenAsync("calendar", _club));

        var feed = await _host.FeedTextAsync(path);

        Assert.Single(FeedApi.Events(feed, "Beschäftigt"));
        Assert.Contains("X-WR-TIMEZONE:America/New_York", feed, StringComparison.Ordinal);
        Assert.Contains("BEGIN:VTIMEZONE\r\nTZID:Europe/Berlin", feed, StringComparison.Ordinal); // the event's own zone
    }

    private async Task<string> ETagAsync(string path)
    {
        using var response = await _host.GetFeedAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response.Headers.ETag!.ToString();
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
