using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.Core.Permissions;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Groups;
using SCalenderPlus.IntegrationTests.Infrastructure;
using SCalenderPlus.IntegrationTests.Problems;

namespace SCalenderPlus.IntegrationTests.Calendars;

/// <summary>Issue #43: group lifecycle rules for calendars (permissions.md §4.6, data-model.md §2).</summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class GroupLifecycleTests(PostgresFixture postgres) : IAsyncLifetime
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
    public async Task A_group_that_owns_calendars_cannot_be_deleted()
    {
        var groupId = await _olga.CreateGroupAsync("Lions");
        var calendarId = await _olga.CreateCalendarAsync("Club", groupId);
        var path = $"/api/v1/groups/{groupId}";

        using var refused = await _olga.SendJsonAsync(HttpMethod.Delete, path, ifMatch: "*");

        var problem = await ProblemResponse.AssertProblemAsync(refused, HttpStatusCode.Conflict, ErrorCodes.GroupHasCalendars);
        Assert.Equal(1, (int)problem["calendarCount"]!);
        Assert.Equal("Lions", (string?)(await _olga.GetGroupAsync(groupId)).Body["name"]);
        Assert.DoesNotContain(await _host.AuditEventsAsync("group", groupId), e => e.Action == "group.deleted");

        // Once the calendar is gone, the group can go.
        using var calendarDeleted = await _olga.SendJsonAsync(HttpMethod.Delete, $"/api/v1/calendars/{calendarId}", ifMatch: "*");
        Assert.Equal(HttpStatusCode.NoContent, calendarDeleted.StatusCode);
        using var deleted = await _olga.SendJsonAsync(HttpMethod.Delete, path, ifMatch: "*");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Admins_still_get_403_before_the_calendar_rule()
    {
        var groupId = await _olga.CreateGroupAsync("Lions");
        await _olga.CreateCalendarAsync("Club", groupId);
        var (adamId, adam) = await PersonAsync("adam");
        await _host.AddMemberAsync(groupId, adamId, GroupRole.Admin);

        using var byAdmin = await adam.SendJsonAsync(HttpMethod.Delete, $"/api/v1/groups/{groupId}", ifMatch: "*");

        await ProblemResponse.AssertProblemAsync(byAdmin, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);
    }

    [Fact]
    public async Task The_database_restricts_deleting_a_group_that_owns_calendars()
    {
        var groupId = await _olga.CreateGroupAsync("Lions");
        await _olga.CreateCalendarAsync("Club", groupId);

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => _host.QueryAsync(async db =>
        {
            await db.GroupMembers.Where(m => m.GroupId == groupId).ExecuteDeleteAsync(Ct);
            return await db.Groups.Where(g => g.Id == groupId).ExecuteDeleteAsync(Ct);
        }));

        Assert.Contains("fk_calendars_groups_owner_group_id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_a_group_removes_its_grants_and_bumps_the_calendars()
    {
        var groupId = await _olga.CreateGroupAsync("Partners");
        var (miaId, mia) = await PersonAsync("mia");
        await _host.AddMemberAsync(groupId, miaId, GroupRole.Member);
        var first = await _olga.CreateCalendarAsync("First");
        var second = await _olga.CreateCalendarAsync("Second");
        await GrantGroupAsync(first, groupId, "read");
        await GrantGroupAsync(second, groupId, "edit", "member");
        using var keptGrant = await _olga.SendJsonAsync(HttpMethod.Post, $"/api/v1/calendars/{first}/grants", new { principal = new { type = "user", id = miaId }, level = "free_busy" });
        Assert.Equal(HttpStatusCode.Created, keptGrant.StatusCode);
        Assert.Equal(new Dictionary<Guid, string> { [first] = "read", [second] = "edit" }, await mia.MyLevelsAsync());
        var firstAcl = await _host.CalendarAclVersionAsync(first);
        var secondAcl = await _host.CalendarAclVersionAsync(second);
        var miaAcl = await _host.AclVersionAsync(miaId);

        using var deleted = await _olga.SendJsonAsync(HttpMethod.Delete, $"/api/v1/groups/{groupId}", ifMatch: "*");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(0, await _host.QueryAsync(db => db.CalendarGrants.CountAsync(g => g.PrincipalType == PrincipalType.Group && g.PrincipalId == groupId, Ct)));
        Assert.Equal(new Dictionary<Guid, string> { [first] = "free_busy" }, await mia.MyLevelsAsync()); // her own grant stays
        Assert.Equal(firstAcl + 1, await _host.CalendarAclVersionAsync(first));
        Assert.Equal(secondAcl + 1, await _host.CalendarAclVersionAsync(second));
        Assert.Equal(miaAcl + 1, await _host.AclVersionAsync(miaId));

        var audit = Assert.Single(await _host.AuditEventsAsync("calendar", second), e => e.Action == "calendar.grant.removed_with_group");
        Assert.Equal($"group:{groupId}[member]", (string?)JsonNode.Parse(audit.Before!)!["principal"]);
        Assert.Equal(_olgaId, audit.SubjectId);
        Assert.Single(await _host.AuditEventsAsync("calendar", first), e => e.Action == "calendar.grant.removed_with_group");
    }

    [Fact]
    public async Task Grant_changes_naming_a_group_bump_its_acl_version()
    {
        var groupId = await _olga.CreateGroupAsync("Partners");
        var calendarId = await _olga.CreateCalendarAsync("Fixtures");
        var version = await _host.GroupAclVersionAsync(groupId);

        var grantId = await GrantGroupAsync(calendarId, groupId, "read");
        Assert.Equal(version + 1, await _host.GroupAclVersionAsync(groupId));

        using var changed = await _olga.SendJsonAsync(HttpMethod.Patch, $"/api/v1/calendars/{calendarId}/grants/{grantId}", new { level = "edit" }, "*");
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(version + 2, await _host.GroupAclVersionAsync(groupId));

        using var removed = await _olga.SendJsonAsync(HttpMethod.Delete, $"/api/v1/calendars/{calendarId}/grants/{grantId}", ifMatch: "*");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(version + 3, await _host.GroupAclVersionAsync(groupId));
    }

    private async Task<Guid> GrantGroupAsync(Guid calendarId, Guid groupId, string level, string minRole = "viewer")
    {
        using var response = await _olga.SendJsonAsync(HttpMethod.Post, $"/api/v1/calendars/{calendarId}/grants", new { principal = new { type = "group", id = groupId, minRole }, level });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (Guid)(await response.JsonAsync())["id"]!;
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
