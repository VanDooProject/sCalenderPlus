using System.Net;
using System.Text.Json.Nodes;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Application.Groups;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Infrastructure;
using SCalenderPlus.IntegrationTests.Problems;

namespace SCalenderPlus.IntegrationTests.Groups;

/// <summary>Issue #35: group membership management (permissions.md §6.1).</summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class MembershipTests(GroupHostFixture fixture) : IClassFixture<GroupHostFixture>, IAsyncLifetime
{
    private readonly GroupHostFixture.RecordingObserver _observer = fixture.Observer;
    private readonly ApiTestHost _host = fixture.Host;
    private Guid _groupId;
    private readonly Dictionary<string, (Guid Id, HttpClient Client)> _people = new(StringComparer.Ordinal);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var (olgaId, olga) = await PersonAsync("olga");
        _groupId = await olga.CreateGroupAsync("Lions");
        _people["olga"] = (olgaId, olga);
        foreach (var (name, role) in new[] { ("otto", GroupRole.Owner), ("adam", GroupRole.Admin), ("anna", GroupRole.Admin), ("mia", GroupRole.Member), ("vic", GroupRole.Viewer) })
        {
            var person = await PersonAsync(name);
            await _host.AddMemberAsync(_groupId, person.Id, role);
            _people[name] = person;
        }

        _observer.Changes.Clear();
    }

    public ValueTask DisposeAsync()
    {
        foreach (var (_, client) in _people.Values)
        {
            client.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private Guid Id(string name) => _people[name].Id;

    private HttpClient As(string name) => _people[name].Client;

    private string MemberPath(string name) => $"/api/v1/groups/{_groupId}/members/{Id(name)}";

    private Task<HttpResponseMessage> ChangeRoleAsync(string actor, string target, string role, string ifMatch = "*", string query = "") =>
        As(actor).SendJsonAsync(HttpMethod.Patch, MemberPath(target) + query, new { role }, ifMatch);

    private Task<HttpResponseMessage> RemoveAsync(string actor, string target, string ifMatch = "*", string query = "") =>
        As(actor).SendJsonAsync(HttpMethod.Delete, MemberPath(target) + query, ifMatch: ifMatch);

    [Fact]
    public async Task Member_list_shows_roles_and_the_billing_owner_and_emails_only_to_admins()
    {
        using var byAdmin = await As("adam").GetAsync(new Uri($"/api/v1/groups/{_groupId}/members", UriKind.Relative), Ct);
        using var byMember = await As("mia").GetAsync(new Uri($"/api/v1/groups/{_groupId}/members?limit=2", UriKind.Relative), Ct);

        var all = (await byAdmin.JsonAsync())["items"]!.AsArray();
        Assert.Equal(6, all.Count);
        var olga = all.Single(m => (Guid)m!["userId"]! == Id("olga"))!;
        Assert.Equal("owner", (string?)olga["role"]);
        Assert.True((bool)olga["isBillingOwner"]!);
        Assert.Contains("olga-", (string?)olga["email"], StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty((string?)olga["etag"]));
        Assert.Equal("viewer", (string?)all.Single(m => (Guid)m!["userId"]! == Id("vic"))!["role"]);

        var page = await byMember.JsonAsync();
        Assert.Equal(2, page["items"]!.AsArray().Count);
        Assert.NotNull((string?)page["nextCursor"]);
        Assert.All(page["items"]!.AsArray(), m => Assert.Null((string?)m!["email"]));
    }

    [Fact]
    public async Task Viewers_cannot_see_a_hidden_member_list()
    {
        using var hide = await As("olga").SendJsonAsync(HttpMethod.Patch, $"/api/v1/groups/{_groupId}", new { memberListVisibility = "members_and_above" }, "*");
        Assert.Equal(HttpStatusCode.OK, hide.StatusCode);

        using var byViewer = await As("vic").GetAsync(new Uri($"/api/v1/groups/{_groupId}/members", UriKind.Relative), Ct);
        using var byMember = await As("mia").GetAsync(new Uri($"/api/v1/groups/{_groupId}/members", UriKind.Relative), Ct);

        await ProblemResponse.AssertProblemAsync(byViewer, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);
        Assert.Equal(HttpStatusCode.OK, byMember.StatusCode);
    }

    [Fact]
    public async Task Owner_promotes_to_admin_with_audit_acl_bump_and_member_etag()
    {
        var acl = await _host.AclVersionAsync(Id("mia"));
        var etag = await MemberETagAsync("mia");

        using var stale = await ChangeRoleAsync("olga", "mia", "admin", ifMatch: "\"stale\"");
        await ProblemResponse.AssertProblemAsync(stale, HttpStatusCode.PreconditionFailed, ErrorCodes.PreconditionFailed);
        using var missing = await As("olga").SendJsonAsync(HttpMethod.Patch, MemberPath("mia"), new { role = "admin" });
        await ProblemResponse.AssertProblemAsync(missing, HttpStatusCode.PreconditionRequired, ErrorCodes.PreconditionRequired);

        using var response = await ChangeRoleAsync("olga", "mia", "admin", ifMatch: etag);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var member = await response.JsonAsync();
        Assert.Equal("admin", (string?)member["role"]);
        Assert.Equal(response.Headers.ETag!.ToString(), (string?)member["etag"]);
        Assert.Equal(GroupRole.Admin, await _host.RoleOfAsync(_groupId, Id("mia")));
        Assert.Equal(acl + 1, await _host.AclVersionAsync(Id("mia")));
        var audit = (await _host.AuditEventsAsync("group", _groupId)).Single(e => e.Action == "group.member.role_changed");
        Assert.Equal("member", (string?)JsonNode.Parse(audit.Before!)!["role"]);
        Assert.Equal("admin", (string?)JsonNode.Parse(audit.After!)!["role"]);
        Assert.Equal(Id("mia"), (Guid)JsonNode.Parse(audit.After!)!["userId"]!);
        Assert.Equal(Id("olga"), audit.ActorUserId);
        var change = Assert.Single(_observer.Changes);
        Assert.Equal(new MembershipChange(_groupId, Id("mia"), GroupRole.Member, GroupRole.Admin, MembershipChangeKind.RoleChanged, RevokeEventShares: false), change);
    }

    [Fact]
    public async Task Admin_demoting_another_admin_is_forbidden()
    {
        using var response = await ChangeRoleAsync("adam", "anna", "member");

        var problem = await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);
        Assert.Equal("owner", (string?)problem["required"]);
        Assert.Equal("admin", (string?)problem["actual"]);
        Assert.Equal(GroupRole.Admin, await _host.RoleOfAsync(_groupId, Id("anna")));
    }

    [Theory]
    [InlineData("adam", "mia", "admin", HttpStatusCode.Forbidden)] // only owners promote to admin
    [InlineData("adam", "olga", "member", HttpStatusCode.Forbidden)]
    [InlineData("adam", "mia", "viewer", HttpStatusCode.OK)]
    [InlineData("adam", "vic", "member", HttpStatusCode.OK)]
    [InlineData("mia", "vic", "member", HttpStatusCode.Forbidden)]
    [InlineData("vic", "vic", "member", HttpStatusCode.Forbidden)] // no self-promotion
    [InlineData("mia", "mia", "viewer", HttpStatusCode.OK)] // self-demotion
    [InlineData("olga", "otto", "admin", HttpStatusCode.OK)] // owners demote other owners
    public async Task Role_change_rules(string actor, string target, string role, HttpStatusCode expected)
    {
        using var response = await ChangeRoleAsync(actor, target, role);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_role_and_unknown_member_are_rejected()
    {
        using var badRole = await ChangeRoleAsync("olga", "mia", "boss");
        using var unknown = await As("olga").SendJsonAsync(HttpMethod.Patch, $"/api/v1/groups/{_groupId}/members/{Guid.CreateVersion7()}", new { role = "member" }, "*");

        await ProblemResponse.AssertProblemAsync(badRole, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        await ProblemResponse.AssertProblemAsync(unknown, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task The_last_owner_cannot_leave_or_demote_themselves()
    {
        // Otto steps down first; then Olga is the last owner (and billing owner).
        using (var otto = await RemoveAsync("otto", "otto"))
        {
            Assert.Equal(HttpStatusCode.NoContent, otto.StatusCode);
        }

        using var demote = await ChangeRoleAsync("olga", "olga", "admin");
        using var leave = await RemoveAsync("olga", "olga");

        await ProblemResponse.AssertProblemAsync(demote, HttpStatusCode.Conflict, ErrorCodes.LastOwner);
        await ProblemResponse.AssertProblemAsync(leave, HttpStatusCode.Conflict, ErrorCodes.LastOwner);
        Assert.Equal(GroupRole.Owner, await _host.RoleOfAsync(_groupId, Id("olga")));
    }

    [Fact]
    public async Task Concurrent_billing_transfer_and_leaving_never_leave_a_non_owner_paying()
    {
        for (var round = 0; round < 5; round++)
        {
            var transfer = As("olga").SendJsonAsync(HttpMethod.Post, $"/api/v1/groups/{_groupId}/transfer", new { userId = Id("otto") });
            var leave = RemoveAsync("otto", "otto");
            using var transferred = await transfer;
            using var left = await leave;

            // Whatever the interleaving: the billing owner is a member with role owner.
            var billingOwner = await _host.QueryAsync(db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
                db.Groups.Where(g => g.Id == _groupId).Select(g => g.OwnerUserId), Ct));
            Assert.Equal(GroupRole.Owner, await _host.RoleOfAsync(_groupId, billingOwner));
            Assert.False(
                transferred.StatusCode == HttpStatusCode.OK && left.StatusCode == HttpStatusCode.NoContent,
                $"Both succeeded: transfer {(int)transferred.StatusCode}, leave {(int)left.StatusCode}");

            // Reset: Olga pays again and Otto is an owner.
            if (billingOwner != Id("olga"))
            {
                using var back = await As("otto").SendJsonAsync(HttpMethod.Post, $"/api/v1/groups/{_groupId}/transfer", new { userId = Id("olga") });
                Assert.Equal(HttpStatusCode.OK, back.StatusCode);
            }

            if (await _host.RoleOfAsync(_groupId, Id("otto")) is null)
            {
                await _host.AddMemberAsync(_groupId, Id("otto"), GroupRole.Owner);
            }
        }
    }

    [Fact]
    public async Task The_billing_owner_transfers_billing_before_stepping_down()
    {
        using var demote = await ChangeRoleAsync("olga", "olga", "admin");
        await ProblemResponse.AssertProblemAsync(demote, HttpStatusCode.Conflict, ErrorCodes.BillingOwnerTransferRequired);
        using var removeBilling = await RemoveAsync("otto", "olga");
        await ProblemResponse.AssertProblemAsync(removeBilling, HttpStatusCode.Conflict, ErrorCodes.BillingOwnerTransferRequired);

        var transfer = $"/api/v1/groups/{_groupId}/transfer";
        using var toAdmin = await As("olga").SendJsonAsync(HttpMethod.Post, transfer, new { userId = Id("adam") });
        await ProblemResponse.AssertProblemAsync(toAdmin, HttpStatusCode.Conflict, ErrorCodes.BillingOwnerMustBeOwner);
        using var byOtherOwner = await As("otto").SendJsonAsync(HttpMethod.Post, transfer, new { userId = Id("otto") });
        await ProblemResponse.AssertProblemAsync(byOtherOwner, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);

        using var transferred = await As("olga").SendJsonAsync(HttpMethod.Post, transfer, new { userId = Id("otto") });
        Assert.Equal(HttpStatusCode.OK, transferred.StatusCode);
        Assert.Equal(Id("otto"), (Guid)(await transferred.JsonAsync())["billingOwnerId"]!);
        var audit = (await _host.AuditEventsAsync("group", _groupId)).Single(e => e.Action == "group.billing_owner_transferred");
        Assert.Equal(Id("olga"), (Guid)JsonNode.Parse(audit.Before!)!["billingOwnerId"]!);
        Assert.Equal(Id("otto"), audit.SubjectId);

        using var leave = await RemoveAsync("olga", "olga");
        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
    }

    [Fact]
    public async Task Leaving_and_removing_bump_acl_version_audit_and_notify_with_the_revoke_flag()
    {
        var miaAcl = await _host.AclVersionAsync(Id("mia"));

        using var leave = await RemoveAsync("vic", "vic");
        using var removed = await RemoveAsync("adam", "mia", query: "?revokeEventShares=false");

        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Null(await _host.RoleOfAsync(_groupId, Id("vic")));
        Assert.Null(await _host.RoleOfAsync(_groupId, Id("mia")));
        Assert.Equal(miaAcl + 1, await _host.AclVersionAsync(Id("mia")));

        var audit = await _host.AuditEventsAsync("group", _groupId);
        Assert.Equal(Id("vic"), (Guid)JsonNode.Parse(audit.Single(e => e.Action == "group.member.left").Before!)!["userId"]!);
        var removal = audit.Single(e => e.Action == "group.member.removed");
        Assert.Equal(Id("mia"), (Guid)JsonNode.Parse(removal.Before!)!["userId"]!);
        Assert.False((bool)JsonNode.Parse(removal.After!)!["revokeEventShares"]!);
        Assert.Equal(
            [
                new MembershipChange(_groupId, Id("vic"), GroupRole.Viewer, null, MembershipChangeKind.Left, RevokeEventShares: true),
                new MembershipChange(_groupId, Id("mia"), GroupRole.Member, null, MembershipChangeKind.Removed, RevokeEventShares: false),
            ],
            _observer.Changes);

        // The removed member no longer sees the group.
        using var gone = await As("mia").GetAsync(new Uri($"/api/v1/groups/{_groupId}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Theory]
    [InlineData("adam", "anna", HttpStatusCode.Forbidden)] // admins cannot remove admins
    [InlineData("adam", "otto", HttpStatusCode.Forbidden)]
    [InlineData("mia", "vic", HttpStatusCode.Forbidden)]
    [InlineData("adam", "vic", HttpStatusCode.NoContent)]
    [InlineData("olga", "anna", HttpStatusCode.NoContent)]
    [InlineData("olga", "otto", HttpStatusCode.NoContent)]
    public async Task Removal_rules(string actor, string target, HttpStatusCode expected)
    {
        using var response = await RemoveAsync(actor, target);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Frozen_groups_refuse_role_changes_but_allow_removals()
    {
        await _host.QueryAsync(async db =>
        {
            var group = await db.Groups.FindAsync([_groupId], Ct);
            group!.FrozenAt = NodaTime.SystemClock.Instance.GetCurrentInstant();
            return await db.SaveChangesAsync(Ct);
        });

        using var change = await ChangeRoleAsync("olga", "mia", "viewer");
        using var remove = await RemoveAsync("olga", "mia");

        await ProblemResponse.AssertProblemAsync(change, HttpStatusCode.Conflict, ErrorCodes.GroupFrozen);
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
    }

    private async Task<string> MemberETagAsync(string name)
    {
        using var response = await As("olga").GetAsync(new Uri($"/api/v1/groups/{_groupId}/members", UriKind.Relative), Ct);
        return (string)(await response.JsonAsync())["items"]!.AsArray().Single(m => (Guid)m!["userId"]! == Id(name))!["etag"]!;
    }

    private async Task<(Guid Id, HttpClient Client)> PersonAsync(string name)
    {
        var email = ApiTestHost.UniqueEmail(name);
        var id = await _host.CreateUserAsync(email, displayName: name);
        return (id, await _host.SignedInClientAsync(email));
    }
}
