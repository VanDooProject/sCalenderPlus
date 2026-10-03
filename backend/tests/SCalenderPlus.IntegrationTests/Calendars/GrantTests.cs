using System.Net;
using System.Text.Json.Nodes;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Groups;
using SCalenderPlus.IntegrationTests.Infrastructure;
using SCalenderPlus.IntegrationTests.Problems;

namespace SCalenderPlus.IntegrationTests.Calendars;

/// <summary>Issue #42: calendar grants, and <c>myLevel</c> of the example principals of permissions.md.</summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class GrantTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly Dictionary<string, (Guid Id, HttpClient Client)> _people = new(StringComparer.Ordinal);
    private ApiTestHost _host = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _host = await ApiTestHost.StartAsync(postgres);

    public async ValueTask DisposeAsync()
    {
        foreach (var (_, client) in _people.Values)
        {
            client.Dispose();
        }

        await _host.DisposeAsync();
    }

    /// <summary>
    /// The acceptance criterion: <c>GET /calendars</c> returns <c>myLevel</c> of every example principal — the
    /// worked examples' club calendar (permissions.md §5; the share link is M2's share-link issue) and the review's
    /// family and two-groups calendars, all set up through the api.
    /// </summary>
    [Fact]
    public async Task My_level_is_right_for_every_example_principal()
    {
        // §5: "FC Lions – Club", owned by group Lions with default role defaults.
        var olga = await PersonAsync("Olga");
        var lions = await olga.CreateGroupAsync("Lions");
        await JoinAsync(lions, "Adam", GroupRole.Admin);
        await JoinAsync(lions, "Mia", GroupRole.Member);
        await JoinAsync(lions, "Max", GroupRole.Member);
        await JoinAsync(lions, "Vic", GroupRole.Viewer);
        await PersonAsync("Eve");
        var club = await olga.CreateCalendarAsync("FC Lions – Club", lions);

        // Review "Family": Dad owns, Mom manage, kids (Family[member]) contribute, Grandma free_busy.
        var dad = await PersonAsync("Dad");
        var family = await dad.CreateGroupAsync("Family");
        await JoinAsync(family, "Mom", GroupRole.Admin);
        await JoinAsync(family, "Tom", GroupRole.Member);
        await JoinAsync(family, "Lisa", GroupRole.Viewer);
        await JoinAsync(family, "Grandma", GroupRole.Viewer);
        var home = await dad.CreateCalendarAsync("Home");
        await GrantAsync(dad, home, "user", Id("Mom"), "manage");
        await GrantAsync(dad, home, "group", family, "contribute", "member");
        await GrantAsync(dad, home, "user", Id("Grandma"), "free_busy");

        // Review "Two groups": group:A → read, group:B[member] → edit; Pat: A member, B viewer; Quinn: A and B member.
        var coach = await PersonAsync("Coach");
        var a = await coach.CreateGroupAsync("A");
        var b = await coach.CreateGroupAsync("B");
        await JoinAsync(a, "Pat", GroupRole.Member);
        await _host.AddMemberAsync(b, Id("Pat"), GroupRole.Viewer);
        await JoinAsync(a, "Quinn", GroupRole.Member);
        await _host.AddMemberAsync(b, Id("Quinn"), GroupRole.Member);
        var season = await coach.CreateCalendarAsync("Season");
        await GrantAsync(coach, season, "group", a, "read");
        await GrantAsync(coach, season, "group", b, "edit", "member");

        var expected = new Dictionary<string, Dictionary<Guid, string>>(StringComparer.Ordinal)
        {
            ["Olga"] = new() { [club] = "owner" },
            ["Adam"] = new() { [club] = "manage" },
            ["Mia"] = new() { [club] = "contribute" },
            ["Max"] = new() { [club] = "contribute" },
            ["Vic"] = new() { [club] = "read" },
            ["Eve"] = [],
            ["Dad"] = new() { [home] = "owner" },
            ["Mom"] = new() { [home] = "manage" },
            ["Tom"] = new() { [home] = "contribute" },
            ["Lisa"] = [], // viewer: Family[member] does not match
            ["Grandma"] = new() { [home] = "free_busy" },
            ["Coach"] = new() { [season] = "owner" },
            ["Pat"] = new() { [season] = "read" },
            ["Quinn"] = new() { [season] = "edit" },
        };
        foreach (var (name, levels) in expected)
        {
            Assert.Equal(levels, await Client(name).MyLevelsAsync());
        }

        // Single reads agree with the list.
        Assert.Equal("free_busy", (string?)(await Client("Grandma").GetCalendarAsync(home)).Body["myLevel"]);
        using var lisa = await Client("Lisa").GetAsync(new Uri($"/api/v1/calendars/{home}", UriKind.Relative), Ct);
        await ProblemResponse.AssertProblemAsync(lisa, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Managers_grant_list_change_and_remove_with_acl_version_bumps_and_audit()
    {
        var olga = await PersonAsync("Olga");
        var lions = await olga.CreateGroupAsync("Lions");
        await JoinAsync(lions, "Adam", GroupRole.Admin);
        await JoinAsync(lions, "Mia", GroupRole.Member);
        var calendar = await olga.CreateCalendarAsync("Fixtures");
        var calendarAcl = await _host.CalendarAclVersionAsync(calendar);
        var miaAcl = await _host.AclVersionAsync(Id("Mia"));
        var lionsAcl = await _host.GroupAclVersionAsync(lions);

        using var created = await olga.SendJsonAsync(HttpMethod.Post, $"/api/v1/calendars/{calendar}/grants", new { principal = new { type = "user", id = Id("Mia") }, level = "read" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var grant = await created.JsonAsync();
        var grantId = (Guid)grant["id"]!;
        Assert.Equal($"/api/v1/calendars/{calendar}/grants/{grantId}", created.Headers.Location!.OriginalString);
        Assert.Equal("user", (string?)grant["principal"]!["type"]);
        Assert.Equal(Id("Mia"), (Guid)grant["principal"]!["id"]!);
        Assert.Null((string?)grant["principal"]!["minRole"]);
        Assert.Equal("Mia", (string?)grant["principalName"]);
        Assert.Equal("read", (string?)grant["level"]);
        Assert.Equal(Id("Olga"), (Guid)grant["createdBy"]!);
        Assert.Equal(created.Headers.ETag!.ToString(), (string?)grant["etag"]);
        Assert.Equal(calendarAcl + 1, await _host.CalendarAclVersionAsync(calendar));
        Assert.Equal(miaAcl + 1, await _host.AclVersionAsync(Id("Mia")));
        Assert.Equal("read", (await Client("Mia").MyLevelsAsync())[calendar]);

        await GrantAsync(olga, calendar, "group", lions, "free_busy", "admin");
        Assert.Equal(lionsAcl + 1, await _host.GroupAclVersionAsync(lions));

        using var duplicate = await olga.SendJsonAsync(HttpMethod.Post, $"/api/v1/calendars/{calendar}/grants", new { principal = new { type = "user", id = Id("Mia") }, level = "edit" });
        await ProblemResponse.AssertProblemAsync(duplicate, HttpStatusCode.Conflict, ErrorCodes.Conflict);

        // The list (managers) names the principals; pages by creation.
        using var first = await olga.GetAsync(new Uri($"/api/v1/calendars/{calendar}/grants?limit=1", UriKind.Relative), Ct);
        var page1 = await first.JsonAsync();
        var listed = Assert.Single(page1["items"]!.AsArray())!;
        Assert.Equal(grantId, (Guid)listed["id"]!);
        using var second = await olga.GetAsync(new Uri($"/api/v1/calendars/{calendar}/grants?limit=1&cursor={(string?)page1["nextCursor"]}", UriKind.Relative), Ct);
        var page2 = await second.JsonAsync();
        var groupGrant = Assert.Single(page2["items"]!.AsArray())!;
        Assert.Equal("Lions", (string?)groupGrant["principalName"]);
        Assert.Equal("admin", (string?)groupGrant["principal"]!["minRole"]);
        Assert.Null((string?)page2["nextCursor"]);

        // Change (If-Match: the grant's etag).
        var path = $"/api/v1/calendars/{calendar}/grants/{grantId}";
        using var missing = await olga.SendJsonAsync(HttpMethod.Patch, path, new { level = "edit" });
        await ProblemResponse.AssertProblemAsync(missing, HttpStatusCode.PreconditionRequired, ErrorCodes.PreconditionRequired);
        using var changed = await olga.SendJsonAsync(HttpMethod.Patch, path, new { level = "edit" }, (string)listed["etag"]!, "application/merge-patch+json");
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("edit", (string?)(await changed.JsonAsync())["level"]);
        Assert.Equal("edit", (await Client("Mia").MyLevelsAsync())[calendar]);
        using var stale = await olga.SendJsonAsync(HttpMethod.Patch, path, new { level = "read" }, (string)listed["etag"]!);
        await ProblemResponse.AssertProblemAsync(stale, HttpStatusCode.PreconditionFailed, ErrorCodes.PreconditionFailed);

        // Remove.
        var beforeRemoval = await _host.AclVersionAsync(Id("Mia"));
        using var removed = await olga.SendJsonAsync(HttpMethod.Delete, path, ifMatch: changed.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.DoesNotContain(calendar, (await Client("Mia").MyLevelsAsync()).Keys);
        Assert.Equal(beforeRemoval + 1, await _host.AclVersionAsync(Id("Mia")));
        using var gone = await olga.SendJsonAsync(HttpMethod.Delete, path, ifMatch: "*");
        await ProblemResponse.AssertProblemAsync(gone, HttpStatusCode.NotFound, ErrorCodes.NotFound);

        var audit = (await _host.AuditEventsAsync("calendar", calendar)).Where(e => e.Action.StartsWith("calendar.grant.", StringComparison.Ordinal)).ToList();
        Assert.Equal(["calendar.grant.created", "calendar.grant.created", "calendar.grant.updated", "calendar.grant.removed"], audit.Select(e => e.Action));
        Assert.Equal($"user:{Id("Mia")}", (string?)JsonNode.Parse(audit[0].After!)!["principal"]);
        Assert.Equal("read", (string?)JsonNode.Parse(audit[2].Before!)!["level"]);
        Assert.Equal("edit", (string?)JsonNode.Parse(audit[2].After!)!["level"]);
        Assert.Null(audit[3].After);
        Assert.All(audit, e => Assert.Equal(Id("Olga"), e.SubjectId));
    }

    [Fact]
    public async Task Grants_never_carry_owner_and_never_reach_above_the_managers_level()
    {
        var olga = await PersonAsync("Olga");
        var lions = await olga.CreateGroupAsync("Lions");
        await JoinAsync(lions, "Adam", GroupRole.Admin);
        await JoinAsync(lions, "Mia", GroupRole.Member);
        await JoinAsync(lions, "Vic", GroupRole.Viewer);
        var club = await olga.CreateCalendarAsync("Club", lions);

        using var owner = await Client("Adam").SendJsonAsync(HttpMethod.Post, $"/api/v1/calendars/{club}/grants", new { principal = new { type = "user", id = Id("Vic") }, level = "owner" });
        var problem = await ProblemResponse.AssertProblemAsync(owner, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.NotNull(problem["errors"]!["level"]);

        // A manager (admin default) grants up to manage …
        await GrantAsync(Client("Adam"), club, "user", Id("Vic"), "manage");
        Assert.Equal("manage", (await Client("Vic").MyLevelsAsync())[club]);

        // … but contributors cannot share at all (403 with the required level).
        using var byMember = await Client("Mia").SendJsonAsync(HttpMethod.Post, $"/api/v1/calendars/{club}/grants", new { principal = new { type = "user", id = Id("Vic") }, level = "read" });
        var forbidden = await ProblemResponse.AssertProblemAsync(byMember, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);
        Assert.Equal("manage", (string?)forbidden["required"]);
        Assert.Equal("contribute", (string?)forbidden["actual"]);
    }

    [Fact]
    public async Task Only_selectable_principals_can_be_granted()
    {
        var olga = await PersonAsync("Olga");
        var lions = await olga.CreateGroupAsync("Lions");
        var eve = await PersonAsync("Eve");
        var evesGroup = await eve.CreateGroupAsync("Eve's");
        var calendar = await olga.CreateCalendarAsync("Private");
        var club = await olga.CreateCalendarAsync("Club", lions);

        foreach (var (type, id) in new[] { ("user", Id("Eve")), ("user", Guid.CreateVersion7()), ("group", evesGroup), ("group", Guid.CreateVersion7()), ("user", Id("Olga")) })
        {
            using var refused = await olga.SendJsonAsync(HttpMethod.Post, $"/api/v1/calendars/{calendar}/grants", new { principal = new { type, id }, level = "read" });
            var problem = await ProblemResponse.AssertProblemAsync(refused, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
            Assert.NotNull(problem["errors"]!["principal"]);
        }

        using var ownerGroup = await olga.SendJsonAsync(HttpMethod.Post, $"/api/v1/calendars/{club}/grants", new { principal = new { type = "group", id = lions, minRole = "member" }, level = "edit" });
        await ProblemResponse.AssertProblemAsync(ownerGroup, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);

        using var badType = await olga.SendJsonAsync(HttpMethod.Post, $"/api/v1/calendars/{calendar}/grants", new { principal = new { type = "everyone", id = lions }, level = "read" });
        Assert.NotNull((await ProblemResponse.AssertProblemAsync(badType, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed))["errors"]!["principal.type"]);
        using var userRole = await olga.SendJsonAsync(HttpMethod.Post, $"/api/v1/calendars/{calendar}/grants", new { principal = new { type = "user", id = Id("Eve"), minRole = "admin" }, level = "read" });
        Assert.NotNull((await ProblemResponse.AssertProblemAsync(userRole, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed))["errors"]!["principal.minRole"]);

        // Once Eve shares a group with Olga she can be picked; afterwards anyone who sees the calendar can be.
        await _host.AddMemberAsync(lions, Id("Eve"), GroupRole.Viewer);
        await GrantAsync(olga, calendar, "user", Id("Eve"), "read");
    }

    [Fact]
    public async Task Managers_cannot_take_away_their_own_manage_level()
    {
        var dad = await PersonAsync("Dad");
        var family = await dad.CreateGroupAsync("Family");
        await JoinAsync(family, "Mom", GroupRole.Member);
        var home = await dad.CreateCalendarAsync("Home");
        var momGrant = await GrantAsync(dad, home, "user", Id("Mom"), "manage");
        var path = $"/api/v1/calendars/{home}/grants/{momGrant}";

        using var lower = await Client("Mom").SendJsonAsync(HttpMethod.Patch, path, new { level = "edit" }, "*");
        await ProblemResponse.AssertProblemAsync(lower, HttpStatusCode.Conflict, ErrorCodes.PermissionSelfLockout);
        using var remove = await Client("Mom").SendJsonAsync(HttpMethod.Delete, path, ifMatch: "*");
        await ProblemResponse.AssertProblemAsync(remove, HttpStatusCode.Conflict, ErrorCodes.PermissionSelfLockout);

        // With a second path to manage (the group grant) Mom may drop the user grant; the owner always may.
        await GrantAsync(dad, home, "group", family, "manage");
        using var allowed = await Client("Mom").SendJsonAsync(HttpMethod.Patch, path, new { level = "read" }, "*");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var byOwner = await dad.SendJsonAsync(HttpMethod.Delete, path, ifMatch: "*");
        Assert.Equal(HttpStatusCode.NoContent, byOwner.StatusCode);
    }

    private static async Task<Guid> GrantAsync(HttpClient client, Guid calendarId, string type, Guid id, string level, string? minRole = null)
    {
        using var response = await client.SendJsonAsync(HttpMethod.Post, $"/api/v1/calendars/{calendarId}/grants", new { principal = new { type, id, minRole }, level });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (Guid)(await response.JsonAsync())["id"]!;
    }

    private async Task<HttpClient> PersonAsync(string name)
    {
        var email = ApiTestHost.UniqueEmail(name.ToLowerInvariant());
        var id = await _host.CreateUserAsync(email, displayName: name);
        var client = await _host.SignedInClientAsync(email);
        _people[name] = (id, client);
        return client;
    }

    private async Task JoinAsync(Guid groupId, string name, GroupRole role)
    {
        if (!_people.ContainsKey(name))
        {
            await PersonAsync(name);
        }

        await _host.AddMemberAsync(groupId, Id(name), role);
    }

    private Guid Id(string name) => _people[name].Id;

    private HttpClient Client(string name) => _people[name].Client;
}
