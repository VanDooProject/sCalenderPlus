using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using SCalenderPlus.Core.Calendars;
using SCalenderPlus.Core.Events;
using SCalenderPlus.Core.Permissions;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Groups;

namespace SCalenderPlus.IntegrationTests.Events;

/// <summary>Helpers for event tests: api calls with JSON results and direct seeding/inspection of stored state.</summary>
internal static class EventApi
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A timed event on 2026-11-02 18:00–20:00 Berlin unless overridden.</summary>
    public static object Timed(Guid calendarId, string title = "Board meeting", string start = "2026-11-02T18:00:00", string end = "2026-11-02T20:00:00", string? timeZone = "Europe/Berlin", string? transparency = null) =>
        new { calendarId, title, start = new { dateTime = start, timeZone }, end = new { dateTime = end }, transparency };

    public static object AllDay(Guid calendarId, string title = "Tournament", string start = "2026-11-02", string end = "2026-11-03") =>
        new { calendarId, title, start = new { date = start }, end = new { date = end } };

    public static async Task<(JsonNode Body, string ETag)> CreateEventAsync(this HttpClient client, object body)
    {
        using var response = await client.SendJsonAsync(HttpMethod.Post, "/api/v1/events", body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.JsonAsync(), response.Headers.ETag!.ToString());
    }

    public static async Task<Guid> CreateEventIdAsync(this HttpClient client, object body) => (Guid)(await client.CreateEventAsync(body)).Body["id"]!;

    public static async Task<(JsonNode Body, string ETag)> GetEventAsync(this HttpClient client, Guid eventId)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/events/{eventId}", UriKind.Relative), Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.JsonAsync(), response.Headers.ETag!.ToString());
    }

    /// <summary>An override entry of a request body: <c>everyone</c>, <c>anonymous</c>, <c>user</c> or <c>group</c>.</summary>
    public static object Everyone(string level) => new { principal = new { type = "everyone" }, level };

    public static object User(Guid userId, string level) => new { principal = new { type = "user", id = userId }, level };

    public static object Group(Guid groupId, string level, string? minRole = null) => new { principal = new { type = "group", id = groupId, minRole }, level };

    public static Task<HttpResponseMessage> PutOverridesAsync(this HttpClient client, Guid eventId, object[] overrides, string? ifMatch = "*") =>
        client.SendJsonAsync(HttpMethod.Put, $"/api/v1/events/{eventId}/overrides", new { overrides }, ifMatch);

    /// <summary>Replaces the event's overrides through the api (asserts 200) and returns the new set.</summary>
    public static async Task<JsonNode> SetOverridesAsync(this HttpClient client, Guid eventId, params object[] overrides)
    {
        using var response = await client.PutOverridesAsync(eventId, overrides);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.JsonAsync();
    }

    public static async Task<(JsonNode Body, string ETag)> GetOverridesAsync(this HttpClient client, Guid eventId)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/events/{eventId}/overrides", UriKind.Relative), Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.JsonAsync(), response.Headers.ETag!.ToString());
    }

    /// <summary>The caller's <c>myLevel</c> on the event, or <c>none</c> when it answers 404.</summary>
    public static async Task<string> LevelOnAsync(this HttpClient client, Guid eventId)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/events/{eventId}", UriKind.Relative), Ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return "none";
        }

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (string)(await response.JsonAsync())["myLevel"]!;
    }

    /// <summary>Gives <paramref name="groupId"/> (members with at least <paramref name="minRole"/>) a group grant directly.</summary>
    public static Task GrantGroupAsync(this ApiTestHost host, Guid calendarId, Guid groupId, CalendarLevel level, Core.Groups.GroupRole minRole = Core.Groups.GroupRole.Viewer) =>
        host.QueryAsync(async db =>
        {
            var grant = CalendarGrantEntry.For(calendarId, Principal.Group(groupId, minRole), level);
            grant.CreatedBy = groupId;
            grant.CreatedAt = grant.UpdatedAt = SystemClock.Instance.GetCurrentInstant();
            db.CalendarGrants.Add(grant);
            return await db.SaveChangesAsync(Ct);
        });

    public static Task<List<EventOverrideEntry>> StoredOverridesAsync(this ApiTestHost host, Guid eventId) =>
        host.QueryAsync(db => db.EventOverrides.AsNoTracking().Where(o => o.EventId == eventId).ToListAsync(Ct));

    /// <summary>Gives <paramref name="userId"/> a user grant directly (grant selection rules are tested elsewhere).</summary>
    public static Task GrantAsync(this ApiTestHost host, Guid calendarId, Guid userId, CalendarLevel level) =>
        host.QueryAsync(async db =>
        {
            var grant = CalendarGrantEntry.For(calendarId, Principal.User(userId), level);
            grant.CreatedBy = userId;
            grant.CreatedAt = grant.UpdatedAt = SystemClock.Instance.GetCurrentInstant();
            db.CalendarGrants.Add(grant);
            return await db.SaveChangesAsync(Ct);
        });

    /// <summary>The stored row, deleted ones included (tests may read events directly; production code may not).</summary>
    public static Task<Event?> StoredEventAsync(this ApiTestHost host, Guid eventId) =>
        host.QueryAsync(db => db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.Id == eventId, Ct));

    public static Task<List<CalendarChange>> ChangesAsync(this ApiTestHost host, Guid calendarId) =>
        host.QueryAsync(db => db.CalendarChanges.AsNoTracking().Where(c => c.CalendarId == calendarId).OrderBy(c => c.Seq).ToListAsync(Ct));

    public static Task<int> ExecuteSqlAsync(this ApiTestHost host, FormattableString sql) =>
        host.QueryAsync(db => db.Database.ExecuteSqlAsync(sql, Ct));
}
