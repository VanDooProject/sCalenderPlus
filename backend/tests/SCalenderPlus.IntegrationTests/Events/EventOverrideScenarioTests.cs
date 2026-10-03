using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Calendars;
using SCalenderPlus.IntegrationTests.Groups;
using SCalenderPlus.IntegrationTests.Infrastructure;

namespace SCalenderPlus.IntegrationTests.Events;

/// <summary>
/// The scenarios of the permission engine review (docs/reviews/2026-10-permission-engine-review.md §1) that the
/// api reaches since issue #46: the overrides are set by the people of the scenario through
/// <c>PUT /events/{id}/overrides</c> and every level is read back through <c>GET /events/{id}</c>.
/// </summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class EventOverrideScenarioTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly List<HttpClient> _clients = [];
    private readonly Dictionary<string, (Guid Id, HttpClient Client)> _people = new(StringComparer.Ordinal);
    private ApiTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await ApiTestHost.StartAsync(postgres);

    public async ValueTask DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Family_private_entry()
    {
        // Dad owns the calendar, Mom manages it, the kids (group Family, role member) contribute, Grandma sees busy blocks.
        var family = await (await PersonAsync("dad")).CreateGroupAsync("Family");
        await MembersAsync(family, ("mom", GroupRole.Member), ("tom", GroupRole.Member), ("lisa", GroupRole.Member), ("grandma", GroupRole.Viewer));
        var calendar = await Person("dad").CreateCalendarAsync("Family");
        await GrantAsync(calendar, new { type = "user", id = Id("mom") }, "manage");
        await GrantAsync(calendar, new { type = "group", id = family, minRole = "member" }, "contribute");
        await GrantAsync(calendar, new { type = "user", id = Id("grandma") }, "free_busy");

        var diary = await Person("tom").CreateEventIdAsync(EventApi.Timed(calendar, title: "Diary"));
        await Person("tom").SetOverridesAsync(diary, EventApi.Everyone("none"));
        var dentist = await Person("tom").CreateEventIdAsync(EventApi.Timed(calendar, title: "Dentist"));

        await AssertLevelsAsync(diary, ("tom", "manage"), ("lisa", "none"), ("grandma", "none"), ("dad", "manage"), ("mom", "manage"));
        await AssertLevelsAsync(dentist, ("lisa", "read"), ("grandma", "free_busy"));
    }

    [Fact]
    public async Task Club_own_entries()
    {
        // Group-owned calendar with the default role defaults; Max (member) keeps his physio appointment private.
        var lions = await (await PersonAsync("olga")).CreateGroupAsync("Lions");
        await MembersAsync(lions, ("adam", GroupRole.Admin), ("max", GroupRole.Member), ("mia", GroupRole.Member), ("vic", GroupRole.Viewer));
        var club = await Person("olga").CreateCalendarAsync("Club", lions);

        var physio = await Person("max").CreateEventIdAsync(EventApi.Timed(club, title: "Physio"));
        await Person("max").SetOverridesAsync(physio, EventApi.Everyone("none"));
        var training = await Person("mia").CreateEventIdAsync(EventApi.Timed(club, title: "Training"));

        await AssertLevelsAsync(physio, ("max", "manage"), ("mia", "none"), ("vic", "none"), ("adam", "manage"), ("olga", "manage"));
        await AssertLevelsAsync(training, ("max", "read"), ("vic", "read"), ("mia", "manage"));
    }

    [Fact]
    public async Task Company_hr_busy_only_and_hr_only()
    {
        // Acme owns the calendar (admin manage, member read, viewer free_busy); HR members contribute through a grant.
        var acme = await (await PersonAsync("ceo")).CreateGroupAsync("Acme");
        await MembersAsync(acme, ("lead", GroupRole.Admin), ("hanna", GroupRole.Member), ("bob", GroupRole.Member), ("carl", GroupRole.Member), ("hugo", GroupRole.Member), ("intern", GroupRole.Viewer));
        var hr = await Person("hanna").CreateGroupAsync("HR");
        await MembersAsync(hr, ("hugo", GroupRole.Member));
        var calendar = await Person("ceo").CreateCalendarAsync("Acme", acme, new { admin = "manage", member = "read", viewer = "free_busy" });
        await _host.GrantGroupAsync(calendar, hr, CalendarLevel.Contribute, GroupRole.Member);

        // Hanna (HR, creator floor without external rights) sets everything herself: no entry is external sharing.
        var review = await Person("hanna").CreateEventIdAsync(EventApi.Timed(calendar, title: "Review Bob"));
        await Person("hanna").SetOverridesAsync(review, EventApi.Everyone("free_busy"), EventApi.User(Id("bob"), "read"), EventApi.Group(hr, "read", "member"));
        var hrOnly = await Person("hanna").CreateEventIdAsync(EventApi.Timed(calendar, title: "Salary round"));
        await Person("hanna").SetOverridesAsync(hrOnly, EventApi.Everyone("none"), EventApi.Group(hr, "read", "member"));

        await AssertLevelsAsync(review, ("hanna", "manage"), ("bob", "read"), ("carl", "free_busy"), ("hugo", "read"), ("intern", "free_busy"), ("lead", "manage"));
        await AssertLevelsAsync(hrOnly, ("carl", "none"), ("hugo", "read"), ("intern", "none"), ("lead", "manage"));
    }

    private async Task AssertLevelsAsync(Guid eventId, params (string Person, string Level)[] expected)
    {
        foreach (var (person, level) in expected)
        {
            Assert.True(level == await Person(person).LevelOnAsync(eventId), $"{person} should have {level}");
        }
    }

    private async Task GrantAsync(Guid calendarId, object principal, string level)
    {
        using var response = await Person("dad").SendJsonAsync(HttpMethod.Post, $"/api/v1/calendars/{calendarId}/grants", new { principal, level });
        Assert.True(response.StatusCode == System.Net.HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private async Task MembersAsync(Guid groupId, params (string Name, GroupRole Role)[] members)
    {
        foreach (var (name, role) in members)
        {
            await PersonAsync(name);
            await _host.AddMemberAsync(groupId, Id(name), role);
        }
    }

    private HttpClient Person(string name) => _people[name].Client;

    private Guid Id(string name) => _people[name].Id;

    private async Task<HttpClient> PersonAsync(string name)
    {
        if (_people.TryGetValue(name, out var known))
        {
            return known.Client;
        }

        var email = ApiTestHost.UniqueEmail(name);
        var id = await _host.CreateUserAsync(email, displayName: name);
        var client = await _host.SignedInClientAsync(email);
        _clients.Add(client);
        _people[name] = (id, client);
        return client;
    }
}
