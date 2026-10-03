using System.Net;
using System.Text.Json;

namespace SCalenderPlus.IntegrationTests.Health;

internal sealed record HealthBody(string Status, Dictionary<string, string> Checks);

internal static class HealthClient
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public static async Task<(HttpStatusCode Status, HealthBody Body, string Raw)> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return (response.StatusCode, JsonSerializer.Deserialize<HealthBody>(raw, _json)!, raw);
    }
}
