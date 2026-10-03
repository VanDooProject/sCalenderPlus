using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Infrastructure;
using SCalenderPlus.IntegrationTests.Problems;

namespace SCalenderPlus.IntegrationTests.Groups;

/// <summary>Issue #34: groups CRUD with roles.</summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class GroupTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiTestHost _host = null!;
    private HttpClient _owner = null!;
    private Guid _ownerId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await ApiTestHost.StartAsync(postgres);
        var email = ApiTestHost.UniqueEmail("olga");
        _ownerId = await _host.CreateUserAsync(email, displayName: "Olga");
        _owner = await _host.SignedInClientAsync(email);
    }

    public async ValueTask DisposeAsync()
    {
        _owner.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Creator_becomes_owner_and_billing_owner()
    {
        var aclBefore = await _host.AclVersionAsync(_ownerId);

        using var response = await _owner.SendJsonAsync(HttpMethod.Post, "/api/v1/groups", new { name = "  FC Lions ", description = "Club" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var group = await response.JsonAsync();
        var id = (Guid)group["id"]!;
        Assert.Equal($"/api/v1/groups/{id}", response.Headers.Location!.OriginalString);
        Assert.NotNull(response.Headers.ETag);
        Assert.Equal("FC Lions", (string?)group["name"]);
        Assert.Equal("Club", (string?)group["description"]);
        Assert.Equal("owner", (string?)group["myRole"]);
        Assert.Equal(_ownerId, (Guid)group["billingOwnerId"]!);
        Assert.Equal(1, (int)group["memberCount"]!);
        Assert.Equal("all_members", (string?)group["memberListVisibility"]);
        Assert.False((bool)group["frozen"]!);

        Assert.Equal(GroupRole.Owner, await _host.RoleOfAsync(id, _ownerId));
        Assert.Equal(aclBefore + 1, await _host.AclVersionAsync(_ownerId));
        var audit = Assert.Single(await _host.AuditEventsAsync("group", id));
        Assert.Equal("group.created", audit.Action);
        Assert.Equal("FC Lions", (string?)JsonNode.Parse(audit.After!)!["name"]);
        Assert.Equal(_ownerId, audit.ActorUserId);
        Assert.Equal(_ownerId, audit.SubjectId);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public async Task Blank_name_is_a_validation_problem(string name)
    {
        using var response = await _owner.SendJsonAsync(HttpMethod.Post, "/api/v1/groups", new { name });

        var problem = await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.NotNull(problem["errors"]!["name"]);
    }

    [Fact]
    public async Task List_shows_my_groups_with_my_role_in_pages()
    {
        var mine = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            mine.Add(await _owner.CreateGroupAsync($"Group {i}"));
        }

        var viewerEmail = ApiTestHost.UniqueEmail("vic");
        var viewerId = await _host.CreateUserAsync(viewerEmail);
        await _host.AddMemberAsync(mine[1], viewerId, GroupRole.Viewer);
        using var viewer = await _host.SignedInClientAsync(viewerEmail);

        using var first = await _owner.GetAsync(new Uri("/api/v1/groups?limit=2", UriKind.Relative), Ct);
        var page1 = await first.JsonAsync();
        Assert.Equal(mine.Take(2), page1["items"]!.AsArray().Select(g => (Guid)g!["id"]!));
        var cursor = (string?)page1["nextCursor"];
        Assert.NotNull(cursor);

        using var second = await _owner.GetAsync(new Uri($"/api/v1/groups?limit=2&cursor={cursor}", UriKind.Relative), Ct);
        var page2 = await second.JsonAsync();
        Assert.Equal(mine[2], (Guid)Assert.Single(page2["items"]!.AsArray())!["id"]!);
        Assert.Null((string?)page2["nextCursor"]);

        using var viewerList = await viewer.GetAsync(new Uri("/api/v1/groups", UriKind.Relative), Ct);
        var only = Assert.Single((await viewerList.JsonAsync())["items"]!.AsArray())!;
        Assert.Equal(mine[1], (Guid)only["id"]!);
        Assert.Equal("viewer", (string?)only["myRole"]);
        Assert.Equal(2, (int)only["memberCount"]!);
    }

    [Theory]
    [InlineData("limit=0", "limit")]
    [InlineData("limit=201", "limit")]
    [InlineData("cursor=nope", "cursor")]
    public async Task Invalid_paging_parameters_are_validation_problems(string query, string field)
    {
        using var response = await _owner.GetAsync(new Uri("/api/v1/groups?" + query, UriKind.Relative), Ct);

        var problem = await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.NotNull(problem["errors"]![field]);
    }

    [Fact]
    public async Task Non_members_get_404_and_unknown_groups_too()
    {
        var id = await _owner.CreateGroupAsync();
        var strangerEmail = ApiTestHost.UniqueEmail("eve");
        await _host.CreateUserAsync(strangerEmail);
        using var stranger = await _host.SignedInClientAsync(strangerEmail);

        using var hidden = await stranger.GetAsync(new Uri($"/api/v1/groups/{id}", UriKind.Relative), Ct);
        using var unknown = await stranger.GetAsync(new Uri($"/api/v1/groups/{Guid.CreateVersion7()}", UriKind.Relative), Ct);

        var hiddenProblem = await ProblemResponse.AssertProblemAsync(hidden, HttpStatusCode.NotFound, ErrorCodes.NotFound);
        var unknownProblem = await ProblemResponse.AssertProblemAsync(unknown, HttpStatusCode.NotFound, ErrorCodes.NotFound);
        Assert.Equal((string?)unknownProblem["detail"], (string?)hiddenProblem["detail"]);
    }

    [Fact]
    public async Task Update_is_a_merge_patch_with_if_match()
    {
        var id = await _owner.CreateGroupAsync("Lions");
        var (_, etag) = await _owner.GetGroupAsync(id);
        var path = $"/api/v1/groups/{id}";

        using var missing = await _owner.SendJsonAsync(HttpMethod.Patch, path, new { name = "X" });
        await ProblemResponse.AssertProblemAsync(missing, HttpStatusCode.PreconditionRequired, ErrorCodes.PreconditionRequired);

        using var updated = await _owner.SendJsonAsync(HttpMethod.Patch, path, new { name = "FC Lions", description = "Since 1920", memberListVisibility = "members_and_above" }, etag, "application/merge-patch+json");
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = await updated.JsonAsync();
        Assert.Equal("FC Lions", (string?)body["name"]);
        Assert.Equal("Since 1920", (string?)body["description"]);
        Assert.Equal("members_and_above", (string?)body["memberListVisibility"]);
        var newEtag = updated.Headers.ETag!.ToString();
        Assert.NotEqual(etag, newEtag);
        Assert.Equal(newEtag, (await _owner.GetGroupAsync(id)).ETag);

        using var stale = await _owner.SendJsonAsync(HttpMethod.Patch, path, new { name = "Y" }, etag);
        await ProblemResponse.AssertProblemAsync(stale, HttpStatusCode.PreconditionFailed, ErrorCodes.PreconditionFailed);

        // An empty description removes it; absent members stay.
        using var cleared = await _owner.SendJsonAsync(HttpMethod.Patch, path, new { description = "" }, newEtag);
        var clearedBody = await cleared.JsonAsync();
        Assert.Null((string?)clearedBody["description"]);
        Assert.Equal("FC Lions", (string?)clearedBody["name"]);

        using var invalid = await _owner.SendJsonAsync(HttpMethod.Patch, path, new { memberListVisibility = "nobody" }, "*");
        await ProblemResponse.AssertProblemAsync(invalid, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);

        var audit = (await _host.AuditEventsAsync("group", id)).Where(e => e.Action == "group.updated").ToList();
        Assert.Equal(2, audit.Count);
        Assert.Equal("Lions", (string?)JsonNode.Parse(audit[0].Before!)!["name"]);
        Assert.Equal("FC Lions", (string?)JsonNode.Parse(audit[0].After!)!["name"]);
    }

    [Theory]
    [InlineData(GroupRole.Admin, HttpStatusCode.OK)]
    [InlineData(GroupRole.Member, HttpStatusCode.Forbidden)]
    [InlineData(GroupRole.Viewer, HttpStatusCode.Forbidden)]
    public async Task Admins_and_owners_update_settings(GroupRole role, HttpStatusCode expected)
    {
        var id = await _owner.CreateGroupAsync();
        var email = ApiTestHost.UniqueEmail("adam");
        await _host.AddMemberAsync(id, await _host.CreateUserAsync(email), role);
        using var client = await _host.SignedInClientAsync(email);

        using var response = await client.SendJsonAsync(HttpMethod.Patch, $"/api/v1/groups/{id}", new { name = "Renamed" }, "*");

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.Forbidden)
        {
            var problem = await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);
            Assert.Equal("admin", (string?)problem["required"]);
            Assert.Equal(GroupRoles.Format(role), (string?)problem["actual"]);
        }
    }

    [Fact]
    public async Task Only_owners_delete_and_everything_goes_with_the_group()
    {
        var id = await _owner.CreateGroupAsync();
        var adminEmail = ApiTestHost.UniqueEmail("adam");
        var adminId = await _host.CreateUserAsync(adminEmail);
        await _host.AddMemberAsync(id, adminId, GroupRole.Admin);
        using var admin = await _host.SignedInClientAsync(adminEmail);
        var path = $"/api/v1/groups/{id}";

        using var byAdmin = await admin.SendJsonAsync(HttpMethod.Delete, path, ifMatch: "*");
        await ProblemResponse.AssertProblemAsync(byAdmin, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);

        using var withoutIfMatch = await _owner.SendJsonAsync(HttpMethod.Delete, path);
        await ProblemResponse.AssertProblemAsync(withoutIfMatch, HttpStatusCode.PreconditionRequired, ErrorCodes.PreconditionRequired);

        var adminAcl = await _host.AclVersionAsync(adminId);
        var (_, etag) = await _owner.GetGroupAsync(id);
        using var deleted = await _owner.SendJsonAsync(HttpMethod.Delete, path, ifMatch: etag);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var gone = await _owner.GetAsync(new Uri(path, UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal(0, await _host.QueryAsync(db => db.GroupMembers.CountAsync(m => m.GroupId == id, Ct)));
        Assert.Equal(adminAcl + 1, await _host.AclVersionAsync(adminId));
        var audit = (await _host.AuditEventsAsync("group", id)).Single(e => e.Action == "group.deleted");
        Assert.Equal(2, (int)JsonNode.Parse(audit.Before!)!["memberCount"]!);
        Assert.Null(audit.After);
    }
}
