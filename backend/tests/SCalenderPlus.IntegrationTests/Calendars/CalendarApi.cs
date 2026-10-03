using System.Net;
using System.Text.Json.Nodes;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Groups;

namespace SCalenderPlus.IntegrationTests.Calendars;

/// <summary>Helpers for calendar tests: api calls with JSON results.</summary>
internal static class CalendarApi
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<Guid> CreateCalendarAsync(this HttpClient client, string name = "Training", Guid? groupId = null, object? groupRoleDefaults = null)
    {
        using var response = await client.SendJsonAsync(HttpMethod.Post, "/api/v1/calendars", new { name, defaultTimeZone = "Europe/Berlin", groupId, groupRoleDefaults });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (Guid)(await response.JsonAsync())["id"]!;
    }

    public static async Task<(JsonNode Body, string ETag)> GetCalendarAsync(this HttpClient client, Guid calendarId)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/calendars/{calendarId}", UriKind.Relative), Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.JsonAsync(), response.Headers.ETag!.ToString());
    }

    /// <summary>All calendars the client sees (every page), as id → <c>myLevel</c>.</summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> MyLevelsAsync(this HttpClient client, int limit = 200)
    {
        var levels = new Dictionary<Guid, string>();
        string? cursor = null;
        do
        {
            var query = cursor is null ? $"?limit={limit}" : $"?limit={limit}&cursor={cursor}";
            using var response = await client.GetAsync(new Uri("/api/v1/calendars" + query, UriKind.Relative), Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = await response.JsonAsync();
            foreach (var item in page["items"]!.AsArray())
            {
                levels.Add((Guid)item!["id"]!, (string)item["myLevel"]!);
            }

            cursor = (string?)page["nextCursor"];
        }
        while (cursor is not null);

        return levels;
    }

    public static Task<long> CalendarAclVersionAsync(this ApiTestHost host, Guid calendarId) =>
        host.QueryAsync(db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
            db.Calendars.Where(c => c.Id == calendarId).Select(c => c.AclVersion), Ct));

    public static Task<long> GroupAclVersionAsync(this ApiTestHost host, Guid groupId) =>
        host.QueryAsync(db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
            db.Groups.Where(g => g.Id == groupId).Select(g => g.AclVersion), Ct));
}
