using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Events;
using SCalenderPlus.IntegrationTests.Groups;
using SCalenderPlus.IntegrationTests.Infrastructure;
using SCalenderPlus.IntegrationTests.Problems;

namespace SCalenderPlus.IntegrationTests.Calendars;

/// <summary>
/// One-time ICS upload (ical-caldav.md §2, api.md "ICS upload"). Setup: group "Lions" (Olga owner, Mia member =
/// contribute, Vic viewer = read) owning the calendar "Club" (Europe/Berlin); Eve is in no group.
/// </summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class IcsUploadTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly List<HttpClient> _clients = [];
    private ApiTestHost _host = null!;
    private HttpClient _olga = null!;
    private HttpClient _mia = null!;
    private HttpClient _vic = null!;
    private HttpClient _eve = null!;
    private Guid _miaId;
    private Guid _club;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await ApiTestHost.StartAsync(postgres);
        (_, _olga) = await PersonAsync("olga");
        var lions = await _olga.CreateGroupAsync();
        (_miaId, _mia) = await PersonAsync("mia");
        await _host.AddMemberAsync(lions, _miaId, GroupRole.Member);
        var (vicId, vic) = await PersonAsync("vic");
        _vic = vic;
        await _host.AddMemberAsync(lions, vicId, GroupRole.Viewer);
        (_, _eve) = await PersonAsync("eve");
        _club = await _olga.CreateCalendarAsync("Club", lions);
    }

    public async ValueTask DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Google_export_previews_imports_with_exceptions_and_a_reupload_adds_nothing()
    {
        var file = Fixture("google-export.ics");

        var preview = await UploadOkAsync(_mia, file, dryRun: true);
        Assert.True((bool)preview["dryRun"]!);
        Assert.Equal(
            """{"total":9,"created":8,"duplicates":0,"skipped":1,"series":3,"modifiedOccurrences":3,"cancelledOccurrences":5}""",
            preview["counts"]!.ToJsonString());
        Assert.Contains(preview["warnings"]!.AsArray(), w => (string)w!["code"]! == "private_event");
        var training = preview["items"]!.AsArray().Single(i => (string)i!["uid"]! == "5q3n8f0v2k1m7c9d4h6j8l0p2r@google.com")!;
        Assert.Equal(("create", "FREQ=WEEKLY;UNTIL=20261221T225959Z;BYDAY=MO", "2026-09-07T18:00:00"), ((string)training["action"]!, (string)training["rrule"]!, (string)training["start"]!["dateTime"]!));
        Assert.Equal(0, await EventCountAsync()); // a dry run stores nothing

        var committed = await UploadOkAsync(_mia, file, dryRun: false);
        Assert.False((bool)committed["dryRun"]!);
        Assert.Equal(8, (int)committed["counts"]!["created"]!);
        Assert.Equal(8, await EventCountAsync());
        var changes = await _host.QueryAsync(db => db.Set<CalendarChange>().AsNoTracking().CountAsync(c => c.CalendarId == _club && c.Change == CalendarChangeKind.Upsert, Ct));
        Assert.Equal(8, changes);
        var audit = Assert.Single(await _host.AuditEventsAsync("calendar", _club), a => a.Action == "calendar.ics_imported");
        Assert.Equal(8, (int)JsonNode.Parse(audit.After!)!["created"]!);

        // The series as viewers see it: occurrences across DST, the moved one, without EXDATEs and the cancelled one.
        var occurrences = await OccurrencesAsync(_vic, "U12 training");
        Assert.Equal(13, occurrences.Count);
        Assert.Contains(occurrences, o => (string)o["start"]!["dateTime"]! == "2026-09-21T19:00:00" && (string)o["location"]! == "Sporthalle Mitte");
        Assert.Contains(occurrences, o => (string)o["title"]! == "U12 training with parents");
        Assert.DoesNotContain(occurrences, o => ((string)o["start"]!["dateTime"]!).StartsWith("2026-11-02", StringComparison.Ordinal));
        var stored = await _host.QueryAsync(db => db.Set<Event>().AsNoTracking().SingleAsync(e => e.Uid == "5q3n8f0v2k1m7c9d4h6j8l0p2r@google.com", Ct));
        Assert.Equal(_miaId, stored.CreatorUserId); // the uploader creates
        Assert.False(stored.HasOverrides); // no permission overrides

        // Re-upload: everything is a duplicate, nothing is added.
        var again = await UploadOkAsync(_olga, file, dryRun: false);
        Assert.Equal((0, 8, 1), ((int)again["counts"]!["created"]!, (int)again["counts"]!["duplicates"]!, (int)again["counts"]!["skipped"]!));
        Assert.All(again["items"]!.AsArray().Where(i => (string)i!["action"]! == "duplicate"), i => Assert.Equal("exists", (string)i!["reason"]!));
        Assert.Equal(8, await EventCountAsync());
    }

    [Fact]
    public async Task A_deleted_event_does_not_count_as_a_duplicate()
    {
        var file = Fixture("outlook-export.ics");
        await UploadOkAsync(_olga, file, dryRun: false);
        await _host.ExecuteSqlAsync($"UPDATE events SET deleted_at = now() WHERE uid = 'seattle-call@example.com'");

        var again = await UploadOkAsync(_olga, file, dryRun: true);

        Assert.Equal((1, 4), ((int)again["counts"]!["created"]!, (int)again["counts"]!["duplicates"]!));
        Assert.Equal("Europe/Berlin", (string)again["items"]![0]!["start"]!["timeZone"]!); // Windows zone name mapped
    }

    [Fact]
    public async Task Needs_contribute_and_a_visible_calendar()
    {
        var file = Fixture("outlook-export.ics");

        using (var reader = await UploadAsync(_vic, file, dryRun: true))
        {
            await ProblemResponse.AssertProblemAsync(reader, HttpStatusCode.Forbidden, "insufficient_permission");
        }

        using (var stranger = await UploadAsync(_eve, file, dryRun: true))
        {
            await ProblemResponse.AssertProblemAsync(stranger, HttpStatusCode.NotFound, "not_found");
        }

        Assert.Equal(5, (int)(await UploadOkAsync(_mia, file, dryRun: true))["counts"]!["created"]!);
    }

    [Fact]
    public async Task Needs_a_verified_email_and_an_unfrozen_calendar()
    {
        var email = ApiTestHost.UniqueEmail("una");
        await _host.CreateUserAsync(email, emailConfirmed: false);
        var una = await _host.SignedInClientAsync(email);
        _clients.Add(una);
        var own = await una.CreateCalendarAsync("Mine");
        using (var unverified = await UploadAsync(una, Fixture("outlook-export.ics"), dryRun: true, calendarId: own))
        {
            await ProblemResponse.AssertProblemAsync(unverified, HttpStatusCode.Forbidden, "email_not_verified");
        }

        await _host.ExecuteSqlAsync($"UPDATE calendars SET frozen_at = now() WHERE id = {_club}");
        using var frozen = await UploadAsync(_olga, Fixture("outlook-export.ics"), dryRun: false);
        await ProblemResponse.AssertProblemAsync(frozen, HttpStatusCode.Conflict, "calendar_frozen");
    }

    [Fact]
    public async Task Refuses_large_invalid_and_overfull_files_and_other_media_types()
    {
        using (var large = await UploadAsync(_olga, "BEGIN:VCALENDAR\r\n" + new string('x', 5 * 1024 * 1024) + "\r\nEND:VCALENDAR\r\n", dryRun: true))
        {
            await ProblemResponse.AssertProblemAsync(large, HttpStatusCode.RequestEntityTooLarge, "payload_too_large");
        }

        using (var invalid = await UploadAsync(_olga, "<html>not a calendar</html>", dryRun: true))
        {
            await ProblemResponse.AssertProblemAsync(invalid, HttpStatusCode.UnprocessableEntity, "ics_invalid");
        }

        var overfull = new StringBuilder("BEGIN:VCALENDAR\r\n");
        for (var i = 0; i < 5001; i++)
        {
            overfull.Append("BEGIN:VEVENT\r\nUID:e").Append(i).Append("\r\nDTSTART;VALUE=DATE:20261101\r\nEND:VEVENT\r\n");
        }

        using (var tooMany = await UploadAsync(_olga, overfull.Append("END:VCALENDAR\r\n").ToString(), dryRun: true))
        {
            var problem = await ProblemResponse.AssertProblemAsync(tooMany, HttpStatusCode.UnprocessableEntity, "ics_too_many_events");
            Assert.Equal(5000, (int)problem["limit"]!["max"]!);
        }

        using var json = await _olga.SendJsonAsync(HttpMethod.Post, $"/api/v1/calendars/{_club}/ics-upload", new { file = "x" });
        await ProblemResponse.AssertProblemAsync(json, HttpStatusCode.UnsupportedMediaType, "unsupported_media_type");
        Assert.Equal(0, await EventCountAsync());
    }

    [Fact]
    public async Task Five_thousand_events_import_in_one_upload()
    {
        var file = new StringBuilder("BEGIN:VCALENDAR\r\nX-WR-TIMEZONE:Europe/Berlin\r\n");
        for (var i = 0; i < 5000; i++)
        {
            file.Append("BEGIN:VEVENT\r\nUID:bulk-").Append(i).Append("@example.test\r\nSUMMARY:Slot ").Append(i)
                .Append("\r\nDTSTART:20270101T090000\r\nDURATION:PT30M\r\nEND:VEVENT\r\n");
        }

        var result = await UploadOkAsync(_olga, file.Append("END:VCALENDAR\r\n").ToString(), dryRun: false);

        Assert.Equal(5000, (int)result["counts"]!["created"]!);
        Assert.Equal(5000, await EventCountAsync());
    }

    private Task<int> EventCountAsync() =>
        _host.QueryAsync(db => db.Set<Event>().AsNoTracking().CountAsync(e => e.CalendarId == _club && e.DeletedAt == null, Ct));

    private async Task<List<JsonNode>> OccurrencesAsync(HttpClient client, string series)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/events?from=2026-09-01T00:00:00Z&to=2027-01-31T00:00:00Z&calendarIds={_club}&expand=occurrences", UriKind.Relative), Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return [.. (await response.JsonAsync())["items"]!.AsArray().Select(i => i!).Where(i => ((string?)i["title"])?.StartsWith(series, StringComparison.Ordinal) == true && i["recurrenceId"] is not null)];
    }

    private async Task<JsonNode> UploadOkAsync(HttpClient client, string content, bool dryRun)
    {
        using var response = await UploadAsync(client, content, dryRun);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.JsonAsync();
    }

    private Task<HttpResponseMessage> UploadAsync(HttpClient client, string content, bool dryRun, Guid? calendarId = null)
    {
        var body = new StringContent(content, Encoding.UTF8);
        body.Headers.ContentType = new MediaTypeHeaderValue("text/calendar") { CharSet = "utf-8" };
        return client.PostAsync(new Uri($"/api/v1/calendars/{calendarId ?? _club}/ics-upload?dryRun={(dryRun ? "true" : "false")}", UriKind.Relative), body, Ct);
    }

    /// <summary>The export fixtures of the application tests (one copy, CRLF).</summary>
    private static string Fixture(string name, [CallerFilePath] string source = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "SCalenderPlus.Application.Tests", "Import", "Fixtures", name), Encoding.UTF8);

    private async Task<(Guid Id, HttpClient Client)> PersonAsync(string name)
    {
        var email = ApiTestHost.UniqueEmail(name);
        var id = await _host.CreateUserAsync(email, displayName: name);
        var client = await _host.SignedInClientAsync(email);
        _clients.Add(client);
        return (id, client);
    }
}
