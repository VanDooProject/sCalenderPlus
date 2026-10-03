using System.Net;
using System.Text.Json.Nodes;
using SCalenderPlus.IntegrationTests.Infrastructure;
using ApiProgram = SCalenderPlus.Api.Program;

namespace SCalenderPlus.IntegrationTests.OpenApi;

/// <summary>The document served at runtime is the one exported at build time and committed. No Docker needed.</summary>
public sealed class OpenApiDocumentTests
{
    [Fact]
    public async Task Served_document_matches_the_committed_backend_openapi_v1_json()
    {
        await using var factory = new HostFactory<ApiProgram>(TestSettings.For(TestSettings.UnreachableDatabase));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        var served = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var committed = JsonNode.Parse(await File.ReadAllTextAsync(CommittedDocumentPath(), TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            JsonNode.DeepEquals(served, committed),
            "backend/openapi/v1.json is stale: run `dotnet build backend` and commit the regenerated file.");
        Assert.NotNull(served?["paths"]?["/health/ready"]?["get"]);
    }

    [Fact]
    public async Task Integers_are_plain_integers_not_integer_or_string()
    {
        var document = JsonNode.Parse(await File.ReadAllTextAsync(CommittedDocumentPath(), TestContext.Current.CancellationToken))!;

        var mixed = Schemas(document).Where(s => s["type"] is JsonArray types
            && types.Any(t => (string?)t == "integer") && types.Any(t => (string?)t == "string")).ToList();

        Assert.True(mixed.Count == 0, "Integer schemas also allow strings (lenient JSON number handling): " + string.Join(", ", mixed.Select(s => s.GetPath())));
    }

    private static IEnumerable<JsonObject> Schemas(JsonNode? node) => node switch
    {
        JsonObject obj => new[] { obj }.Concat(obj.SelectMany(p => Schemas(p.Value))),
        JsonArray array => array.SelectMany(Schemas),
        _ => [],
    };

    private static string CommittedDocumentPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SCalenderPlus.slnx")))
            {
                return Path.Combine(dir.FullName, "openapi", "v1.json");
            }
        }

        throw new InvalidOperationException("backend/SCalenderPlus.slnx not found above " + AppContext.BaseDirectory);
    }
}
