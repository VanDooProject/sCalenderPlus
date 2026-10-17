using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using SCalenderPlus.Application.Accounts;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;
using SCalenderPlus.Infrastructure.Auditing;
using SCalenderPlus.Infrastructure.Jobs;
using SCalenderPlus.IntegrationTests.Calendars;
using SCalenderPlus.IntegrationTests.Events;
using SCalenderPlus.IntegrationTests.Feeds;
using SCalenderPlus.IntegrationTests.Groups;
using SCalenderPlus.IntegrationTests.Infrastructure;
using SCalenderPlus.IntegrationTests.Problems;

namespace SCalenderPlus.IntegrationTests.Auth;

/// <summary>
/// Account deletion (api.md "Me", permissions.md §4.6): <c>DELETE /me</c> with a 14-day grace period, blockers,
/// cancelling, and the purge job. Setup: group "Lions" (Olga owner, Mia member, Vic viewer) with the calendar "Club";
/// Olga's personal calendar "Olga's" (shared with Mia); Mia's personal calendar "Mine".
/// </summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class AccountDeletionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly List<HttpClient> _clients = [];
    private readonly MutableClock _clock = new();
    private ApiTestHost _host = null!;
    private HttpClient _olga = null!;
    private HttpClient _mia = null!;
    private Guid _olgaId;
    private Guid _miaId;
    private Guid _vicId;
    private string _miaEmail = null!;
    private Guid _lions;
    private Guid _club;
    private Guid _olgas;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await ApiTestHost.StartAsync(postgres, services: s => s.AddSingleton<IClock>(_clock));
        (_olgaId, _olga, _) = await PersonAsync("olga");
        _lions = await _olga.CreateGroupAsync();
        (_miaId, _mia, _miaEmail) = await PersonAsync("mia");
        await _host.AddMemberAsync(_lions, _miaId, GroupRole.Member);
        (_vicId, _, _) = await PersonAsync("vic");
        await _host.AddMemberAsync(_lions, _vicId, GroupRole.Viewer);
        _club = await _olga.CreateCalendarAsync("Club", _lions);
        _olgas = await _olga.CreateCalendarAsync("Olga's");
        await _host.GrantAsync(_olgas, _miaId, CalendarLevel.Read);
    }

    public async ValueTask DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Sole_owners_of_groups_with_members_and_owners_of_shared_calendars_are_blocked()
    {
        var state = await GetJsonAsync(_olga, "/api/v1/me/deletion");
        Assert.Null(state["scheduled"]);
        Assert.Equal(14, (int)state["graceDays"]!);
        var blockers = state["blockers"]!.AsArray();
        Assert.Contains(blockers, b => (string)b!["type"]! == "group" && (Guid)b["id"]! == _lions && (string)b["reason"]! == "sole_owner" && (int)b["count"]! == 2);
        Assert.Contains(blockers, b => (string)b!["type"]! == "calendar" && (Guid)b["id"]! == _olgas && (string)b["reason"]! == "shared" && (string)b["name"]! == "Olga's");

        using var refused = await DeleteMeAsync(_olga, new { password = ApiTestHost.Password });
        var problem = await ProblemResponse.AssertProblemAsync(refused, HttpStatusCode.Conflict, "account_deletion_blocked");
        Assert.Equal(2, problem["blockers"]!.AsArray().Count);
        Assert.Null(await DeletedAtAsync(_olgaId));
    }

    [Fact]
    public async Task Needs_the_password_or_a_code()
    {
        using (var wrong = await DeleteMeAsync(_mia, new { password = "wrong password!" }))
        {
            await ProblemResponse.AssertProblemAsync(wrong, HttpStatusCode.Forbidden, "reauthentication_failed");
        }

        using (var none = await DeleteMeAsync(_mia, new { }))
        {
            await ProblemResponse.AssertProblemAsync(none, HttpStatusCode.BadRequest, "validation_failed");
        }

        Assert.Null(await DeletedAtAsync(_miaId));
    }

    [Fact]
    public async Task Requesting_marks_the_account_feeds_answer_410_and_cancelling_restores_them()
    {
        var feed = FeedApi.PathOf(await _mia.CreateFeedTokenAsync());
        Assert.Equal(HttpStatusCode.OK, (await _host.GetFeedAsync(feed)).StatusCode);

        var scheduled = await RequestDeletionAsync(_mia);
        var purgeAt = (DateTimeOffset)scheduled["scheduled"]!["purgeAt"]!;
        Assert.Equal(TimeSpan.FromDays(14), purgeAt - (DateTimeOffset)scheduled["scheduled"]!["requestedAt"]!);
        Assert.Equal(purgeAt, (DateTimeOffset)(await GetJsonAsync(_mia, "/api/v1/me"))["deletionScheduledFor"]!); // this session stays
        using (var gone = await _host.GetFeedAsync(feed))
        {
            Assert.Equal(HttpStatusCode.Gone, gone.StatusCode);
        }

        var email = Assert.Single(await _host.EmailsToAsync(_miaEmail));
        Assert.Equal("Your account will be deleted", email.Subject);
        Assert.Contains("https://app.example.test/settings/security", email.TextBody, StringComparison.Ordinal);
        var job = await _host.QueryAsync(db => db.Set<Job>().AsNoTracking().SingleAsync(j => j.Type == "account.purge", Ct));
        Assert.Equal(purgeAt, job.RunAt.ToDateTimeOffset());
        Assert.Contains(await _host.AuditEventsAsync(_miaId), a => a.Action == "user.deletion_requested");

        // Signing in still works (to cancel); a second request changes nothing.
        using (var again = await _host.SignedInClientAsync(_miaEmail))
        {
            Assert.Equal(purgeAt, (DateTimeOffset)(await RequestDeletionAsync(again))["scheduled"]!["purgeAt"]!);
        }

        using (var cancel = await _mia.PostAsync(new Uri("/api/v1/me/deletion/cancel", UriKind.Relative), null, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
            Assert.Null((await cancel.JsonAsync())["scheduled"]);
        }

        Assert.Null(await DeletedAtAsync(_miaId));
        Assert.Equal(HttpStatusCode.OK, (await _host.GetFeedAsync(feed)).StatusCode);
        Assert.Contains(await _host.AuditEventsAsync(_miaId), a => a.Action == "user.deletion_cancelled");

        // The pending job finds nothing to do, even after the grace period.
        _clock.Advance(Duration.FromDays(15));
        Assert.False(await PurgeAsync(_miaId, Instant.FromDateTimeOffset((DateTimeOffset)scheduled["scheduled"]!["requestedAt"]!)));
        Assert.NotNull(await _host.QueryAsync(db => db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == _miaId, Ct)));
    }

    [Fact]
    public async Task The_purge_removes_personal_data_and_access_and_keeps_shared_calendars()
    {
        // Mia: a personal calendar with an event, a group she is alone in (with a calendar), a group she will be the
        // last owner of, a group she bills with Olga as co-owner, an event in the club, overrides naming her, a feed.
        var mine = await _mia.CreateCalendarAsync("Mine");
        await _mia.CreateEventIdAsync(EventApi.Timed(mine, "Dentist"));
        var solo = await _mia.CreateGroupAsync("Solo");
        var soloCalendar = await _mia.CreateCalendarAsync("Solo cal", solo);
        var later = await _mia.CreateGroupAsync("Later");
        var pair = await _mia.CreateGroupAsync("Pair");
        await _host.AddMemberAsync(pair, _olgaId, GroupRole.Owner);
        var clubEvent = await _mia.CreateEventIdAsync(EventApi.Timed(_club, "Training by Mia"));
        var shared = await _olga.CreateEventIdAsync(EventApi.Timed(_club, "Board"));
        await _olga.SetOverridesAsync(shared, EventApi.User(_miaId, "none"), EventApi.User(_vicId, "read"));
        using (var invite = await _olga.SendJsonAsync(HttpMethod.Post, $"/api/v1/groups/{pair}/invites", new { email = _miaEmail, role = "member" }))
        {
            Assert.True(invite.StatusCode == HttpStatusCode.Created, await invite.Content.ReadAsStringAsync(Ct)); // her address in Olga's audit trail
        }

        var feedId = (Guid)(await _mia.CreateFeedTokenAsync())["feedToken"]!["id"]!;
        var feed = FeedApi.PathOf(await _mia.CreateFeedTokenAsync());

        var scheduled = await RequestDeletionAsync(_mia);
        var requestedAt = Instant.FromDateTimeOffset((DateTimeOffset)scheduled["scheduled"]!["requestedAt"]!);
        await _host.AddMemberAsync(later, _vicId, GroupRole.Member); // joined during the grace period: hand-over

        Assert.Equal(HttpStatusCode.Gone, (await _host.GetFeedAsync(feed)).StatusCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => PurgeAsync(_miaId, requestedAt)); // too early
        _clock.Advance(Duration.FromDays(14));
        Assert.True(await PurgeAsync(_miaId, requestedAt));

        // The account and what was only hers are gone.
        Assert.Null(await _host.QueryAsync(db => db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == _miaId, Ct)));
        Assert.False(await _host.QueryAsync(db => db.Calendars.AnyAsync(c => c.Id == mine || c.Id == soloCalendar, Ct)));
        Assert.False(await _host.QueryAsync(db => db.Groups.AnyAsync(g => g.Id == solo, Ct)));
        Assert.False(await _host.QueryAsync(db => db.FeedTokens.AnyAsync(t => t.Id == feedId, Ct)));
        Assert.False(await _host.QueryAsync(db => db.GroupMembers.AnyAsync(m => m.UserId == _miaId, Ct)));
        Assert.Equal(HttpStatusCode.NotFound, (await _host.GetFeedAsync(feed)).StatusCode); // the token went with the account

        // Shared calendars survive; her grants and overrides are gone, others' stay.
        Assert.True(await _host.QueryAsync(db => db.Calendars.AnyAsync(c => c.Id == _club || c.Id == _olgas, Ct)));
        Assert.False(await _host.QueryAsync(db => db.CalendarGrants.AnyAsync(g => g.PrincipalType == PrincipalType.User && g.PrincipalId == _miaId, Ct)));
        var overrides = await _host.StoredOverridesAsync(shared);
        Assert.Equal([_vicId], overrides.Select(o => o.PrincipalId!.Value));
        Assert.Contains(await _host.AuditEventsAsync("event", shared), a => a.Action == "event.overrides.removed_with_user");
        var tombstone = await _host.StoredEventAsync(clubEvent);
        Assert.Equal(_miaId, tombstone!.CreatorUserId); // tombstone: no account behind it, no floor
        var (seen, _) = await _olga.GetEventAsync(clubEvent);
        Assert.Null(seen["createdBy"]?["displayName"]);

        // Groups: the last owner's group was handed over, billing went to the co-owner.
        Assert.Equal(GroupRole.Owner, await _host.RoleOfAsync(later, _vicId));
        Assert.Equal(_vicId, await _host.QueryAsync(db => db.Groups.Where(g => g.Id == later).Select(g => g.OwnerUserId).SingleAsync(Ct)));
        Assert.Equal(_olgaId, await _host.QueryAsync(db => db.Groups.Where(g => g.Id == pair).Select(g => g.OwnerUserId).SingleAsync(Ct)));
        Assert.Contains(await _host.AuditEventsAsync("group", later), a => a.Action == "group.owner_handed_over");

        // Final email; audit pseudonymised: no snapshots of her own records, no IP / user agent, the user.deleted record.
        Assert.Equal("Your account has been deleted", (await _host.EmailsToAsync(_miaEmail))[^1].Subject);
        var own = await _host.AuditEventsAsync(_miaId);
        Assert.All(own.Where(a => a.Action != "user.deleted"), a => Assert.True(a.Before is null && a.After is null, a.Action));
        Assert.Contains(own, a => a.Action == "user.deleted" && a.ActorKind == "system");
        var acted = await _host.QueryAsync(db => db.Set<AuditEvent>().AsNoTracking().Where(a => a.ActorUserId == _miaId).ToListAsync(Ct));
        Assert.NotEmpty(acted);
        Assert.All(acted, a => Assert.True(a.Ip is null && a.UserAgent is null));
        var all = await _host.QueryAsync(db => db.Set<AuditEvent>().AsNoTracking().ToListAsync(Ct));
        Assert.DoesNotContain(all, a => $"{a.Before}{a.After}".Contains(_miaEmail, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(all, a => a.Action == "group.invite.created" && a.After!.Contains("\"[deleted]\"", StringComparison.Ordinal));

        Assert.False(await PurgeAsync(_miaId, requestedAt)); // idempotent
    }

    /// <summary>What the worker's <c>account.purge</c> job runs.</summary>
    private async Task<bool> PurgeAsync(Guid userId, Instant requestedAt)
    {
        await using var scope = _host.Api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AccountPurgeService>().PurgeAsync(userId, requestedAt, Ct);
    }

    private Task<Instant?> DeletedAtAsync(Guid userId) =>
        _host.QueryAsync(db => db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DeletedAt).SingleAsync(Ct));

    private static Task<HttpResponseMessage> DeleteMeAsync(HttpClient client, object body) =>
        client.SendJsonAsync(HttpMethod.Delete, "/api/v1/me", body);

    private static async Task<JsonNode> RequestDeletionAsync(HttpClient client)
    {
        using var response = await DeleteMeAsync(client, new { password = ApiTestHost.Password });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.JsonAsync();
    }

    private static async Task<JsonNode> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.JsonAsync();
    }

    private async Task<(Guid Id, HttpClient Client, string Email)> PersonAsync(string name)
    {
        var email = ApiTestHost.UniqueEmail(name);
        var id = await _host.CreateUserAsync(email, displayName: name);
        var client = await _host.SignedInClientAsync(email);
        _clients.Add(client);
        return (id, client, email);
    }
}
