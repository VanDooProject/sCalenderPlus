using System.Net;
using SCalenderPlus.IntegrationTests.Auth;
using SCalenderPlus.IntegrationTests.Groups;
using SCalenderPlus.IntegrationTests.Infrastructure;

namespace SCalenderPlus.IntegrationTests.Calendars;

/// <summary>Issue #56: the personal calendar overlay (hidden, color) per user.</summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class PrefsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly List<HttpClient> _clients = [];
    private ApiTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await ApiTestHost.StartAsync(postgres);

    public async ValueTask DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Overlay_starts_with_defaults_and_is_replaced_with_if_match()
    {
        var olga = await PersonAsync("olga");
        var calendarId = await olga.CreateCalendarAsync();

        using var get = await olga.SendJsonAsync(HttpMethod.Get, $"/api/v1/calendars/{calendarId}/prefs");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var defaults = await get.JsonAsync();
        Assert.False((bool)defaults["hidden"]!);
        Assert.Null((string?)defaults["color"]);
        var etag = get.Headers.ETag!.Tag;
        Assert.Equal(etag, (string?)defaults["etag"]);

        using var missing = await olga.SendJsonAsync(HttpMethod.Put, $"/api/v1/calendars/{calendarId}/prefs", new { hidden = true });
        Assert.Equal((HttpStatusCode)428, missing.StatusCode);

        using var put = await olga.SendJsonAsync(HttpMethod.Put, $"/api/v1/calendars/{calendarId}/prefs", new { hidden = true, color = "#AA00FF" }, etag);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var saved = await put.JsonAsync();
        Assert.True((bool)saved["hidden"]!);
        Assert.Equal("#aa00ff", (string?)saved["color"]);

        using var stale = await olga.SendJsonAsync(HttpMethod.Put, $"/api/v1/calendars/{calendarId}/prefs", new { hidden = false }, etag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        using var invalid = await olga.SendJsonAsync(HttpMethod.Put, $"/api/v1/calendars/{calendarId}/prefs", new { hidden = false, color = "red" }, "*");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var list = await olga.SendJsonAsync(HttpMethod.Get, "/api/v1/me/calendar-prefs");
        var items = (await list.JsonAsync())["items"]!.AsArray();
        var item = Assert.Single(items);
        Assert.Equal(calendarId, (Guid)item!["calendarId"]!);
        Assert.True((bool)item["hidden"]!);
    }

    [Fact]
    public async Task Overlay_is_personal_and_needs_a_visible_calendar()
    {
        var olga = await PersonAsync("olga");
        var eve = await PersonAsync("eve");
        var calendarId = await olga.CreateCalendarAsync();

        using var foreign = await eve.SendJsonAsync(HttpMethod.Put, $"/api/v1/calendars/{calendarId}/prefs", new { hidden = true }, "*");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        using var put = await olga.SendJsonAsync(HttpMethod.Put, $"/api/v1/calendars/{calendarId}/prefs", new { hidden = true }, "*");
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        using var eveList = await eve.SendJsonAsync(HttpMethod.Get, "/api/v1/me/calendar-prefs");
        Assert.Empty((await eveList.JsonAsync())["items"]!.AsArray());

        var (_, etag) = await olga.GetCalendarAsync(calendarId);
        using var delete = await olga.SendJsonAsync(HttpMethod.Delete, $"/api/v1/calendars/{calendarId}", ifMatch: etag);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        using var after = await olga.SendJsonAsync(HttpMethod.Get, "/api/v1/me/calendar-prefs");
        Assert.Empty((await after.JsonAsync())["items"]!.AsArray());
    }

    private async Task<HttpClient> PersonAsync(string name)
    {
        var email = ApiTestHost.UniqueEmail(name);
        await _host.CreateUserAsync(email, displayName: name);
        var client = await _host.SignedInClientAsync(email);
        _clients.Add(client);
        return client;
    }
}
