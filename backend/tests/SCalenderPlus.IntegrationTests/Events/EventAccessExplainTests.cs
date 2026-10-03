using System.Net;
using System.Text.Json.Nodes;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Calendars;
using SCalenderPlus.IntegrationTests.Groups;
using SCalenderPlus.IntegrationTests.Infrastructure;
using SCalenderPlus.IntegrationTests.Problems;

namespace SCalenderPlus.IntegrationTests.Events;

/// <summary>
/// Issue #47: <c>GET /events/{id}/access/explain?userId=</c> returns the engine's trace. The worked examples of
/// permissions.md §5 are set up through the api ("FC Lions – Club" of group Lions: Olga owner, Adam admin, Mia
/// member, Vic viewer; Eve outside) and every explanation is compared with <see cref="PermissionEngine.Resolve"/>
/// run on the same inputs here.
/// </summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class EventAccessExplainTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly List<HttpClient> _clients = [];
    private readonly Dictionary<string, (Guid Id, HttpClient Client)> _people = new(StringComparer.Ordinal);
    private ApiTestHost _host = null!;
    private Guid _lions;
    private Guid _friends;
    private Guid _club;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await ApiTestHost.StartAsync(postgres);
        _lions = await (await PersonAsync("olga")).CreateGroupAsync("Lions");
        foreach (var (name, role) in new[] { ("adam", GroupRole.Admin), ("mia", GroupRole.Member), ("vic", GroupRole.Viewer) })
        {
            await PersonAsync(name);
            await _host.AddMemberAsync(_lions, Id(name), role);
        }

        // Eve shares only the group "Friends" with Adam (so he may name her); Sam shares nothing with anyone.
        _friends = await (await PersonAsync("eve")).CreateGroupAsync("Friends");
        await _host.AddMemberAsync(_friends, Id("adam"), GroupRole.Member);
        await PersonAsync("sam");
        _club = await Person("olga").CreateCalendarAsync("FC Lions – Club", _lions);
    }

    public async ValueTask DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Explanations_match_the_engine_for_the_worked_examples()
    {
        // A: Mia's event without overrides. B: Adam's board meeting. C: Mia's private note. D: Mia's training. E: shared with Eve.
        var a = await Person("mia").CreateEventIdAsync(EventApi.Timed(_club, title: "A"));
        var b = await Person("adam").CreateEventIdAsync(EventApi.Timed(_club, title: "B"));
        await Person("adam").SetOverridesAsync(b, EventApi.Everyone("free_busy"), EventApi.Group(_lions, "read", "admin"));
        var c = await Person("mia").CreateEventIdAsync(EventApi.Timed(_club, title: "C"));
        await Person("mia").SetOverridesAsync(c, EventApi.Everyone("none"), EventApi.User(Id("vic"), "read"));
        var d = await Person("mia").CreateEventIdAsync(EventApi.Timed(_club, title: "D"));
        await Person("mia").SetOverridesAsync(d, EventApi.User(Id("vic"), "edit"), EventApi.Group(_lions, "read"));
        var e = await Person("adam").CreateEventIdAsync(EventApi.Timed(_club, title: "E"));
        await Person("adam").SetOverridesAsync(e, EventApi.User(Id("eve"), "read"));

        // The api orders an event's overrides users, groups, anonymous, everyone (the trace lists matches in that order).
        var calendar = new CalendarAcl(_club, Principal.Group(_lions));
        var events = new Dictionary<Guid, EventAcl>
        {
            [a] = new(a, _club, Id("mia")),
            [b] = new(b, _club, Id("adam"), [new(Principal.Group(_lions, GroupRole.Admin), EventLevel.Read), new(Principal.Everyone, EventLevel.FreeBusy)]),
            [c] = new(c, _club, Id("mia"), [new(Principal.User(Id("vic")), EventLevel.Read), new(Principal.Everyone, EventLevel.None)]),
            [d] = new(d, _club, Id("mia"), [new(Principal.User(Id("vic")), EventLevel.Edit), new(Principal.Group(_lions), EventLevel.Read)]),
            [e] = new(e, _club, Id("adam"), [new(Principal.User(Id("eve")), EventLevel.Read)]),
        };
        var roles = new Dictionary<string, GroupRole> { ["olga"] = GroupRole.Owner, ["adam"] = GroupRole.Admin, ["mia"] = GroupRole.Member, ["vic"] = GroupRole.Viewer };
        var expectedLevels = new Dictionary<(Guid, string), string>
        {
            [(a, "olga")] = "manage",
            [(a, "adam")] = "manage",
            [(a, "mia")] = "manage",
            [(a, "vic")] = "read",
            [(b, "olga")] = "manage",
            [(b, "adam")] = "manage",
            [(b, "mia")] = "free_busy",
            [(b, "vic")] = "free_busy",
            [(c, "olga")] = "manage",
            [(c, "adam")] = "manage",
            [(c, "mia")] = "manage",
            [(c, "vic")] = "read",
            [(d, "olga")] = "manage",
            [(d, "mia")] = "manage",
            [(d, "vic")] = "edit",
            [(e, "eve")] = "read",
            [(e, "vic")] = "read",
        };

        foreach (var ((eventId, person), level) in expectedLevels)
        {
            var principal = roles.TryGetValue(person, out var role)
                ? PrincipalContext.ForUser(Id(person), new Dictionary<Guid, GroupRole> { [_lions] = role })
                : PrincipalContext.ForUser(Id(person), new Dictionary<Guid, GroupRole> { [_friends] = GroupRole.Owner });
            var engine = PermissionEngine.Resolve(principal, calendar, events[eventId]);

            // The calendar's owner explains everyone (manager); everyone explains themselves.
            foreach (var explanation in new[] { await ExplainAsync(Person("olga"), eventId, Id(person)), await ExplainAsync(Person(person), eventId) })
            {
                Assert.Equal(level, (string?)explanation["level"]);
                Assert.Equal(PermissionLevels.Format(engine.Level), (string?)explanation["level"]);
                Assert.Equal(PermissionLevels.Format(engine.CalendarLevel), (string?)explanation["calendarLevel"]);
                Assert.Equal(Id(person), (Guid)explanation["user"]!["id"]!);
                Assert.Equal(person, (string?)explanation["user"]!["displayName"]);
                Assert.Equal(engine.Steps.Select(Expected), explanation["steps"]!.AsArray().Select(Actual));
            }
        }

        // Spot checks of the rendering: Mia in B is restricted by the everyone tier.
        var mia = await ExplainAsync(Person("olga"), b, Id("mia"));
        Assert.Equal(["group_role_default", "calendar_result", "base_level", "override_matched", "override_applied", "restrict_only", "result"], mia["steps"]!.AsArray().Select(s => (string)s!["kind"]!));
        Assert.Equal("Lions", (string?)mia["steps"]![0]!["principalName"]);
        Assert.Equal("member", (string?)mia["steps"]![0]!["role"]);
        Assert.Equal("everyone", (string?)mia["steps"]![3]!["principal"]!["type"]);
        Assert.True((bool)mia["steps"]![3]!["decisive"]!);
        Assert.Equal(1, (int)mia["steps"]![3]!["tier"]!);
        var self = await ExplainAsync(Person("mia"), c);
        Assert.Equal(["group_role_default", "calendar_result", "creator_floor", "result"], self["steps"]!.AsArray().Select(s => (string)s!["kind"]!));
    }

    [Fact]
    public async Task Explaining_others_needs_calendar_manage_and_a_visible_user()
    {
        var training = await Person("mia").CreateEventIdAsync(EventApi.Timed(_club, title: "Training"));

        // Mia has the creator floor on her event but is no calendar manager.
        using (var creator = await GetAsync(Person("mia"), training, Id("vic")))
        {
            var problem = await ProblemResponse.AssertProblemAsync(creator, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);
            Assert.Equal("manage", (string?)problem["required"]);
            Assert.Equal("contribute", (string?)problem["actual"]);
        }

        // Strangers, unknown ids and outsiders sharing nothing with the caller are 404 alike.
        foreach (var userId in new[] { Id("sam"), Guid.CreateVersion7(), Id("eve") })
        {
            using var hidden = await GetAsync(Person("olga"), training, userId);
            await ProblemResponse.AssertProblemAsync(hidden, HttpStatusCode.NotFound, ErrorCodes.NotFound);
        }

        // Adam shares a group with Eve: he may explain why she cannot see it.
        var eve = await ExplainAsync(Person("adam"), training, Id("eve"));
        Assert.Equal("none", (string?)eve["level"]);
        Assert.Equal(["calendar_result", "base_level", "no_matching_override", "result"], eve["steps"]!.AsArray().Select(s => (string)s!["kind"]!));

        // Without a level on the event, even explaining oneself is 404.
        using var outsider = await GetAsync(Person("eve"), training, null);
        await ProblemResponse.AssertProblemAsync(outsider, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Transparent_events_are_hidden_from_free_busy_viewers()
    {
        await _host.GrantAsync(_club, Id("sam"), CalendarLevel.FreeBusy);
        var lunch = await Person("adam").CreateEventIdAsync(EventApi.Timed(_club, title: "Lunch", transparency: "transparent"));

        var sam = await ExplainAsync(Person("olga"), lunch, Id("sam"));

        Assert.Equal("none", (string?)sam["level"]);
        Assert.True((bool)sam["hiddenAsTransparent"]!);
        Assert.Equal("free_busy", (string?)sam["steps"]!.AsArray().Last()!["eventLevel"]);
        using var self = await GetAsync(Person("sam"), lunch, null);
        await ProblemResponse.AssertProblemAsync(self, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    private static string Expected(ResolutionStep step) =>
        string.Join(
            "|",
            Snake(step.Kind.ToString()),
            step.Principal is null ? "-" : $"{Snake(step.Principal.Type.ToString())}:{step.Principal.Id}:{(step.Principal.MinRole is { } r ? GroupRoles.Format(r) : "-")}",
            step.Role is { } role ? GroupRoles.Format(role) : "-",
            step.CalendarLevel is { } cl ? PermissionLevels.Format(cl) : "-",
            step.EventLevel is { } el ? PermissionLevels.Format(el) : "-",
            step.Tier?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-",
            step.Decisive?.ToString() ?? "-",
            step.Applied?.ToString() ?? "-");

    private static string Actual(JsonNode? step)
    {
        var principal = step!["principal"];
        return string.Join(
            "|",
            (string)step["kind"]!,
            principal is null ? "-" : $"{(string)principal["type"]!}:{(Guid?)principal["id"]}:{(string?)principal["minRole"] ?? "-"}",
            (string?)step["role"] ?? "-",
            (string?)step["calendarLevel"] ?? "-",
            (string?)step["eventLevel"] ?? "-",
            ((int?)step["tier"])?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-",
            ((bool?)step["decisive"])?.ToString() ?? "-",
            ((bool?)step["applied"])?.ToString() ?? "-");
    }

    private static string Snake(string pascal) =>
        string.Concat(pascal.Select((ch, i) => char.IsUpper(ch) && i > 0 ? "_" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString()));

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, Guid eventId, Guid? userId) =>
        client.GetAsync(new Uri($"/api/v1/events/{eventId}/access/explain" + (userId is { } id ? $"?userId={id}" : string.Empty), UriKind.Relative), Ct);

    private static async Task<JsonNode> ExplainAsync(HttpClient client, Guid eventId, Guid? userId = null)
    {
        using var response = await GetAsync(client, eventId, userId);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.JsonAsync();
    }

    private HttpClient Person(string name) => _people[name].Client;

    private Guid Id(string name) => _people[name].Id;

    private async Task<HttpClient> PersonAsync(string name)
    {
        var email = ApiTestHost.UniqueEmail(name);
        var id = await _host.CreateUserAsync(email, displayName: name);
        var client = await _host.SignedInClientAsync(email);
        _clients.Add(client);
        _people[name] = (id, client);
        return client;
    }
}
