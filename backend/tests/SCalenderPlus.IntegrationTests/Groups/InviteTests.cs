using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using SCalenderPlus.Application.Email;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Application.Groups;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Infrastructure;
using SCalenderPlus.IntegrationTests.Problems;

namespace SCalenderPlus.IntegrationTests.Groups;

/// <summary>Issue #36: group invites by email and by link; email invites bind to the verified address.</summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed partial class InviteTests(GroupHostFixture fixture) : IClassFixture<GroupHostFixture>, IAsyncLifetime
{
    private readonly ApiTestHost _host = fixture.Host;
    private readonly List<HttpClient> _clients = [];
    private HttpClient _owner = null!;
    private Guid _groupId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        (_, _owner) = await PersonAsync("olga");
        _groupId = await _owner.CreateGroupAsync("FC Lions");
        fixture.Observer.Changes.Clear();
    }

    public ValueTask DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        return ValueTask.CompletedTask;
    }

    private string InvitesPath => $"/api/v1/groups/{_groupId}/invites";

    [Fact]
    public async Task Email_invite_then_sign_up_then_verify_joins_automatically_but_not_before()
    {
        var email = ApiTestHost.UniqueEmail("nora");
        using var created = await _owner.SendJsonAsync(HttpMethod.Post, InvitesPath, new { email, role = "admin" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var invite = (await created.JsonAsync())["invite"]!;
        Assert.Equal("email", (string?)invite["kind"]);
        Assert.Equal(1, (int)invite["maxUses"]!);
        Assert.Null((string?)(await created.JsonAsync())["url"]); // the token only goes to the mailbox
        var token = InviteToken(await _host.SingleEmailToAsync(email));

        // Sign-up: the account exists but is unverified, so it neither joins nor may accept.
        using var anonymous = _host.CreateClient();
        using var registered = await anonymous.SendJsonAsync(HttpMethod.Post, "/api/v1/auth/register", new { email, password = ApiTestHost.Password, displayName = "Nora" });
        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        var nora = await _host.SignedInClientAsync(email);
        _clients.Add(nora);
        var noraId = (await _host.FindUserAsync(email)).Id;
        using var early = await AcceptAsync(nora, token);
        await ProblemResponse.AssertProblemAsync(early, HttpStatusCode.Forbidden, ErrorCodes.EmailNotVerified);
        Assert.Null(await _host.RoleOfAsync(_groupId, noraId));

        // Verification joins the pending invite with its role.
        await ConfirmAsync(email);

        Assert.Equal(GroupRole.Admin, await _host.RoleOfAsync(_groupId, noraId));
        using var mine = await nora.GetAsync(new Uri("/api/v1/groups", UriKind.Relative), Ct);
        Assert.Equal("admin", (string?)Assert.Single((await mine.JsonAsync())["items"]!.AsArray())!["myRole"]);
        var joined = (await _host.AuditEventsAsync("group", _groupId)).Single(e => e.Action == "group.member.joined");
        var after = JsonNode.Parse(joined.After!)!;
        Assert.Equal(noraId, (Guid)after["userId"]!);
        Assert.Equal("email", (string?)after["via"]);
        Assert.True((bool)after["automatic"]!);
        Assert.Contains(fixture.Observer.Changes, c => c == new MembershipChange(_groupId, noraId, null, GroupRole.Admin, MembershipChangeKind.Joined));

        // The invite is used up.
        using var again = await AcceptAsync(nora, token);
        await ProblemResponse.AssertProblemAsync(again, HttpStatusCode.BadRequest, ErrorCodes.TokenInvalid);
        Assert.Empty(await PendingAsync());
    }

    [Fact]
    public async Task Unverified_account_with_the_matching_email_does_not_join()
    {
        var (email, mia, miaId) = await UnverifiedPersonAsync("mia");

        using var created = await _owner.SendJsonAsync(HttpMethod.Post, InvitesPath, new { email = email.ToUpperInvariant(), role = "member" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var token = InviteToken((await _host.EmailsToAsync(email)).Single(m => m.Subject.StartsWith("Invitation", StringComparison.Ordinal)));

        using var accept = await AcceptAsync(mia, token);
        await ProblemResponse.AssertProblemAsync(accept, HttpStatusCode.Forbidden, ErrorCodes.EmailNotVerified);
        Assert.Null(await _host.RoleOfAsync(_groupId, miaId));
        Assert.Single(await PendingAsync());
    }

    [Fact]
    public async Task Email_invites_bind_to_the_invited_address()
    {
        var (bob, bobClient) = await PersonAsync("bob");
        var (_, eveClient) = await PersonAsync("eve");
        using var created = await _owner.SendJsonAsync(HttpMethod.Post, InvitesPath, new { email = bob, role = "viewer" });
        var token = InviteToken(await _host.SingleEmailToAsync(bob));

        using var byEve = await AcceptAsync(eveClient, token);
        await ProblemResponse.AssertProblemAsync(byEve, HttpStatusCode.Forbidden, ErrorCodes.InviteEmailMismatch);

        using var byBob = await AcceptAsync(bobClient, token);
        Assert.Equal(HttpStatusCode.OK, byBob.StatusCode);
        var group = await byBob.JsonAsync();
        Assert.Equal(_groupId, (Guid)group["id"]!);
        Assert.Equal("viewer", (string?)group["myRole"]);
        Assert.NotNull(byBob.Headers.ETag);
    }

    [Fact]
    public async Task Invite_links_have_max_uses_and_carry_at_most_member()
    {
        using var tooHigh = await _owner.SendJsonAsync(HttpMethod.Post, InvitesPath, new { role = "admin" });
        var problem = await ProblemResponse.AssertProblemAsync(tooHigh, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.NotNull(problem["errors"]!["role"]);

        using var created = await _owner.SendJsonAsync(HttpMethod.Post, InvitesPath, new { role = "member", maxUses = 2 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.JsonAsync();
        Assert.Equal("link", (string?)body["invite"]!["kind"]);
        var url = (string)body["url"]!;
        Assert.StartsWith("https://app.example.test/invite?token=", url, StringComparison.Ordinal);
        var token = Uri.UnescapeDataString(url[(url.IndexOf('=', StringComparison.Ordinal) + 1)..]);

        for (var i = 0; i < 2; i++)
        {
            var (_, client) = await PersonAsync("link" + i);
            using var accepted = await AcceptAsync(client, token);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            Assert.Equal("member", (string?)(await accepted.JsonAsync())["myRole"]);
        }

        var (_, late) = await PersonAsync("late");
        using var usedUp = await AcceptAsync(late, token);
        await ProblemResponse.AssertProblemAsync(usedUp, HttpStatusCode.BadRequest, ErrorCodes.TokenInvalid);
        Assert.Equal(3, (int)(await _owner.GetGroupAsync(_groupId)).Body["memberCount"]!);
        Assert.Equal(2, await _host.QueryAsync(db => db.GroupInvites.Where(i => i.GroupId == _groupId).Select(i => i.Uses).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Members_accepting_again_keep_their_role_and_unknown_tokens_are_invalid()
    {
        var token = await CreateLinkAsync();
        using var byOwner = await AcceptAsync(_owner, token);
        using var unknown = await AcceptAsync(_owner, "not-a-token");

        Assert.Equal("owner", (string?)(await byOwner.JsonAsync())["myRole"]);
        await ProblemResponse.AssertProblemAsync(unknown, HttpStatusCode.BadRequest, ErrorCodes.TokenInvalid);
        Assert.Equal(0, await _host.QueryAsync(db => db.GroupInvites.Where(i => i.GroupId == _groupId).Select(i => i.Uses).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Invites_expire()
    {
        using var created = await _owner.SendJsonAsync(HttpMethod.Post, InvitesPath, new { role = "viewer", expiresInDays = 1 });
        var url = (string)(await created.JsonAsync())["url"]!;
        Assert.Single(await PendingAsync());

        fixture.Clock.Advance(Duration.FromDays(1) + Duration.FromMinutes(1));

        var (_, client) = await PersonAsync("tardy");
        using var accept = await AcceptAsync(client, Uri.UnescapeDataString(url.Split("token=")[1]));
        await ProblemResponse.AssertProblemAsync(accept, HttpStatusCode.BadRequest, ErrorCodes.TokenInvalid);
        Assert.Empty(await PendingAsync());
    }

    [Theory]
    [InlineData("maxUses", 0)]
    [InlineData("maxUses", 1001)]
    [InlineData("expiresInDays", 31)]
    public async Task Out_of_range_limits_are_validation_problems(string field, int value)
    {
        using var response = await _owner.SendJsonAsync(HttpMethod.Post, InvitesPath, new Dictionary<string, object> { ["role"] = "member", [field] = value });

        var problem = await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.NotNull(problem["errors"]![field]);
    }

    [Fact]
    public async Task Numbers_sent_as_strings_are_rejected()
    {
        using var response = await _owner.SendJsonAsync(HttpMethod.Post, InvitesPath, new { role = "member", maxUses = "5" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await PendingAsync());
    }

    [Fact]
    public async Task Admins_invite_up_to_member_members_not_at_all()
    {
        var (_, admin) = await MemberAsync("adam", GroupRole.Admin);
        var (_, member) = await MemberAsync("max", GroupRole.Member);

        using var adminInvitesAdmin = await admin.SendJsonAsync(HttpMethod.Post, InvitesPath, new { email = ApiTestHost.UniqueEmail("x"), role = "admin" });
        using var adminInvitesMember = await admin.SendJsonAsync(HttpMethod.Post, InvitesPath, new { email = ApiTestHost.UniqueEmail("y"), role = "member" });
        using var memberInvites = await member.SendJsonAsync(HttpMethod.Post, InvitesPath, new { role = "viewer" });

        var problem = await ProblemResponse.AssertProblemAsync(adminInvitesAdmin, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);
        Assert.Equal("owner", (string?)problem["required"]);
        Assert.Equal(HttpStatusCode.Created, adminInvitesMember.StatusCode);
        await ProblemResponse.AssertProblemAsync(memberInvites, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);
    }

    [Fact]
    public async Task Unverified_members_cannot_invite()
    {
        var (_, client, userId) = await UnverifiedPersonAsync("uma");
        await _host.AddMemberAsync(_groupId, userId, GroupRole.Owner);

        using var response = await client.SendJsonAsync(HttpMethod.Post, InvitesPath, new { role = "member" });

        await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.Forbidden, ErrorCodes.EmailNotVerified);
    }

    [Fact]
    public async Task Revoked_invites_cannot_be_used_and_admins_revoke_only_what_they_could_create()
    {
        var token = await CreateLinkAsync();
        var linkId = (Guid)Assert.Single(await PendingAsync())!["id"]!;
        var invitee = ApiTestHost.UniqueEmail("ida");
        using var ownerInvitesAdmin = await _owner.SendJsonAsync(HttpMethod.Post, InvitesPath, new { email = invitee, role = "admin" });
        var adminInviteId = (Guid)(await ownerInvitesAdmin.JsonAsync())["invite"]!["id"]!;
        var (_, admin) = await MemberAsync("adam", GroupRole.Admin);

        using var forbidden = await admin.SendJsonAsync(HttpMethod.Delete, $"/api/v1/invites/{adminInviteId}");
        await ProblemResponse.AssertProblemAsync(forbidden, HttpStatusCode.Forbidden, ErrorCodes.InsufficientPermission);

        using var revoked = await admin.SendJsonAsync(HttpMethod.Delete, $"/api/v1/invites/{linkId}");
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        using var again = await admin.SendJsonAsync(HttpMethod.Delete, $"/api/v1/invites/{linkId}");
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);

        var (_, client) = await PersonAsync("rita");
        using var accept = await AcceptAsync(client, token);
        await ProblemResponse.AssertProblemAsync(accept, HttpStatusCode.BadRequest, ErrorCodes.TokenInvalid);
        Assert.Equal(adminInviteId, (Guid)Assert.Single(await PendingAsync())!["id"]!);
        Assert.Single(await _host.AuditEventsAsync("group", _groupId), e => e.Action == "group.invite.revoked");

        using var unknown = await admin.SendJsonAsync(HttpMethod.Delete, $"/api/v1/invites/{Guid.CreateVersion7()}");
        await ProblemResponse.AssertProblemAsync(unknown, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_new_email_invite_replaces_the_pending_one_and_is_sent_in_the_recipients_language()
    {
        var (email, _, _) = await UnverifiedPersonAsync("dora");
        await _host.QueryAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Email == email, Ct);
            user.Locale = "de";
            return await db.SaveChangesAsync(Ct);
        });

        using var first = await _owner.SendJsonAsync(HttpMethod.Post, InvitesPath, new { email, role = "viewer" });
        using var second = await _owner.SendJsonAsync(HttpMethod.Post, InvitesPath, new { email, role = "member" });

        var invites = (await _host.EmailsToAsync(email)).Where(m => m.Subject.StartsWith("Einladung", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, invites.Count);
        Assert.Contains("„FC Lions“", invites[0].Subject, StringComparison.Ordinal);
        var pending = Assert.Single(await PendingAsync())!;
        Assert.Equal("member", (string?)pending["role"]);
        Assert.Equal(email, (string?)pending["email"]);

        // Verifying joins with the newest invite only.
        var userId = (await _host.FindUserAsync(email)).Id;
        await ConfirmAsync(email);
        Assert.Equal(GroupRole.Member, await _host.RoleOfAsync(_groupId, userId));
    }

    [Fact]
    public async Task Pending_invites_are_listed_for_admins_without_tokens()
    {
        await CreateLinkAsync();
        using var email = await _owner.SendJsonAsync(HttpMethod.Post, InvitesPath, new { email = ApiTestHost.UniqueEmail("p"), role = "member" });

        using var response = await _owner.GetAsync(new Uri(InvitesPath + "?limit=1", UriKind.Relative), Ct);

        var page = await response.JsonAsync();
        var first = Assert.Single(page["items"]!.AsArray())!;
        Assert.Equal("link", (string?)first["kind"]);
        Assert.DoesNotContain("token", first.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        Assert.NotNull((string?)page["nextCursor"]);
        var audit = (await _host.AuditEventsAsync("group", _groupId)).Where(e => e.Action == "group.invite.created").ToList();
        Assert.Equal(2, audit.Count);
        Assert.All(audit, e => Assert.DoesNotContain("token", e.After!, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Frozen_groups_take_no_new_members_through_existing_invites()
    {
        var token = await CreateLinkAsync();
        var (email, _, userId) = await UnverifiedPersonAsync("fritz");
        using (var invited = await _owner.SendJsonAsync(HttpMethod.Post, InvitesPath, new { email, role = "member" }))
        {
            Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        }

        await _host.QueryAsync(async db =>
        {
            var group = await db.Groups.SingleAsync(g => g.Id == _groupId, Ct);
            group.FrozenAt = SystemClock.Instance.GetCurrentInstant();
            return await db.SaveChangesAsync(Ct);
        });

        var (_, client) = await PersonAsync("frank");
        using var accept = await AcceptAsync(client, token);
        await ProblemResponse.AssertProblemAsync(accept, HttpStatusCode.Conflict, ErrorCodes.GroupFrozen);

        // Confirming the address still works; the email invite stays pending instead of joining.
        await ConfirmAsync(email);
        Assert.Null(await _host.RoleOfAsync(_groupId, userId));
        Assert.Equal(2, (await PendingAsync()).Count);
        Assert.Equal(1, (int)(await _owner.GetGroupAsync(_groupId)).Body["memberCount"]!);
    }

    [Fact]
    public async Task Concurrent_joins_through_one_link_all_succeed_until_it_is_used_up()
    {
        var token = await CreateLinkAsync();
        var people = new List<HttpClient>();
        for (var i = 0; i < 5; i++)
        {
            people.Add((await PersonAsync("crowd" + i)).Client);
        }

        var joined = await Task.WhenAll(people.Select(c => AcceptAsync(c, token)));
        Assert.All(joined, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(6, (int)(await _owner.GetGroupAsync(_groupId)).Body["memberCount"]!);

        // The last use goes to exactly one of several concurrent callers.
        using var single = await _owner.SendJsonAsync(HttpMethod.Post, InvitesPath, new { role = "viewer", maxUses = 1 });
        var url = (string)(await single.JsonAsync())["url"]!;
        var lastUse = Uri.UnescapeDataString(url.Split("token=")[1]);
        var racers = new List<HttpClient>();
        for (var i = 0; i < 4; i++)
        {
            racers.Add((await PersonAsync("racer" + i)).Client);
        }

        var raced = await Task.WhenAll(racers.Select(c => AcceptAsync(c, lastUse)));
        Assert.Single(raced, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(raced.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Equal(7, (int)(await _owner.GetGroupAsync(_groupId)).Body["memberCount"]!);
        foreach (var response in joined.Concat(raced))
        {
            response.Dispose();
        }
    }

    /// <summary>Opens the confirmation link of the sign-up email.</summary>
    private async Task ConfirmAsync(string email)
    {
        var message = (await _host.EmailsToAsync(email)).Single(m => m.TextBody.Contains("/verify-email?", StringComparison.Ordinal));
        var (_, id, token) = ApiTestHost.LinkIn(message);
        using var anonymous = _host.CreateClient();
        using var confirmed = await anonymous.SendJsonAsync(HttpMethod.Post, "/api/v1/auth/confirm-email", new { userId = id, token });
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
    }

    private async Task<string> CreateLinkAsync()
    {
        using var created = await _owner.SendJsonAsync(HttpMethod.Post, InvitesPath, new { role = "member" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var url = (string)(await created.JsonAsync())["url"]!;
        return Uri.UnescapeDataString(url.Split("token=")[1]);
    }

    private async Task<JsonArray> PendingAsync()
    {
        using var response = await _owner.GetAsync(new Uri(InvitesPath, UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.JsonAsync())["items"]!.AsArray();
    }

    private static Task<HttpResponseMessage> AcceptAsync(HttpClient client, string token) =>
        client.SendJsonAsync(HttpMethod.Post, "/api/v1/invites/accept", new { token });

    private static string InviteToken(EmailMessage message)
    {
        var match = InviteLinkPattern().Match(message.TextBody);
        Assert.True(match.Success, "No invite link in: " + message.TextBody);
        return Uri.UnescapeDataString(match.Groups["token"].Value);
    }

    private async Task<(Guid Id, HttpClient Client)> MemberAsync(string name, GroupRole role)
    {
        var (email, client) = await PersonAsync(name);
        var id = (await _host.FindUserAsync(email)).Id;
        await _host.AddMemberAsync(_groupId, id, role);
        return (id, client);
    }

    private async Task<(string Email, HttpClient Client)> PersonAsync(string name)
    {
        var email = ApiTestHost.UniqueEmail(name);
        await _host.CreateUserAsync(email, displayName: name);
        var client = await _host.SignedInClientAsync(email);
        _clients.Add(client);
        return (email, client);
    }

    /// <summary>A signed-up user who has not confirmed the address yet (confirmation email queued, like after register).</summary>
    private async Task<(string Email, HttpClient Client, Guid Id)> UnverifiedPersonAsync(string name)
    {
        var email = ApiTestHost.UniqueEmail(name);
        using var anonymous = _host.CreateClient();
        using var registered = await anonymous.SendJsonAsync(HttpMethod.Post, "/api/v1/auth/register", new { email, password = ApiTestHost.Password, displayName = name });
        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        var client = await _host.SignedInClientAsync(email);
        _clients.Add(client);
        return (email, client, (await _host.FindUserAsync(email)).Id);
    }

    [GeneratedRegex(@"https://app\.example\.test/invite\?token=(?<token>\S+)")]
    private static partial Regex InviteLinkPattern();
}
