using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using SCalenderPlus.Core.Groups;
using SCalenderPlus.IntegrationTests.Auth;

namespace SCalenderPlus.IntegrationTests.Groups;

/// <summary>Helpers for group tests: api calls with JSON results and direct seeding of memberships.</summary>
internal static class GroupApi
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<Guid> CreateGroupAsync(this HttpClient client, string name = "Lions")
    {
        using var response = await client.PostAsJsonAsync("/api/v1/groups", new { name }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (Guid)(await response.JsonAsync())["id"]!;
    }

    public static async Task<(JsonNode Body, string ETag)> GetGroupAsync(this HttpClient client, Guid groupId)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/groups/{groupId}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.JsonAsync(), response.Headers.ETag!.ToString());
    }

    public static Task<HttpResponseMessage> SendJsonAsync(this HttpClient client, HttpMethod method, string path, object? body = null, string? ifMatch = null, string contentType = "application/json")
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, contentType);
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return client.SendAsync(request, Ct);
    }

    public static async Task<JsonNode> JsonAsync(this HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;

    /// <summary>Adds a membership directly in the database (seeding; the api path is the invite flow).</summary>
    public static Task AddMemberAsync(this ApiTestHost host, Guid groupId, Guid userId, GroupRole role) =>
        host.QueryAsync(async db =>
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            db.GroupMembers.Add(new GroupMember { GroupId = groupId, UserId = userId, Role = role, JoinedAt = now, UpdatedAt = now });
            return await db.SaveChangesAsync(Ct);
        });

    public static Task<GroupRole?> RoleOfAsync(this ApiTestHost host, Guid groupId, Guid userId) =>
        host.QueryAsync(db => db.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == groupId && m.UserId == userId)
            .Select(m => (GroupRole?)m.Role)
            .SingleOrDefaultAsync(Ct));
}
