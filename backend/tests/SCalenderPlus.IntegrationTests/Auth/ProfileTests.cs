using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SCalenderPlus.Application.Errors;
using SCalenderPlus.IntegrationTests.Infrastructure;
using SCalenderPlus.IntegrationTests.Problems;

namespace SCalenderPlus.IntegrationTests.Auth;

/// <summary>Issue #33: profile settings via <c>GET/PATCH /api/v1/me</c> (merge patch, ETag/If-Match).</summary>
[Trait(PostgresFixture.Category, PostgresFixture.Docker)]
public sealed class ProfileTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiTestHost _host = null!;
    private HttpClient _client = null!;
    private Guid _userId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await ApiTestHost.StartAsync(postgres);
        var email = ApiTestHost.UniqueEmail();
        _userId = await _host.CreateUserAsync(email);
        _client = await _host.SignedInClientAsync(email);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Merge_patch_updates_only_the_given_settings_and_changes_the_etag()
    {
        var (_, etag) = await GetMeAsync();

        using var response = await PatchAsync(new { displayName = "  Mia Vogel ", locale = "de", timeZone = "America/Argentina/Buenos_Aires", weekStart = "sunday" }, etag);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
        Assert.Equal("Mia Vogel", (string?)me["displayName"]);
        Assert.Equal("de", (string?)me["locale"]);
        Assert.Equal("America/Argentina/Buenos_Aires", (string?)me["timeZone"]);
        Assert.Equal("sunday", (string?)me["weekStart"]);
        var newEtag = response.Headers.ETag!.ToString();
        Assert.NotEqual(etag, newEtag);

        // Absent and null members stay unchanged.
        using var partial = await PatchAsync(new { locale = "en", timeZone = (string?)null }, newEtag);
        var after = JsonNode.Parse(await partial.Content.ReadAsStringAsync(Ct))!;
        Assert.Equal("en", (string?)after["locale"]);
        Assert.Equal("America/Argentina/Buenos_Aires", (string?)after["timeZone"]);
        Assert.Equal("Mia Vogel", (string?)after["displayName"]);
        var (reloaded, reloadedEtag) = await GetMeAsync();
        Assert.Equal("en", (string?)reloaded["locale"]);
        Assert.Equal(partial.Headers.ETag!.ToString(), reloadedEtag);

        var audit = (await _host.AuditEventsAsync(_userId)).Where(e => e.Action == "user.profile_updated").ToList();
        Assert.Equal(2, audit.Count);
        Assert.Equal("Mia", (string?)JsonNode.Parse(audit[0].Before!)!["displayName"]);
        Assert.Equal("Mia Vogel", (string?)JsonNode.Parse(audit[0].After!)!["displayName"]);
    }

    [Fact]
    public async Task Invalid_time_zone_is_422_with_the_field_in_errors()
    {
        var (_, etag) = await GetMeAsync();

        using var response = await PatchAsync(new { timeZone = "Europe/Atlantis" }, etag);

        var problem = await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, ErrorCodes.TimeZoneInvalid);
        Assert.NotNull(problem["errors"]!["timeZone"]);
        Assert.Equal("UTC", (string?)(await GetMeAsync()).Me["timeZone"]);
    }

    [Theory]
    [InlineData("locale", "fr")]
    [InlineData("weekStart", "funday")]
    [InlineData("weekStart", "Monday")]
    [InlineData("displayName", "   ")]
    [InlineData("displayName", "")]
    public async Task Invalid_settings_are_validation_problems(string field, string value)
    {
        var (_, etag) = await GetMeAsync();

        using var response = await PatchAsync(new Dictionary<string, string> { [field] = value }, etag);

        var problem = await ProblemResponse.AssertProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.True(problem["errors"]![field] is not null, problem.ToJsonString());
    }

    [Fact]
    public async Task If_match_is_required_and_must_be_current()
    {
        var (_, etag) = await GetMeAsync();

        using var missing = await PatchAsync(new { locale = "de" }, ifMatch: null);
        await ProblemResponse.AssertProblemAsync(missing, HttpStatusCode.PreconditionRequired, ErrorCodes.PreconditionRequired);

        using var first = await PatchAsync(new { locale = "de" }, etag);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var stale = await PatchAsync(new { locale = "en" }, etag);
        await ProblemResponse.AssertProblemAsync(stale, HttpStatusCode.PreconditionFailed, ErrorCodes.PreconditionFailed);

        using var any = await PatchAsync(new { locale = "en" }, "*");
        Assert.Equal(HttpStatusCode.OK, any.StatusCode);
    }

    [Fact]
    public async Task Plain_json_is_accepted_too()
    {
        var (_, etag) = await GetMeAsync();
        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri("/api/v1/me", UriKind.Relative))
        {
            Content = new StringContent("""{"weekStart":"saturday"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        using var response = await _client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("saturday", (string?)JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["weekStart"]);
    }

    private async Task<(JsonNode Me, string ETag)> GetMeAsync()
    {
        using var response = await _client.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!, response.Headers.ETag!.ToString());
    }

    private Task<HttpResponseMessage> PatchAsync(object body, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, new Uri("/api/v1/me", UriKind.Relative))
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, new MediaTypeHeaderValue("application/merge-patch+json")),
        };
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return _client.SendAsync(request, Ct);
    }
}
