using System.Net;
using System.Text.Json.Nodes;

namespace SCalenderPlus.IntegrationTests.Problems;

/// <summary>Asserts the RFC 9457 contract of docs/architecture/api.md §2 on an error response.</summary>
internal static class ProblemResponse
{
    public static async Task<JsonObject> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == status, $"Expected {status}, got {response.StatusCode}: {raw}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = JsonNode.Parse(raw)!.AsObject();
        Assert.Equal(code, (string?)body["code"]);
        Assert.Equal((int)status, (int?)body["status"]);
        Assert.Equal("https://scalenderplus.app/problems/" + code.Replace('_', '-'), (string?)body["type"]);
        Assert.False(string.IsNullOrEmpty((string?)body["title"]));
        Assert.False(string.IsNullOrEmpty((string?)body["traceId"]));
        Assert.Equal(response.RequestMessage!.RequestUri!.AbsolutePath, (string?)body["instance"]);
        return body;
    }
}
