using System.Net;
using System.Text.Json.Nodes;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Calendars;
using SCalenderPlus.IntegrationTests.Events;
using SCalenderPlus.IntegrationTests.Groups;
using SCalenderPlus.IntegrationTests.Infrastructure;
using SCalenderPlus.IntegrationTests.Problems;

namespace SCalenderPlus.IntegrationTests.Entitlements;

/// <summary>Issue #52: plan limits of groups (plans.md) through the api — Free for everyone with a billing provider, self-host unlimited unless configured.</summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class EntitlementTests(PostgresFixture postgres) : IAsyncDisposable
{
    private readonly List<HttpClient> _clients = [];
    private ApiTestHost? _host;

    private ApiTestHost Host => _host ?? throw new InvalidOperationException("Start the host first.");

    public async ValueTask DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }

    [Fact]
    public async Task Free_users_own_one_group()
    {
        await StartAsync(new() { ["Billing:Provider"] = "stripe" });
        var (_, olga) = await PersonAsync("olga");
        await olga.CreateGroupAsync("FC Lions");

        using var second = await olga.SendJsonAsync(HttpMethod.Post, "/api/v1/groups", new { name = "Tennis" });

        var problem = await ProblemResponse.AssertProblemAsync(second, HttpStatusCode.PaymentRequired, ErrorCodes.PlanLimitReached);
        AssertLimit(problem, "owned_groups", max: 1, used: 1);
        Assert.Equal("free", (string?)problem["plan"]);
        Assert.Contains("1", (string?)problem["detail"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Free_groups_take_fifteen_members_through_invites()
    {
        await StartAsync(new() { ["Billing:Provider"] = "stripe" });
        var (_, olga) = await PersonAsync("olga");
        var groupId = await olga.CreateGroupAsync("FC Lions");
        for (var i = 0; i < 13; i++)
        {
            var email = ApiTestHost.UniqueEmail($"member{i}");
            await Host.AddMemberAsync(groupId, await Host.CreateUserAsync(email), GroupRole.Member);
        }

        // 14 members: an invite link is still possible, and one more person may join.
        var token = await CreateLinkAsync(olga, groupId);
        var (_, mia) = await PersonAsync("mia");
        using var joined = await AcceptAsync(mia, token);
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);

        // 15 members: the 16th neither joins nor gets invited.
        var (vicEmail, vic) = await PersonAsync("vic");
        using var refused = await AcceptAsync(vic, token);
        AssertLimit(await ProblemResponse.AssertProblemAsync(refused, HttpStatusCode.PaymentRequired, ErrorCodes.PlanLimitReached), "members_per_group", max: 15, used: 15);

        using var invite = await olga.SendJsonAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/invites", new { email = ApiTestHost.UniqueEmail("eve"), role = "member" });
        AssertLimit(await ProblemResponse.AssertProblemAsync(invite, HttpStatusCode.PaymentRequired, ErrorCodes.PlanLimitReached), "members_per_group", max: 15, used: 15);
        Assert.Null(await Host.RoleOfAsync(groupId, (await Host.FindUserAsync(vicEmail)).Id));
    }

    [Fact]
    public async Task Configured_limits_override_the_plan_defaults()
    {
        await StartAsync(new() { ["Billing:Provider"] = "stripe", ["Plans:Free:OwnedGroups"] = "2", ["Plans:Free:MembersPerGroup"] = "1" });
        var (_, olga) = await PersonAsync("olga");
        var groupId = await olga.CreateGroupAsync("FC Lions");
        await olga.CreateGroupAsync("Tennis");

        using var invite = await olga.SendJsonAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/invites", new { role = "member" });

        AssertLimit(await ProblemResponse.AssertProblemAsync(invite, HttpStatusCode.PaymentRequired, ErrorCodes.PlanLimitReached), "members_per_group", max: 1, used: 1);
    }

    [Fact]
    public async Task Free_users_own_three_calendars_counting_those_of_groups_they_bill()
    {
        await StartAsync(new() { ["Billing:Provider"] = "stripe", ["Plans:Free:OwnedGroups"] = "2" });
        var (_, olga) = await PersonAsync("olga");
        var groupId = await olga.CreateGroupAsync("FC Lions");
        var (adamEmail, adam) = await PersonAsync("adam");
        await Host.AddMemberAsync(groupId, (await Host.FindUserAsync(adamEmail)).Id, GroupRole.Admin);
        await olga.CreateCalendarAsync("Family");
        await olga.CreateCalendarAsync("Work");
        await adam.CreateCalendarAsync("Club", groupId); // Olga bills the group: counts for her

        using var fourth = await olga.SendJsonAsync(HttpMethod.Post, "/api/v1/calendars", new { name = "Hobby", defaultTimeZone = "UTC" });
        AssertLimit(await ProblemResponse.AssertProblemAsync(fourth, HttpStatusCode.PaymentRequired, ErrorCodes.PlanLimitReached), "owned_calendars", max: 3, used: 3);
        using var groupFourth = await adam.SendJsonAsync(HttpMethod.Post, "/api/v1/calendars", new { name = "Fixtures", defaultTimeZone = "UTC", groupId });
        AssertLimit(await ProblemResponse.AssertProblemAsync(groupFourth, HttpStatusCode.PaymentRequired, ErrorCodes.PlanLimitReached), "owned_calendars", max: 3, used: 3);

        // Adam's own plan is untouched by the group's calendar.
        await adam.CreateCalendarAsync("Adam's");
    }

    [Fact]
    public async Task Concurrent_calendar_creations_cannot_exceed_the_limit()
    {
        await StartAsync(new() { ["Billing:Provider"] = "stripe", ["Plans:Free:OwnedCalendars"] = "1" });
        var (email, _) = await PersonAsync("olga");
        var clients = new List<HttpClient>();
        for (var i = 0; i < 5; i++)
        {
            var client = await Host.SignedInClientAsync(email);
            _clients.Add(client);
            clients.Add(client);
        }

        var responses = await Task.WhenAll(clients.Select((c, i) => c.SendJsonAsync(HttpMethod.Post, "/api/v1/calendars", new { name = $"Calendar {i}", defaultTimeZone = "UTC" })));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.PaymentRequired));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Event_overrides_count_against_the_calendar_owners_plan()
    {
        await StartAsync(new() { ["Billing:Provider"] = "stripe", ["Plans:Free:EventsWithOverrides"] = "2", ["Plans:Free:OverridesPerEvent"] = "2" });
        var (_, olga) = await PersonAsync("olga");
        var family = await olga.CreateGroupAsync("Family");
        var calendar = await olga.CreateCalendarAsync("Olga");
        var a = await olga.CreateEventIdAsync(EventApi.Timed(calendar, start: "2099-01-01T10:00:00", end: "2099-01-01T11:00:00"));
        var b = await olga.CreateEventIdAsync(EventApi.Timed(calendar, start: "2099-01-02T10:00:00", end: "2099-01-02T11:00:00"));
        var c = await olga.CreateEventIdAsync(EventApi.Timed(calendar, start: "2099-01-03T10:00:00", end: "2099-01-03T11:00:00"));
        var past = await olga.CreateEventIdAsync(EventApi.Timed(calendar, start: "2000-01-01T10:00:00", end: "2000-01-01T11:00:00"));

        await olga.SetOverridesAsync(a, EventApi.Everyone("none"));
        await olga.SetOverridesAsync(b, EventApi.Everyone("none"));
        using (var third = await olga.PutOverridesAsync(c, [EventApi.Everyone("none")]))
        {
            AssertLimit(await ProblemResponse.AssertProblemAsync(third, HttpStatusCode.PaymentRequired, ErrorCodes.PlanLimitReached), "events_with_overrides", max: 2, used: 2);
        }

        // Past events do not count; an event with overrides already may change them; entries per event are capped.
        await olga.SetOverridesAsync(past, EventApi.Everyone("none"));
        await olga.SetOverridesAsync(a, EventApi.Everyone("none"), EventApi.Group(family, "read"));
        using (var tooMany = await olga.PutOverridesAsync(a, [EventApi.Everyone("none"), EventApi.Group(family, "read"), new { principal = new { type = "anonymous" }, level = "none" }]))
        {
            AssertLimit(await ProblemResponse.AssertProblemAsync(tooMany, HttpStatusCode.PaymentRequired, ErrorCodes.PlanLimitReached), "overrides_per_event", max: 2, used: 2);
        }

        // Removing always passes and makes room.
        await olga.SetOverridesAsync(b);
        await olga.SetOverridesAsync(c, EventApi.Everyone("none"));
    }

    [Fact]
    public async Task Moved_overrides_count_against_the_target_owners_plan()
    {
        await StartAsync(new() { ["Billing:Provider"] = "stripe", ["Plans:Free:EventsWithOverrides"] = "1" });
        var (_, olga) = await PersonAsync("olga");
        var (miaEmail, mia) = await PersonAsync("mia");
        var lions = await olga.CreateGroupAsync("Lions");
        await Host.AddMemberAsync(lions, (await Host.FindUserAsync(miaEmail)).Id, GroupRole.Member);
        var club = await olga.CreateCalendarAsync("Club", lions);
        var personal = await mia.CreateCalendarAsync("Mia");
        var own = await mia.CreateEventIdAsync(EventApi.Timed(personal, start: "2099-01-01T10:00:00", end: "2099-01-01T11:00:00"));
        await mia.SetOverridesAsync(own, EventApi.Everyone("none"));
        var training = await mia.CreateEventIdAsync(EventApi.Timed(club, start: "2099-01-02T10:00:00", end: "2099-01-02T11:00:00"));
        await mia.SetOverridesAsync(training, EventApi.Everyone("none"));

        using var move = await mia.SendJsonAsync(HttpMethod.Post, $"/api/v1/events/{training}/move", new { targetCalendarId = personal }, ifMatch: "*");

        AssertLimit(await ProblemResponse.AssertProblemAsync(move, HttpStatusCode.PaymentRequired, ErrorCodes.PlanLimitReached), "events_with_overrides", max: 1, used: 1);
    }

    [Fact]
    public async Task Self_hosting_is_unlimited_unless_the_operator_sets_limits()
    {
        await StartAsync([]);
        var (_, olga) = await PersonAsync("olga");
        for (var i = 0; i < 3; i++)
        {
            await olga.CreateGroupAsync($"Group {i}");
        }

        await Host.DisposeAsync();
        _host = null;
        await StartAsync(new() { ["Plans:SelfHost:OwnedGroups"] = "1" });
        var (_, mia) = await PersonAsync("mia");
        await mia.CreateGroupAsync("FC Lions");
        using var second = await mia.SendJsonAsync(HttpMethod.Post, "/api/v1/groups", new { name = "Tennis" });

        var problem = await ProblemResponse.AssertProblemAsync(second, HttpStatusCode.PaymentRequired, ErrorCodes.PlanLimitReached);
        Assert.Equal("selfhost", (string?)problem["plan"]);
    }

    private static void AssertLimit(JsonObject problem, string key, int max, int used)
    {
        var limit = problem["limit"]!;
        Assert.Equal(key, (string?)limit["key"]);
        Assert.Equal(max, (int)limit["max"]!);
        Assert.Equal(used, (int)limit["used"]!);
    }

    private async Task StartAsync(Dictionary<string, string?> settings) =>
        _host = await ApiTestHost.StartAsync(postgres, configure: s =>
        {
            foreach (var (key, value) in settings)
            {
                s[key] = value;
            }
        });

    private async Task<(string Email, HttpClient Client)> PersonAsync(string name)
    {
        var email = ApiTestHost.UniqueEmail(name);
        await Host.CreateUserAsync(email, displayName: name);
        var client = await Host.SignedInClientAsync(email);
        _clients.Add(client);
        return (email, client);
    }

    private static async Task<string> CreateLinkAsync(HttpClient owner, Guid groupId)
    {
        using var created = await owner.SendJsonAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/invites", new { role = "member" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var url = (string)(await created.JsonAsync())["url"]!;
        return Uri.UnescapeDataString(url.Split("token=")[1]);
    }

    private static Task<HttpResponseMessage> AcceptAsync(HttpClient client, string token) =>
        client.SendJsonAsync(HttpMethod.Post, "/api/v1/invites/accept", new { token });
}
