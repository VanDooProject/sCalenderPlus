using System.Net;
using System.Text.Json.Nodes;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.IntegrationTests.Auth;
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
