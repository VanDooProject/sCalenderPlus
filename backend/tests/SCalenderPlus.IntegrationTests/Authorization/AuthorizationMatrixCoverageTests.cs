using System.Net;
using System.Text.Json.Nodes;
using SCalenderPlus.IntegrationTests.Infrastructure;
using ApiProgram = SCalenderPlus.Api.Program;

namespace SCalenderPlus.IntegrationTests.Authorization;

/// <summary>
/// Generator side of the matrix: enumerates the operations of the served OpenAPI document and fails for every
/// operation without matrix entries (issue #26). No Docker needed, so it also runs with SCAL_SKIP_DOCKER_TESTS.
/// </summary>
public sealed class AuthorizationMatrixCoverageTests
{
    [Fact]
    public async Task Every_operation_of_the_OpenAPI_document_is_covered_by_the_matrix()
    {
        var operations = await ServedOperationsAsync();

        var problems = MatrixCoverage.Check(operations, AuthorizationMatrix.AnonymousOperations, AuthorizationMatrix.Cases);

        Assert.NotEmpty(operations);
        Assert.True(problems.Count == 0, "Authorization matrix is incomplete:\n  " + string.Join("\n  ", problems));
    }

    [Fact]
    public void Operation_without_case_is_reported()
    {
        var operations = new[] { new ApiOperation("GET", "/health/live"), new ApiOperation("POST", "/api/v1/groups") };

        var problems = MatrixCoverage.Check(operations, AuthorizationMatrix.AnonymousOperations.Take(1).ToList(), []);

        Assert.Equal(["POST /api/v1/groups: no authorization matrix case. Add cases to AuthorizationMatrix.Cases "
            + "(or, if it is deliberately public, to AuthorizationMatrix.AnonymousOperations with a reason)."], problems);
    }

    [Fact]
    public void Protected_operation_needs_anonymous_401_and_cross_tenant_404()
    {
        var user = new MatrixActor("user", _ => throw new NotSupportedException());
        var operation = new ApiOperation("GET", "/api/v1/groups/{id}");
        var routes = (Func<MatrixScenario, IReadOnlyDictionary<string, string>>)(_ => new Dictionary<string, string>());

        var problems = MatrixCoverage.Check([operation], [], [new MatrixCase(operation, user, HttpStatusCode.OK, routes)]);

        Assert.Contains(problems, p => p.Contains("missing case 'anonymous -> 401'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("no cross-tenant case expecting 404", StringComparison.Ordinal));
    }

    [Fact]
    public void Stale_and_duplicate_entries_are_reported()
    {
        var operation = new ApiOperation("GET", "/api/v1/me");
        MatrixCase[] cases =
        [
            new(operation, Actors.Anonymous, HttpStatusCode.Unauthorized),
            new(operation, Actors.Anonymous, HttpStatusCode.Unauthorized),
        ];

        var problems = MatrixCoverage.Check([], [new AnonymousOperation(new ApiOperation("GET", "/gone"), "test")], cases);

        Assert.Contains("GET /gone: matrix entry for an operation that is not in the OpenAPI document (removed or renamed?).", problems);
        Assert.Contains("GET /api/v1/me: matrix entry for an operation that is not in the OpenAPI document (removed or renamed?).", problems);
        Assert.Contains("GET /api/v1/me: more than one case for actor 'anonymous'.", problems);
    }

    [Fact]
    public void Operations_are_read_from_every_method_of_every_path()
    {
        var document = JsonNode.Parse("""
            { "paths": { "/a": { "get": {}, "post": {}, "parameters": [] }, "/b/{id}": { "delete": {} } } }
            """)!;

        var operations = ApiOperation.FromDocument(document);

        Assert.Equal([new("GET", "/a"), new("POST", "/a"), new("DELETE", "/b/{id}")], operations);
        Assert.True(operations[2].HasPathParameters);
    }

    internal static async Task<IReadOnlyList<ApiOperation>> ServedOperationsAsync()
    {
        await using var factory = new HostFactory<ApiProgram>(TestSettings.For(TestSettings.UnreachableDatabase));
        using var client = factory.CreateClient();
        var json = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        return ApiOperation.FromDocument(JsonNode.Parse(json)!);
    }
}
