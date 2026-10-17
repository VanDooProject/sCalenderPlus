using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Calendars;
using SCalenderPlus.IntegrationTests.Events;
using SCalenderPlus.IntegrationTests.Groups;
using SCalenderPlus.IntegrationTests.Infrastructure;
using SCalenderPlus.IntegrationTests.Problems;

namespace SCalenderPlus.IntegrationTests.Feeds;

/// <summary>
/// Anonymous share links (permissions.md §3) and their feeds <c>/ical/v1/s/{token}.ics</c>: the link level is a
/// ceiling, <c>anonymous</c>/<c>everyone</c> overrides only restrict. Setup: group "Lions" (Olga owner, Mia member)
/// owning the calendar "Club".
/// </summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class ShareLinkTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly List<HttpClient> _clients = [];
    private ApiTestHost _host = null!;
    private HttpClient _olga = null!;
    private HttpClient _mia = null!;
    private Guid _lions;
    private Guid _club;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await ApiTestHost.StartAsync(postgres);
        (_, _olga) = await PersonAsync("olga");
        _lions = await _olga.CreateGroupAsync();
        var (miaId, mia) = await PersonAsync("mia");
        _mia = mia;
        await _host.AddMemberAsync(_lions, miaId, GroupRole.Member);
        _club = await _olga.CreateCalendarAsync("Club", _lions);
    }

    public async ValueTask DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Managers_create_list_and_revoke_links_and_the_secret_is_shown_once()
    {
        var aclBefore = await _host.CalendarAclVersionAsync(_club);
        var issued = await CreateLinkAsync("read", label: "Website");

        var secret = (string)issued["token"]!;
        Assert.Equal($"https://app.example.test/ical/v1/s/{secret}.ics", (string)issued["url"]!);
        Assert.StartsWith("webcal://", (string)issued["webcalUrl"]!, StringComparison.Ordinal);
        Assert.Equal("read", (string)issued["shareLink"]!["level"]!);
        Assert.True(await _host.CalendarAclVersionAsync(_club) > aclBefore);
        var id = (Guid)issued["shareLink"]!["id"]!;
        var stored = await _host.QueryAsync(db => db.ShareLinks.AsNoTracking().SingleAsync(l => l.Id == id, Ct));
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(secret)), stored.TokenHash);

        var list = await _olga.GetStringAsync(new Uri($"/api/v1/calendars/{_club}/share-links", UriKind.Relative), Ct);
        Assert.Contains("Website", list, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, list, StringComparison.Ordinal);
        var audit = await _host.AuditEventsAsync("calendar", _club);
        Assert.Contains(audit, a => a.Action == "calendar.share_link.created");
        Assert.DoesNotContain(audit, a => (a.After ?? string.Empty).Contains(secret, StringComparison.Ordinal));

        // A member (contribute) may neither list nor create links nor revoke them.
        using (var listed = await _mia.GetAsync(new Uri($"/api/v1/calendars/{_club}/share-links", UriKind.Relative), Ct))
        {
            await ProblemResponse.AssertProblemAsync(listed, HttpStatusCode.Forbidden, "insufficient_permission");
        }

        using (var revokedByMember = await _mia.DeleteAsync(new Uri($"/api/v1/share-links/{id}", UriKind.Relative), Ct))
        {
            await ProblemResponse.AssertProblemAsync(revokedByMember, HttpStatusCode.Forbidden, "insufficient_permission");
        }

        Assert.Equal(HttpStatusCode.OK, (await _host.GetFeedAsync(FeedApi.PathOf(issued))).StatusCode);
        using (var revoke = await _olga.DeleteAsync(new Uri($"/api/v1/share-links/{id}", UriKind.Relative), Ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        }

        using var gone = await _host.GetFeedAsync(FeedApi.PathOf(issued));
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        using var again = await _olga.DeleteAsync(new Uri($"/api/v1/share-links/{id}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode); // idempotent
        Assert.DoesNotContain(id.ToString(), await _olga.GetStringAsync(new Uri($"/api/v1/calendars/{_club}/share-links", UriKind.Relative), Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Links_carry_free_busy_or_read_and_need_a_verified_email()
    {
        foreach (var level in new[] { "edit", "contribute", "none", "manage" })
        {
            using var response = await _olga.SendJsonAsync(HttpMethod.Post, $"/api/v1/calendars/{_club}/share-links", new { level });
            await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        }

        var email = ApiTestHost.UniqueEmail("ulla");
        await _host.CreateUserAsync(email, emailConfirmed: false);
        var unverified = await _host.SignedInClientAsync(email);
        _clients.Add(unverified);
        var own = await unverified.CreateCalendarAsync("Mine");
        using var refused = await unverified.SendJsonAsync(HttpMethod.Post, $"/api/v1/calendars/{own}/share-links", new { level = "read" });
        await ProblemResponse.AssertProblemAsync(refused, HttpStatusCode.Forbidden, "email_not_verified");
    }

    [Fact]
    public async Task Anonymous_none_hides_an_event_and_everyone_read_cannot_lift_a_free_busy_link()
    {
        var normal = await _mia.CreateEventIdAsync(EventApi.Timed(_club, title: "Training", start: FeedApi.Soon(2), end: FeedApi.Soon(2, 20)));
        var hidden = await _olga.CreateEventIdAsync(EventApi.Timed(_club, title: "Board meeting", start: FeedApi.Soon(3), end: FeedApi.Soon(3, 20)));
        await _olga.SetOverridesAsync(hidden, new { principal = new { type = "anonymous" }, level = "none" });
        var widened = await _mia.CreateEventIdAsync(EventApi.Timed(_club, title: "Open day", start: FeedApi.Soon(4), end: FeedApi.Soon(4, 20)));
        await _mia.SetOverridesAsync(widened, EventApi.Everyone("read"));
        var busyLink = await CreateLinkAsync("free_busy");
        var readLink = await CreateLinkAsync("read");

        var busy = await _host.FeedTextAsync(FeedApi.PathOf(busyLink));
        Assert.Equal(2, FeedApi.Events(busy).Count); // training and open day, both busy
        Assert.Equal(2, FeedApi.Events(busy, "Busy").Count);
        foreach (var secret in new[] { "Training", "Open day", "Board meeting", hidden.ToString(), "X-SCALENDERPLUS", "app.example.test" })
        {
            Assert.DoesNotContain(secret, busy, StringComparison.Ordinal);
        }

        Assert.Contains($"UID:{widened}@scalenderplus", busy, StringComparison.Ordinal); // opaque UID for busy events

        var read = await _host.FeedTextAsync(FeedApi.PathOf(readLink));
        Assert.Single(FeedApi.Events(read, "Training")); // no access label, no app link: holders have no account
        Assert.Single(FeedApi.Events(read, "Open day"));
        Assert.DoesNotContain("Board meeting", read, StringComparison.Ordinal);
        Assert.DoesNotContain("🔒", read, StringComparison.Ordinal);
        Assert.DoesNotContain("X-SCALENDERPLUS", read, StringComparison.Ordinal);
        Assert.Contains("X-WR-CALNAME:Club", read, StringComparison.Ordinal);
        Assert.Contains($"UID:{normal}@scalenderplus", read, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expired_links_and_links_of_deleted_calendars_are_404()
    {
        var expired = await CreateLinkAsync("read", expiresInDays: 1);
        var id = (Guid)expired["shareLink"]!["id"]!;
        await _host.QueryAsync(db => db.ShareLinks.Where(l => l.Id == id).ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiresAt, SystemClock.Instance.GetCurrentInstant() - Duration.FromMinutes(1)), Ct));
        using var response = await _host.GetFeedAsync(FeedApi.PathOf(expired));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var personal = await _olga.CreateCalendarAsync("Short-lived");
        using var created = await _olga.SendJsonAsync(HttpMethod.Post, $"/api/v1/calendars/{personal}/share-links", new { level = "read" });
        var link = await created.JsonAsync();
        using var delete = await _olga.SendJsonAsync(HttpMethod.Delete, $"/api/v1/calendars/{personal}", ifMatch: "*");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        using var deleted = await _host.GetFeedAsync(FeedApi.PathOf(link));
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
    }

    private async Task<JsonNode> CreateLinkAsync(string level, string? label = null, int? expiresInDays = null)
    {
        using var response = await _olga.SendJsonAsync(HttpMethod.Post, $"/api/v1/calendars/{_club}/share-links", new { level, label, expiresInDays });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.JsonAsync();
    }

    private async Task<(Guid Id, HttpClient Client)> PersonAsync(string name)
    {
        var email = ApiTestHost.UniqueEmail(name);
        var id = await _host.CreateUserAsync(email, displayName: name);
        var client = await _host.SignedInClientAsync(email);
        _clients.Add(client);
        return (id, client);
    }
}
