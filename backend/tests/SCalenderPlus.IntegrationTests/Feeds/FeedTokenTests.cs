using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SCalenderPlus.Infrastructure.Identity;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Calendars;
using SCalenderPlus.IntegrationTests.Events;
using SCalenderPlus.IntegrationTests.Groups;
using SCalenderPlus.IntegrationTests.Infrastructure;
using SCalenderPlus.IntegrationTests.Problems;

namespace SCalenderPlus.IntegrationTests.Feeds;

/// <summary>
/// Feed links (<c>/api/v1/feed-tokens</c>): the secret is shown once and stored hashed; rotation and revocation take
/// effect at once — also over a cached render — and a password reset can rotate them all.
/// </summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class FeedTokenTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly List<HttpClient> _clients = [];
    private ApiTestHost _host = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _host = await ApiTestHost.StartAsync(postgres);

    public async ValueTask DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task The_secret_is_shown_once_and_stored_only_as_its_hash()
    {
        var (userId, mia) = await PersonAsync("mia");

        var issued = await mia.CreateFeedTokenAsync();

        var secret = (string)issued["token"]!;
        Assert.Equal(43, secret.Length);
        Assert.Equal($"https://app.example.test/ical/v1/{secret}.ics", (string)issued["url"]!);
        Assert.Equal($"webcal://app.example.test/ical/v1/{secret}.ics", (string)issued["webcalUrl"]!);
        var id = (Guid)issued["feedToken"]!["id"]!;
        Assert.Equal("aggregate", (string)issued["feedToken"]!["scope"]!);
        Assert.Equal("prefix_emoji", (string)issued["feedToken"]!["labelMode"]!);
        Assert.Equal(90, (int)issued["feedToken"]!["includePastDays"]!);

        // Stored: the SHA-256 only. Lists, reads and the audit trail never contain the secret.
        var stored = await _host.QueryAsync(db => db.FeedTokens.AsNoTracking().SingleAsync(t => t.Id == id, Ct));
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(secret)), stored.TokenHash);
        Assert.Equal(userId, stored.UserId);
        var list = await mia.GetStringAsync(new Uri("/api/v1/feed-tokens", UriKind.Relative), Ct);
        var one = await mia.GetStringAsync(new Uri($"/api/v1/feed-tokens/{id}", UriKind.Relative), Ct);
        var audit = await _host.AuditEventsAsync("feed_token", id);
        Assert.Contains(id.ToString(), list, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, list + one + string.Join(' ', audit.Select(a => a.After)), StringComparison.Ordinal);
        Assert.Equal("feed_token.created", Assert.Single(audit).Action);

        Assert.Contains("BEGIN:VCALENDAR", await _host.FeedTextAsync(FeedApi.PathOf(issued)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoked_and_rotated_tokens_answer_404_even_when_a_render_is_cached()
    {
        var (_, mia) = await PersonAsync("mia");
        var calendar = await mia.CreateCalendarAsync();
        await mia.CreateEventIdAsync(EventApi.Timed(calendar, title: "Training", start: FeedApi.Soon(3), end: FeedApi.Soon(3, 20)));
        var revoked = await mia.CreateFeedTokenAsync("calendar", calendar);
        var rotated = await mia.CreateFeedTokenAsync("calendar", calendar);
        Assert.Contains("Training", await _host.FeedTextAsync(FeedApi.PathOf(revoked)), StringComparison.Ordinal); // rendered and cached
        Assert.Contains("Training", await _host.FeedTextAsync(FeedApi.PathOf(rotated)), StringComparison.Ordinal);

        using (var delete = await mia.DeleteAsync(new Uri($"/api/v1/feed-tokens/{revoked["feedToken"]!["id"]}", UriKind.Relative), Ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        using var rotate = await mia.SendJsonAsync(HttpMethod.Post, $"/api/v1/feed-tokens/{rotated["feedToken"]!["id"]}/rotate");
        Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);
        var renewed = await rotate.JsonAsync();

        using var gone = await _host.GetFeedAsync(FeedApi.PathOf(revoked));
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal("text/plain", gone.Content.Headers.ContentType?.MediaType); // no problem details echoing the path
        using var old = await _host.GetFeedAsync(FeedApi.PathOf(rotated));
        Assert.Equal(HttpStatusCode.NotFound, old.StatusCode);
        Assert.Contains("Training", await _host.FeedTextAsync(FeedApi.PathOf(renewed)), StringComparison.Ordinal);
        Assert.NotNull(renewed["feedToken"]!["rotatedAt"]);

        // The revoked one is gone from the list and its id answers 404.
        var list = await mia.GetStringAsync(new Uri("/api/v1/feed-tokens", UriKind.Relative), Ct);
        Assert.DoesNotContain(revoked["feedToken"]!["id"]!.ToString(), list, StringComparison.Ordinal);
        using var again = await mia.DeleteAsync(new Uri($"/api/v1/feed-tokens/{revoked["feedToken"]!["id"]}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Unknown_and_malformed_tokens_are_404()
    {
        foreach (var path in new[] { "/ical/v1/" + new string('a', 43) + ".ics", "/ical/v1/short.ics", "/ical/v1/s/" + new string('b', 43) + ".ics" })
        {
            using var response = await _host.GetFeedAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task Rotate_all_replaces_every_secret()
    {
        var (_, mia) = await PersonAsync("mia");
        var first = await mia.CreateFeedTokenAsync();
        var second = await mia.CreateFeedTokenAsync("shared_with_me");

        using var response = await mia.SendJsonAsync(HttpMethod.Post, "/api/v1/feed-tokens/rotate-all");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.JsonAsync())["items"]!.AsArray();
        Assert.Equal(2, items.Count);
        foreach (var old in new[] { first, second })
        {
            using var dead = await _host.GetFeedAsync(FeedApi.PathOf(old));
            Assert.Equal(HttpStatusCode.NotFound, dead.StatusCode);
        }

        foreach (var issued in items)
        {
            Assert.Equal(HttpStatusCode.OK, (await _host.GetFeedAsync(FeedApi.PathOf(issued!))).StatusCode);
        }
    }

    [Fact]
    public async Task Settings_change_with_if_match()
    {
        var (_, mia) = await PersonAsync("mia");
        var issued = await mia.CreateFeedTokenAsync();
        var id = (Guid)issued["feedToken"]!["id"]!;

        using (var missing = await mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/feed-tokens/{id}", new { labelMode = "suffix_text" }))
        {
            Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);
        }

        using var patch = await mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/feed-tokens/{id}", new { labelMode = "suffix_text", name = " iPhone ", includePastDays = 7 }, (string)issued["feedToken"]!["etag"]!);
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var body = await patch.JsonAsync();
        Assert.Equal("suffix_text", (string)body["labelMode"]!);
        Assert.Equal("iPhone", (string)body["name"]!);
        Assert.Equal(7, (int)body["includePastDays"]!);

        using var stale = await mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/feed-tokens/{id}", new { name = "" }, (string)issued["feedToken"]!["etag"]!);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        using var invalid = await mia.SendJsonAsync(HttpMethod.Patch, $"/api/v1/feed-tokens/{id}", new { labelMode = "loud" }, "*");
        await ProblemResponse.AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Calendar_feeds_need_a_calendar_the_caller_sees_and_tokens_are_private()
    {
        var (_, olga) = await PersonAsync("olga");
        var (_, eve) = await PersonAsync("eve");
        var calendar = await olga.CreateCalendarAsync();

        using (var foreign = await eve.SendJsonAsync(HttpMethod.Post, "/api/v1/feed-tokens", new { scope = "calendar", calendarId = calendar }))
        {
            await ProblemResponse.AssertProblemAsync(foreign, HttpStatusCode.NotFound, "not_found");
        }

        using (var noCalendar = await eve.SendJsonAsync(HttpMethod.Post, "/api/v1/feed-tokens", new { scope = "calendar" }))
        {
            await ProblemResponse.AssertProblemAsync(noCalendar, HttpStatusCode.BadRequest, "validation_failed");
        }

        using (var extra = await eve.SendJsonAsync(HttpMethod.Post, "/api/v1/feed-tokens", new { scope = "aggregate", calendarId = calendar }))
        {
            await ProblemResponse.AssertProblemAsync(extra, HttpStatusCode.BadRequest, "validation_failed");
        }

        var olgas = await olga.CreateFeedTokenAsync("calendar", calendar);
        var id = olgas["feedToken"]!["id"]!.ToString();
        using var read = await eve.GetAsync(new Uri($"/api/v1/feed-tokens/{id}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        using var revoke = await eve.DeleteAsync(new Uri($"/api/v1/feed-tokens/{id}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NotFound, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _host.GetFeedAsync(FeedApi.PathOf(olgas))).StatusCode);
    }

    [Fact]
    public async Task A_password_reset_can_rotate_all_feed_tokens()
    {
        var email = ApiTestHost.UniqueEmail("mia");
        var userId = await _host.CreateUserAsync(email);
        var mia = await _host.SignedInClientAsync(email);
        _clients.Add(mia);
        var kept = await mia.CreateFeedTokenAsync();
        var token = await ResetTokenAsync(email);

        // Without the flag the feeds keep working …
        using var anonymous = _host.CreateClient();
        using (var reset = await anonymous.SendJsonAsync(HttpMethod.Post, "/api/v1/auth/reset-password", new { userId, token, newPassword = "a brand new password" }))
        {
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await _host.GetFeedAsync(FeedApi.PathOf(kept))).StatusCode);

        // … with it every secret is replaced (not shown: the request has no session) and the links need a new URL.
        using (var reset = await anonymous.SendJsonAsync(HttpMethod.Post, "/api/v1/auth/reset-password", new { userId, token = await ResetTokenAsync(email), newPassword = "another new password", rotateFeedTokens = true }))
        {
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
            Assert.Empty(await reset.Content.ReadAsByteArrayAsync(Ct));
        }

        using var dead = await _host.GetFeedAsync(FeedApi.PathOf(kept));
        Assert.Equal(HttpStatusCode.NotFound, dead.StatusCode);
        var fresh = await _host.SignedInClientAsync(email, "another new password");
        _clients.Add(fresh);
        var list = JsonNode.Parse(await fresh.GetStringAsync(new Uri("/api/v1/feed-tokens", UriKind.Relative), Ct))!["items"]!.AsArray();
        Assert.True((bool)Assert.Single(list)!["needsNewLink"]!);
        var audit = await _host.AuditEventsAsync("feed_token", (Guid)kept["feedToken"]!["id"]!);
        Assert.Contains(audit, a => a.Action == "feed_token.rotated" && a.After!.Contains("password_reset", StringComparison.Ordinal));

        using var rotate = await fresh.SendJsonAsync(HttpMethod.Post, $"/api/v1/feed-tokens/{kept["feedToken"]!["id"]}/rotate");
        var renewed = await rotate.JsonAsync();
        Assert.False((bool)renewed["feedToken"]!["needsNewLink"]!);
        Assert.Equal(HttpStatusCode.OK, (await _host.GetFeedAsync(FeedApi.PathOf(renewed))).StatusCode);
    }

    [Fact]
    public async Task A_deleted_owner_s_feeds_answer_410()
    {
        var (userId, mia) = await PersonAsync("mia");
        var issued = await mia.CreateFeedTokenAsync();
        await _host.QueryAsync(db => db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAt, NodaTime.SystemClock.Instance.GetCurrentInstant()), Ct));

        using var response = await _host.GetFeedAsync(FeedApi.PathOf(issued));

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    private async Task<string> ResetTokenAsync(string email)
    {
        await using var scope = _host.Api.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        return Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GeneratePasswordResetTokenAsync(user)));
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
