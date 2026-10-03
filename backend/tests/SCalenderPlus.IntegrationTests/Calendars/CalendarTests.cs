using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Groups;
using SCalenderPlus.IntegrationTests.Infrastructure;
using SCalenderPlus.IntegrationTests.Problems;

namespace SCalenderPlus.IntegrationTests.Calendars;

/// <summary>Issue #41: calendars CRUD (user- and group-owned) with levels from the permission engine.</summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class CalendarTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly List<HttpClient> _clients = [];
    private ApiTestHost _host = null!;
    private HttpClient _olga = null!;
    private Guid _olgaId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await ApiTestHost.StartAsync(postgres);
        (_olgaId, _olga) = await PersonAsync("olga");
    }

    public async ValueTask DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Anyone_creates_a_personal_calendar_and_owns_it()
    {
        using var response = await _olga.SendJsonAsync(HttpMethod.Post, "/api/v1/calendars", new
        {
            name = "  Family ",
            description = "Home",
            color = "#AA00FF",
            defaultTimeZone = "Europe/Berlin",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var calendar = await response.JsonAsync();
        var id = (Guid)calendar["id"]!;
        Assert.Equal($"/api/v1/calendars/{id}", response.Headers.Location!.OriginalString);
        Assert.Equal("Family", (string?)calendar["name"]);
        Assert.Equal("Home", (string?)calendar["description"]);
        Assert.Equal("#aa00ff", (string?)calendar["color"]);
        Assert.Equal("Europe/Berlin", (string?)calendar["defaultTimeZone"]);
        Assert.Equal("user", (string?)calendar["owner"]!["type"]);
        Assert.Equal(_olgaId, (Guid)calendar["owner"]!["id"]!);
        Assert.Equal("owner", (string?)calendar["myLevel"]);
        Assert.Null(calendar["groupRoleDefaults"]);
        Assert.True((bool)calendar["creatorsManageOwnEvents"]!);
        Assert.False((bool)calendar["creatorsMayShareExternally"]!);
        Assert.False((bool)calendar["frozen"]!);

        // The ETag of the creation is the one GET returns.
        Assert.Equal(response.Headers.ETag!.ToString(), (await _olga.GetCalendarAsync(id)).ETag);

        var audit = Assert.Single(await _host.AuditEventsAsync("calendar", id));
        Assert.Equal("calendar.created", audit.Action);
        Assert.Equal($"user:{_olgaId}", (string?)JsonNode.Parse(audit.After!)!["owner"]);
        Assert.Equal(_olgaId, audit.SubjectId);
    }

    [Fact]
    public async Task Default_color_and_settings_apply()
    {
        var id = await _olga.CreateCalendarAsync();
        var (body, _) = await _olga.GetCalendarAsync(id);

        Assert.Equal("#4f46e5", (string?)body["color"]);
        Assert.Null((string?)body["description"]);
    }

    [Theory]
    [InlineData("""{"name":"   ","defaultTimeZone":"Europe/Berlin"}""", "name", ErrorCodes.ValidationFailed)]
    [InlineData("""{"name":"X","defaultTimeZone":"Europe/Atlantis"}""", "defaultTimeZone", ErrorCodes.TimeZoneInvalid)]
    [InlineData("""{"name":"X","defaultTimeZone":"Europe/Berlin","color":"red"}""", "color", ErrorCodes.ValidationFailed)]
    [InlineData("""{"name":"X","defaultTimeZone":"Europe/Berlin","groupRoleDefaults":{"member":"read"}}""", "groupRoleDefaults", ErrorCodes.ValidationFailed)]
    public async Task Invalid_values_are_problems_with_the_field(string json, string field, string code)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/calendars", UriKind.Relative))
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        using var response = await _olga.SendAsync(request, Ct);

        var status = code == ErrorCodes.TimeZoneInvalid ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.BadRequest;
        var problem = await ProblemResponse.AssertProblemAsync(response, status, code);
        Assert.NotNull(problem["errors"]![field]);
    }

    [Fact]
    public async Task Group_admins_create_group_calendars_and_members_cannot()
    {
        var groupId = await _olga.CreateGroupAsync("FC Lions");
        var (_, adam) = await MemberAsync(groupId, "adam", GroupRole.Admin);
        var (_, mia) = await MemberAsync(groupId, "mia", GroupRole.Member);
        var (_, eve) = await PersonAsync("eve");
        var body = new { name = "Club", defaultTimeZone = "Europe/Berlin", groupId };

        using var byMember = await mia.SendJsonAsync(HttpMethod.Post, "/api/v1/calendars", body);
        var problem = await ProblemResponse.AssertProblemAsync(byMember, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);
        Assert.Equal("admin", (string?)problem["required"]);
        Assert.Equal("member", (string?)problem["actual"]);

        using var byStranger = await eve.SendJsonAsync(HttpMethod.Post, "/api/v1/calendars", body);
        await ProblemResponse.AssertProblemAsync(byStranger, HttpStatusCode.NotFound, ErrorCodes.NotFound);

        using var byAdmin = await adam.SendJsonAsync(HttpMethod.Post, "/api/v1/calendars", body);
        Assert.Equal(HttpStatusCode.Created, byAdmin.StatusCode);
        var calendar = await byAdmin.JsonAsync();
        var id = (Guid)calendar["id"]!;
        Assert.Equal("manage", (string?)calendar["myLevel"]);
        Assert.Equal("group", (string?)calendar["owner"]!["type"]);
        Assert.Equal(groupId, (Guid)calendar["owner"]!["id"]!);
        Assert.Equal("manage", (string?)calendar["groupRoleDefaults"]!["admin"]);
        Assert.Equal("contribute", (string?)calendar["groupRoleDefaults"]!["member"]);
        Assert.Equal("read", (string?)calendar["groupRoleDefaults"]!["viewer"]);

        // The group's role-owner owns it; members see it with their role default; strangers don't.
        Assert.Equal("owner", (string?)(await _olga.GetCalendarAsync(id)).Body["myLevel"]);
        Assert.Equal("contribute", (string?)(await mia.GetCalendarAsync(id)).Body["myLevel"]);
        using var hidden = await eve.GetAsync(new Uri($"/api/v1/calendars/{id}", UriKind.Relative), Ct);
        await ProblemResponse.AssertProblemAsync(hidden, HttpStatusCode.NotFound, ErrorCodes.NotFound);

        // The group's billing owner is the plan subject.
        Assert.Equal(_olgaId, Assert.Single(await _host.AuditEventsAsync("calendar", id)).SubjectId);
    }

    [Fact]
    public async Task Role_defaults_can_be_chosen_on_creation_but_not_to_lock_out_the_creator()
    {
        var groupId = await _olga.CreateGroupAsync();
        var (_, adam) = await MemberAsync(groupId, "adam", GroupRole.Admin);

        var id = await _olga.CreateCalendarAsync("Fixtures", groupId, new { member = "read", viewer = "free_busy" });
        var (body, _) = await _olga.GetCalendarAsync(id);
        Assert.Equal("read", (string?)body["groupRoleDefaults"]!["member"]);
        Assert.Equal("free_busy", (string?)body["groupRoleDefaults"]!["viewer"]);

        using var lockout = await adam.SendJsonAsync(HttpMethod.Post, "/api/v1/calendars", new { name = "X", defaultTimeZone = "UTC", groupId, groupRoleDefaults = new { admin = "read" } });
        await ProblemResponse.AssertProblemAsync(lockout, HttpStatusCode.Conflict, ErrorCodes.PermissionSelfLockout);

        using var invalid = await _olga.SendJsonAsync(HttpMethod.Post, "/api/v1/calendars", new { name = "X", defaultTimeZone = "UTC", groupId, groupRoleDefaults = new { admin = "owner" } });
        var problem = await ProblemResponse.AssertProblemAsync(invalid, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.NotNull(problem["errors"]!["groupRoleDefaults.admin"]);
    }

    [Fact]
    public async Task List_shows_owned_and_group_calendars_with_my_level_in_pages()
    {
        var groupId = await _olga.CreateGroupAsync();
        var (vicId, vic) = await MemberAsync(groupId, "vic", GroupRole.Viewer);
        var personal = await vic.CreateCalendarAsync("Vic's");
        var club = await _olga.CreateCalendarAsync("Club", groupId);
        var hiddenFromViewers = await _olga.CreateCalendarAsync("Board", groupId, new { viewer = "none" });
        var fixtures = await _olga.CreateCalendarAsync("Fixtures", groupId, new { viewer = "free_busy" });
        await _olga.CreateCalendarAsync("Olga's own");

        // The "Board" candidate is skipped and the page still fills up from the next candidates.
        using var first = await vic.GetAsync(new Uri("/api/v1/calendars?limit=2", UriKind.Relative), Ct);
        var page1 = await first.JsonAsync();
        Assert.Equal([personal, club], page1["items"]!.AsArray().Select(c => (Guid)c!["id"]!));
        var cursor = (string?)page1["nextCursor"];
        Assert.NotNull(cursor);
        using var second = await vic.GetAsync(new Uri($"/api/v1/calendars?limit=2&cursor={cursor}", UriKind.Relative), Ct);
        var page2 = await second.JsonAsync();
        Assert.Equal(fixtures, (Guid)Assert.Single(page2["items"]!.AsArray())!["id"]!);
        Assert.Null((string?)page2["nextCursor"]);

        var levels = await vic.MyLevelsAsync(limit: 1);
        Assert.Equal(
            new Dictionary<Guid, string> { [personal] = "owner", [club] = "read", [fixtures] = "free_busy" },
            levels);
        Assert.DoesNotContain(hiddenFromViewers, levels.Keys);
        Assert.Equal(4, (await _olga.MyLevelsAsync()).Count);
        Assert.NotEqual(Guid.Empty, vicId);
    }

    [Theory]
    [InlineData("limit=0", "limit")]
    [InlineData("limit=201", "limit")]
    [InlineData("cursor=nope", "cursor")]
    public async Task Invalid_paging_parameters_are_validation_problems(string query, string field)
    {
        using var response = await _olga.GetAsync(new Uri("/api/v1/calendars?" + query, UriKind.Relative), Ct);

        var problem = await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.NotNull(problem["errors"]![field]);
    }

    [Fact]
    public async Task Hidden_and_unknown_calendars_are_indistinguishable()
    {
        var id = await _olga.CreateCalendarAsync();
        var (_, eve) = await PersonAsync("eve");

        using var hidden = await eve.GetAsync(new Uri($"/api/v1/calendars/{id}", UriKind.Relative), Ct);
        using var unknown = await eve.GetAsync(new Uri($"/api/v1/calendars/{Guid.CreateVersion7()}", UriKind.Relative), Ct);

        var hiddenProblem = await ProblemResponse.AssertProblemAsync(hidden, HttpStatusCode.NotFound, ErrorCodes.NotFound);
        var unknownProblem = await ProblemResponse.AssertProblemAsync(unknown, HttpStatusCode.NotFound, ErrorCodes.NotFound);
        Assert.Equal((string?)unknownProblem["detail"], (string?)hiddenProblem["detail"]);
    }

    [Fact]
    public async Task Update_is_a_merge_patch_with_if_match_and_bumps_the_acl_version_for_permission_settings()
    {
        var id = await _olga.CreateCalendarAsync("Training");
        var (_, etag) = await _olga.GetCalendarAsync(id);
        var path = $"/api/v1/calendars/{id}";
        var acl = await _host.CalendarAclVersionAsync(id);

        using var missing = await _olga.SendJsonAsync(HttpMethod.Patch, path, new { name = "X" });
        await ProblemResponse.AssertProblemAsync(missing, HttpStatusCode.PreconditionRequired, ErrorCodes.PreconditionRequired);

        using var renamed = await _olga.SendJsonAsync(HttpMethod.Patch, path, new { name = "Matches", color = "#00FF00", defaultTimeZone = "America/New_York", description = "Season" }, etag, "application/merge-patch+json");
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var body = await renamed.JsonAsync();
        Assert.Equal("Matches", (string?)body["name"]);
        Assert.Equal("#00ff00", (string?)body["color"]);
        Assert.Equal("America/New_York", (string?)body["defaultTimeZone"]);
        Assert.Equal("Season", (string?)body["description"]);
        var newEtag = renamed.Headers.ETag!.ToString();
        Assert.NotEqual(etag, newEtag);
        Assert.Equal(newEtag, (await _olga.GetCalendarAsync(id)).ETag);
        Assert.Equal(acl, await _host.CalendarAclVersionAsync(id)); // not ACL-relevant

        using var stale = await _olga.SendJsonAsync(HttpMethod.Patch, path, new { name = "Y" }, etag);
        await ProblemResponse.AssertProblemAsync(stale, HttpStatusCode.PreconditionFailed, ErrorCodes.PreconditionFailed);

        using var settings = await _olga.SendJsonAsync(HttpMethod.Patch, path, new { creatorsManageOwnEvents = false, creatorsMayShareExternally = true, description = "" }, newEtag);
        var settingsBody = await settings.JsonAsync();
        Assert.False((bool)settingsBody["creatorsManageOwnEvents"]!);
        Assert.True((bool)settingsBody["creatorsMayShareExternally"]!);
        Assert.Null((string?)settingsBody["description"]);
        Assert.Equal("Matches", (string?)settingsBody["name"]);
        Assert.Equal(acl + 1, await _host.CalendarAclVersionAsync(id));

        using var badZone = await _olga.SendJsonAsync(HttpMethod.Patch, path, new { defaultTimeZone = "Mars/Olympus" }, "*");
        await ProblemResponse.AssertProblemAsync(badZone, HttpStatusCode.UnprocessableEntity, ErrorCodes.TimeZoneInvalid);

        using var personalDefaults = await _olga.SendJsonAsync(HttpMethod.Patch, path, new { groupRoleDefaults = new { member = "read" } }, "*");
        await ProblemResponse.AssertProblemAsync(personalDefaults, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);

        var audit = (await _host.AuditEventsAsync("calendar", id)).Where(e => e.Action == "calendar.updated").ToList();
        Assert.Equal(2, audit.Count);
        Assert.Equal("Training", (string?)JsonNode.Parse(audit[0].Before!)!["name"]);
        Assert.Equal("Matches", (string?)JsonNode.Parse(audit[0].After!)!["name"]);
    }

    [Fact]
    public async Task Managers_change_role_defaults_but_not_their_own_manage_level()
    {
        var groupId = await _olga.CreateGroupAsync();
        var (_, adam) = await MemberAsync(groupId, "adam", GroupRole.Admin);
        var (_, mia) = await MemberAsync(groupId, "mia", GroupRole.Member);
        var id = await _olga.CreateCalendarAsync("Club", groupId);
        var path = $"/api/v1/calendars/{id}";
        var acl = await _host.CalendarAclVersionAsync(id);

        using var byMember = await mia.SendJsonAsync(HttpMethod.Patch, path, new { groupRoleDefaults = new { member = "edit" } }, "*");
        var forbidden = await ProblemResponse.AssertProblemAsync(byMember, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);
        Assert.Equal("manage", (string?)forbidden["required"]);
        Assert.Equal("contribute", (string?)forbidden["actual"]);

        using var curated = await adam.SendJsonAsync(HttpMethod.Patch, path, new { groupRoleDefaults = new { member = "read" } }, "*");
        Assert.Equal(HttpStatusCode.OK, curated.StatusCode);
        Assert.Equal("manage", (string?)(await curated.JsonAsync())["groupRoleDefaults"]!["admin"]);
        Assert.Equal("read", (string?)(await mia.GetCalendarAsync(id)).Body["myLevel"]);
        Assert.Equal(acl + 1, await _host.CalendarAclVersionAsync(id));

        using var lockout = await adam.SendJsonAsync(HttpMethod.Patch, path, new { groupRoleDefaults = new { admin = "edit" } }, "*");
        await ProblemResponse.AssertProblemAsync(lockout, HttpStatusCode.Conflict, ErrorCodes.PermissionSelfLockout);

        // The owner may lower the admins (owners are never affected by role defaults).
        using var byOwner = await _olga.SendJsonAsync(HttpMethod.Patch, path, new { groupRoleDefaults = new { admin = "edit" } }, "*");
        Assert.Equal(HttpStatusCode.OK, byOwner.StatusCode);
        Assert.Equal("edit", (string?)(await adam.GetCalendarAsync(id)).Body["myLevel"]);

        var audit = (await _host.AuditEventsAsync("calendar", id)).Last(e => e.Action == "calendar.updated");
        Assert.Equal("manage", (string?)JsonNode.Parse(audit.Before!)!["aclRelevant"]!["groupRoleDefaults"]!["admin"]);
        Assert.Equal("edit", (string?)JsonNode.Parse(audit.After!)!["aclRelevant"]!["groupRoleDefaults"]!["admin"]);
    }

    [Fact]
    public async Task Frozen_calendars_refuse_changes_but_can_be_deleted()
    {
        var id = await _olga.CreateCalendarAsync();
        await _host.QueryAsync(db => db.Calendars.Where(c => c.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.FrozenAt, SystemClock.Instance.GetCurrentInstant()), Ct));

        Assert.True((bool)(await _olga.GetCalendarAsync(id)).Body["frozen"]!);
        using var update = await _olga.SendJsonAsync(HttpMethod.Patch, $"/api/v1/calendars/{id}", new { name = "X" }, "*");
        await ProblemResponse.AssertProblemAsync(update, HttpStatusCode.Conflict, ErrorCodes.CalendarFrozen);

        using var delete = await _olga.SendJsonAsync(HttpMethod.Delete, $"/api/v1/calendars/{id}", ifMatch: "*");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task Only_owners_delete()
    {
        var groupId = await _olga.CreateGroupAsync();
        var (_, adam) = await MemberAsync(groupId, "adam", GroupRole.Admin);
        var id = await adam.CreateCalendarAsync("Club", groupId);
        var path = $"/api/v1/calendars/{id}";

        using var byAdmin = await adam.SendJsonAsync(HttpMethod.Delete, path, ifMatch: "*");
        var problem = await ProblemResponse.AssertProblemAsync(byAdmin, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);
        Assert.Equal("owner", (string?)problem["required"]);
        Assert.Equal("manage", (string?)problem["actual"]);

        using var withoutIfMatch = await _olga.SendJsonAsync(HttpMethod.Delete, path);
        await ProblemResponse.AssertProblemAsync(withoutIfMatch, HttpStatusCode.PreconditionRequired, ErrorCodes.PreconditionRequired);
        using var stale = await _olga.SendJsonAsync(HttpMethod.Delete, path, ifMatch: "\"stale\"");
        await ProblemResponse.AssertProblemAsync(stale, HttpStatusCode.PreconditionFailed, ErrorCodes.PreconditionFailed);

        var (_, etag) = await _olga.GetCalendarAsync(id);
        using var deleted = await _olga.SendJsonAsync(HttpMethod.Delete, path, ifMatch: etag);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var gone = await _olga.GetAsync(new Uri(path, UriKind.Relative), Ct);
        await ProblemResponse.AssertProblemAsync(gone, HttpStatusCode.NotFound, ErrorCodes.NotFound);
        var audit = (await _host.AuditEventsAsync("calendar", id)).Single(e => e.Action == "calendar.deleted");
        Assert.Equal("Club", (string?)JsonNode.Parse(audit.Before!)!["name"]);
        Assert.Null(audit.After);
        Assert.Equal(_olgaId, audit.SubjectId);
    }

    [Fact]
    public async Task The_database_requires_exactly_one_owner()
    {
        var exception = await Assert.ThrowsAnyAsync<DbUpdateException>(() => _host.QueryAsync(async db =>
        {
            db.Calendars.Add(new Core.Calendars.Calendar { Id = Guid.CreateVersion7(), Name = "Nobody's", DefaultTimeZone = "UTC" });
            return await db.SaveChangesAsync(Ct);
        }));

        Assert.Contains("ck_calendars_one_owner", exception.InnerException!.Message, StringComparison.Ordinal);
    }

    private async Task<(Guid Id, HttpClient Client)> PersonAsync(string name)
    {
        var email = ApiTestHost.UniqueEmail(name);
        var id = await _host.CreateUserAsync(email, displayName: name);
        var client = await _host.SignedInClientAsync(email);
        _clients.Add(client);
        return (id, client);
    }

    private async Task<(Guid Id, HttpClient Client)> MemberAsync(Guid groupId, string name, GroupRole role)
    {
        var person = await PersonAsync(name);
        await _host.AddMemberAsync(groupId, person.Id, role);
        return person;
    }
}
